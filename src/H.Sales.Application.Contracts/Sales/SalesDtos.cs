using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace H.Sales.Sales;

// ── Customer ──────────────────────────────────────────────────────────────────

public class CustomerDto
{
    public Guid   Id       { get; set; }
    public string Code     { get; set; } = "";
    public string Name     { get; set; } = "";
    public bool   IsActive { get; set; }
}

public class CreateUpdateCustomerDto
{
    [Required, MaxLength(32)]  public string Code     { get; set; } = "";
    [Required, MaxLength(256)] public string Name     { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

// ── Invoice Lines ─────────────────────────────────────────────────────────────

public class SalesInvoiceLineDto
{
    public Guid?   Id          { get; set; }
    public int     LineNumber  { get; set; }
    public Guid?   ItemId      { get; set; }
    public string? Description { get; set; }
    public decimal Qty         { get; set; }
    public decimal UnitPrice   { get; set; }
    public decimal LineTotal   { get; set; }
}

public class CreateSalesInvoiceLineDto
{
    public Guid?   ItemId      { get; set; }
    public string? Description { get; set; }
    public decimal Qty         { get; set; }
    public decimal UnitPrice   { get; set; }
}

// ── Invoice ───────────────────────────────────────────────────────────────────

public class SalesInvoiceDto
{
    public Guid                       Id              { get; set; }
    public string                     Number          { get; set; } = "";
    public DateTime                   DocDate         { get; set; }
    public Guid                       CustomerId      { get; set; }
    public string?                    CustomerCode    { get; set; }
    public string?                    CustomerName    { get; set; }
    public Guid?                      WarehouseId     { get; set; }
    public SalesInvoiceStatus         Status          { get; set; }
    public decimal                    SubTotal        { get; set; }
    public decimal                    TaxTotal        { get; set; }
    public decimal                    GrandTotal      { get; set; }
    public string?                    JournalNumber   { get; set; }
    public string?                    StockDocumentId { get; set; }
    public List<SalesInvoiceLineDto>  Lines           { get; set; } = new();
}

public class CreateSalesInvoiceDto
{
    public DateTime DocDate      { get; set; } = DateTime.Today;
    [Required] public Guid CustomerId { get; set; }
    public Guid?    WarehouseId  { get; set; }
    public decimal  TaxTotal     { get; set; }
    /// <summary>When true the invoice is posted immediately after creation.</summary>
    public bool     Post         { get; set; }
    public List<CreateSalesInvoiceLineDto> Lines { get; set; } = new();
}

// ── Inventory lookups (best-effort via H.Inventory machine client) ────────────

public class WarehouseLookupDto
{
    public Guid   Id        { get; set; }
    public string Code      { get; set; } = "";
    public string Name      { get; set; } = "";
    public bool   IsDefault { get; set; }
    public bool   IsActive  { get; set; }
}

public class ItemLookupDto
{
    public Guid   Id       { get; set; }
    public string Code     { get; set; } = "";
    public string Name     { get; set; } = "";
    public bool   IsActive { get; set; }
}

// ── Bootstrap ─────────────────────────────────────────────────────────────────

public class SalesBootstrapDto
{
    public List<CustomerDto>         Customers      { get; set; } = new();
    public List<SalesInvoiceDto>     RecentInvoices { get; set; } = new();
    /// <summary>From Inventory (H_Ops_Machine). Empty when Inventory is unavailable.</summary>
    public List<WarehouseLookupDto>  Warehouses     { get; set; } = new();
    /// <summary>From Inventory (H_Ops_Machine). Empty when Inventory is unavailable.</summary>
    public List<ItemLookupDto>       Items          { get; set; } = new();
}
