using System;
using System.Collections.Generic;
using System.Linq;
using H.Platform.Common;
using Volo.Abp;

namespace H.Sales.Invoices;

/// <summary>
/// Sales invoice header.
/// Workflow: Draft → Posted.
/// When posted:
///   - TODO: Call H.Inventory StockIssue hook (SourceModule=Sales, SourceKey=si:{id})
///   - TODO: Call H.Accounting GL post hook (SourceModule=Sales, SourceKey=si:{id})
///           DEBIT AR, CREDIT Revenue / Sales Income
/// </summary>
public class SalesInvoice : HTrackedAggregateRoot<Guid>
{
    public string Number { get; set; } = string.Empty;

    public DateTime DocDate { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>Source warehouse for the stock issue (optional in draft).</summary>
    public Guid? WarehouseId { get; set; }

    public SalesInvoiceStatus Status { get; set; } = SalesInvoiceStatus.Draft;

    public decimal SubTotal { get; set; }

    public decimal TaxTotal { get; set; }

    public decimal GrandTotal { get; set; }

    /// <summary>GL journal number, populated after Accounting integration (SalesGlPoster).</summary>
    public string? JournalNumber { get; set; }

    /// <summary>Stock issue document ID returned from Inventory integration (TODO).</summary>
    public string? StockDocumentId { get; set; }

    public ICollection<SalesInvoiceLine> Lines { get; set; } = new List<SalesInvoiceLine>();

    protected SalesInvoice() { }

    public SalesInvoice(Guid id) : base(id) { }

    public void EnsureCanEdit()
    {
        if (Status != SalesInvoiceStatus.Draft)
            throw new BusinessException(SALESErrorCodes.InvoiceNotDraft);
    }

    public void RecalcTotals()
    {
        SubTotal   = Lines.Sum(l => l.LineTotal);
        GrandTotal = SubTotal + TaxTotal;
    }
}

/// <summary>
/// One line of a sales invoice.
/// Either ItemId (stock item) or Description (free-text service) must be provided.
/// </summary>
public class SalesInvoiceLine : HTrackedAggregateRoot<Guid>
{
    public Guid InvoiceId { get; set; }

    public int LineNumber { get; set; }

    /// <summary>Inventory item (optional; null for free-text service lines).</summary>
    public Guid? ItemId { get; set; }

    public string? Description { get; set; }

    public decimal Qty { get; set; }

    /// <summary>Unit selling price (excluding tax).</summary>
    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }

    public SalesInvoice? Invoice { get; set; }

    protected SalesInvoiceLine() { }

    public SalesInvoiceLine(Guid id) : base(id) { }

    public void EnsureValid()
    {
        if (Qty <= 0)
            throw new BusinessException(SALESErrorCodes.InvalidQuantity);
        if (UnitPrice < 0)
            throw new BusinessException(SALESErrorCodes.InvalidUnitCost);
        LineTotal = Qty * UnitPrice;
    }
}
