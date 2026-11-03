using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using H.Sales.Invoices;
using H.Sales.Customers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace H.Sales.Sales;

[Authorize]
public class SalesAppService : H.Sales.SalesAppService, ISalesAppService
{
    private readonly IRepository<Customer, Guid>        _customers;
    private readonly IRepository<SalesInvoice, Guid>    _invoices;
    private readonly SalesInvoiceManager                _manager;
    private readonly IGuidGenerator                     _guids;
    private readonly SalesGlPoster                      _glPoster;

    public SalesAppService(
        IRepository<Customer, Guid>     customers,
        IRepository<SalesInvoice, Guid> invoices,
        SalesInvoiceManager             manager,
        IGuidGenerator                  guids,
        SalesGlPoster                   glPoster)
    {
        _customers = customers;
        _invoices  = invoices;
        _manager   = manager;
        _guids     = guids;
        _glPoster  = glPoster;
    }

    // ── Bootstrap ─────────────────────────────────────────────────────────────

    public virtual async Task<SalesBootstrapDto> GetBootstrapAsync()
    {
        var customers = await _customers.GetListAsync();
        var query     = await _invoices.WithDetailsAsync(i => i.Lines);
        var recent    = (await AsyncExecuter.ToListAsync(query))
            .OrderByDescending(i => i.DocDate).ThenByDescending(i => i.Number)
            .Take(40).ToList();

        return new SalesBootstrapDto
        {
            Customers      = customers.Select(MapCustomer).ToList(),
            RecentInvoices = recent.Select(i => MapInvoice(i, customers)).ToList()
        };
    }

    // ── Customer CRUD ─────────────────────────────────────────────────────────

    public virtual async Task<CustomerDto> CreateCustomerAsync(CreateUpdateCustomerDto input)
    {
        if (await _customers.AnyAsync(s => s.Code == input.Code))
            throw new BusinessException(SALESErrorCodes.DuplicateCustomerCode).WithData("Code", input.Code);

        var customer = new Customer(_guids.Create())
        {
            TenantId = CurrentTenant.Id,
            Code     = input.Code.Trim(),
            Name     = input.Name.Trim(),
            IsActive = input.IsActive
        };
        customer.EnsureValid();
        await _customers.InsertAsync(customer, autoSave: true);
        return MapCustomer(customer);
    }

    public virtual async Task<CustomerDto> UpdateCustomerAsync(Guid id, CreateUpdateCustomerDto input)
    {
        var customer = await _customers.GetAsync(id);
        if (await _customers.AnyAsync(s => s.Code == input.Code && s.Id != id))
            throw new BusinessException(SALESErrorCodes.DuplicateCustomerCode).WithData("Code", input.Code);

        customer.Code     = input.Code.Trim();
        customer.Name     = input.Name.Trim();
        customer.IsActive = input.IsActive;
        customer.EnsureValid();
        await _customers.UpdateAsync(customer, autoSave: true);
        return MapCustomer(customer);
    }

    // ── Invoice CRUD ──────────────────────────────────────────────────────────

    public virtual async Task<SalesInvoiceDto> CreateInvoiceAsync(CreateSalesInvoiceDto input)
    {
        if (!await _customers.AnyAsync(s => s.Id == input.CustomerId))
            throw new BusinessException(SALESErrorCodes.CustomerNotFound);

        if (!input.Lines.Any())
            throw new BusinessException(SALESErrorCodes.NoLines);

        var customers = await _customers.GetListAsync();

        var invoice = new SalesInvoice(_guids.Create())
        {
            TenantId    = CurrentTenant.Id,
            Number      = await _manager.NextNumberAsync(),
            DocDate     = input.DocDate.Date,
            CustomerId  = input.CustomerId,
            WarehouseId = input.WarehouseId,
            TaxTotal    = input.TaxTotal,
            Status      = SalesInvoiceStatus.Draft
        };

        var n = 1;
        foreach (var l in input.Lines)
        {
            var line = new SalesInvoiceLine(_guids.Create())
            {
                InvoiceId   = invoice.Id,
                LineNumber  = n++,
                ItemId      = l.ItemId,
                Description = l.Description,
                Qty         = l.Qty,
                UnitPrice   = l.UnitPrice
            };
            line.EnsureValid();
            invoice.Lines.Add(line);
        }

        invoice.RecalcTotals();
        await _invoices.InsertAsync(invoice, autoSave: true);

        if (input.Post)
        {
            await _manager.PostAsync(invoice, CurrentUser.Id);
            // GL post — fire-and-forget on failure (non-blocking; logs warning on error).
            await _glPoster.PostInvoiceJournalAsync(invoice, CurrentTenant.Id);
        }

        return MapInvoice(invoice, customers);
    }

    public virtual async Task<SalesInvoiceDto> PostInvoiceAsync(Guid id)
    {
        var query   = await _invoices.WithDetailsAsync(i => i.Lines);
        var where   = query.Where(i => i.Id == id);
        var invoice = await AsyncExecuter.FirstOrDefaultAsync(where)
            ?? throw new BusinessException(SALESErrorCodes.InvoiceNotFound);

        await _manager.PostAsync(invoice, CurrentUser.Id);

        // GL post — fire-and-forget on failure (non-blocking; logs warning on error).
        await _glPoster.PostInvoiceJournalAsync(invoice, CurrentTenant.Id);

        var customers = await _customers.GetListAsync();
        return MapInvoice(invoice, customers);
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static CustomerDto MapCustomer(Customer s) => new()
    {
        Id       = s.Id,
        Code     = s.Code,
        Name     = s.Name,
        IsActive = s.IsActive
    };

    private static SalesInvoiceDto MapInvoice(SalesInvoice i, IEnumerable<Customer> customers)
    {
        var map = customers.ToDictionary(s => s.Id);
        var cus = map.GetValueOrDefault(i.CustomerId);
        return new SalesInvoiceDto
        {
            Id              = i.Id,
            Number          = i.Number,
            DocDate         = i.DocDate,
            CustomerId      = i.CustomerId,
            CustomerCode    = cus?.Code,
            CustomerName    = cus?.Name,
            WarehouseId     = i.WarehouseId,
            Status          = i.Status,
            SubTotal        = i.SubTotal,
            TaxTotal        = i.TaxTotal,
            GrandTotal      = i.GrandTotal,
            JournalNumber   = i.JournalNumber,
            StockDocumentId = i.StockDocumentId,
            Lines = i.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new SalesInvoiceLineDto
                {
                    Id          = l.Id,
                    LineNumber  = l.LineNumber,
                    ItemId      = l.ItemId,
                    Description = l.Description,
                    Qty         = l.Qty,
                    UnitPrice   = l.UnitPrice,
                    LineTotal   = l.LineTotal
                }).ToList()
        };
    }
}
