using System;
using System.Threading.Tasks;
using H.Sales.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace H.Sales.HttpApi.V1;

/// <summary>Stable Vite-friendly routes under /api/v1/*.</summary>
[RemoteService]
[Area("Sales")]
[Route("api/v1")]
[Authorize]
public class SalesV1Controller : SalesController
{
    private readonly ISalesAppService _sales;

    public SalesV1Controller(ISalesAppService sales)
    {
        _sales = sales;
    }

    [HttpGet("bootstrap")]
    public Task<SalesBootstrapDto> BootstrapAsync() =>
        _sales.GetBootstrapAsync();

    // ── Customers ─────────────────────────────────────────────────────────────

    [HttpGet("customers")]
    public async Task<object> CustomersAsync()
    {
        var b = await _sales.GetBootstrapAsync();
        return b.Customers;
    }

    [HttpPost("customers")]
    public Task<CustomerDto> CreateCustomerAsync([FromBody] CreateUpdateCustomerDto input) =>
        _sales.CreateCustomerAsync(input);

    [HttpPut("customers/{id:guid}")]
    public Task<CustomerDto> UpdateCustomerAsync(Guid id, [FromBody] CreateUpdateCustomerDto input) =>
        _sales.UpdateCustomerAsync(id, input);

    // ── Invoices ──────────────────────────────────────────────────────────────

    [HttpGet("invoices")]
    public async Task<object> InvoicesAsync()
    {
        var b = await _sales.GetBootstrapAsync();
        return b.RecentInvoices;
    }

    [HttpPost("invoices")]
    public Task<SalesInvoiceDto> CreateInvoiceAsync([FromBody] CreateSalesInvoiceDto input) =>
        _sales.CreateInvoiceAsync(input);

    [HttpPost("invoices/{id:guid}/post")]
    public Task<SalesInvoiceDto> PostInvoiceAsync(Guid id) =>
        _sales.PostInvoiceAsync(id);
}
