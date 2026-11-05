using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace H.Sales.Invoices;

public class SalesInvoiceManager : DomainService
{
    private readonly IRepository<SalesInvoice, Guid> _invoices;

    public SalesInvoiceManager(IRepository<SalesInvoice, Guid> invoices)
    {
        _invoices = invoices;
    }

    public async Task<string> NextNumberAsync()
    {
        var count = await _invoices.CountAsync();
        return $"SI-{(count + 1):D6}";
    }

    /// <summary>
    /// Marks the invoice Posted (domain status only).
    /// Inventory StockIssue + GL (SalesGlPoster / SourceKey si:{id}) run in the Application layer after this.
    /// </summary>
    public async Task PostAsync(SalesInvoice invoice, Guid? userId)
    {
        Check.NotNull(invoice, nameof(invoice));

        if (invoice.Status == SalesInvoiceStatus.Posted)
            throw new BusinessException(SALESErrorCodes.InvoiceAlreadyPosted);

        if (invoice.Status != SalesInvoiceStatus.Draft)
            throw new BusinessException(SALESErrorCodes.InvoiceNotDraft);

        if (!invoice.Lines.Any())
            throw new BusinessException(SALESErrorCodes.NoLines);

        foreach (var line in invoice.Lines)
            line.EnsureValid();

        invoice.RecalcTotals();
        invoice.Status = SalesInvoiceStatus.Posted;

        await _invoices.UpdateAsync(invoice);
    }

    /// <summary>
    /// Cancels a Draft invoice. Only Draft → Cancelled is allowed.
    /// </summary>
    public async Task CancelAsync(SalesInvoice invoice)
    {
        Check.NotNull(invoice, nameof(invoice));

        if (invoice.Status != SalesInvoiceStatus.Draft)
            throw new BusinessException(SALESErrorCodes.InvoiceNotCancellable)
                .WithData("Status", invoice.Status.ToString());

        invoice.Status = SalesInvoiceStatus.Cancelled;
        await _invoices.UpdateAsync(invoice);
    }
}

