using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using H.Sales.Inventory;
using H.Sales.Invoices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace H.Sales;

/// <summary>
/// Creates and posts an Issue stock document in H.Inventory when a
/// SalesInvoice is posted, for every line that has an ItemId set and
/// when the invoice header has a WarehouseId.
///
/// Source-key reference on the stock document:  si:{invoiceId}
/// DocType: Issue  (stock out — goods issued to customer)
///
/// Non-blocking by default (RequireStock=false):
///   - Missing InventoryBaseUrl → warning, skip.
///   - Any HTTP failure → log error, do NOT throw (unless RequireStock=true).
/// </summary>
public class SalesStockPoster
{
    private readonly InventoryStockClient _stockClient;
    private readonly IOptions<SalesInventoryOptions> _options;
    private readonly IRepository<SalesInvoice, Guid> _invoices;
    private readonly ILogger<SalesStockPoster> _logger;

    public SalesStockPoster(
        InventoryStockClient stockClient,
        IOptions<SalesInventoryOptions> options,
        IRepository<SalesInvoice, Guid> invoices,
        ILogger<SalesStockPoster> logger)
    {
        _stockClient = stockClient;
        _options     = options;
        _invoices    = invoices;
        _logger      = logger;
    }

    /// <summary>
    /// Posts (or skips) the sales invoice Issue stock document.
    /// Never throws when RequireStock=false — logs and returns null on failure.
    /// </summary>
    /// <param name="invoice">The posted SalesInvoice entity (must already be saved).</param>
    /// <param name="tenantId">Tenant ID forwarded to the Inventory API header.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Stock document ID if posted successfully, null otherwise.</returns>
    public async Task<Guid?> PostIssueAsync(
        SalesInvoice invoice,
        Guid? tenantId,
        CancellationToken ct = default)
    {
        var opts = _options.Value;

        // ── Pre-conditions ────────────────────────────────────────────────────

        if (!invoice.WarehouseId.HasValue)
        {
            _logger.LogInformation("[Sales→Inventory] Invoice {Number} has no WarehouseId — skipping stock issue.", invoice.Number);
            return null;
        }

        var stockLines = invoice.Lines
            .Where(l => l.ItemId.HasValue)
            .ToList();

        if (stockLines.Count == 0)
        {
            _logger.LogInformation("[Sales→Inventory] Invoice {Number} has no item lines — skipping stock issue.", invoice.Number);
            return null;
        }

        // Idempotency guard
        if (!string.IsNullOrWhiteSpace(invoice.StockDocumentId))
        {
            _logger.LogInformation("[Sales→Inventory] Invoice {Number} already has StockDocumentId={DocId} — skipping.",
                invoice.Number, invoice.StockDocumentId);
            return Guid.TryParse(invoice.StockDocumentId, out var existingId) ? existingId : null;
        }

        var reference = BuildSourceKey(invoice.Id);

        // For the Issue doc we use unit cost = unit price (sale price used as cost reference).
        // Actual COGS / cost calculation lives in Inventory's stock layer.
        var request = new CreateStockDocumentRequest
        {
            DocType     = 2, // Issue
            DocDate     = invoice.DocDate.ToString("yyyy-MM-dd"),
            WarehouseId = invoice.WarehouseId.Value,
            Reference   = reference,
            Notes       = $"فاتورة مبيعات {invoice.Number}",
            Post        = true,
            Lines       = stockLines.Select(l => new StockDocumentLineRequest
            {
                ItemId   = l.ItemId!.Value,
                Quantity = l.Qty,
                UnitCost = l.UnitPrice,   // sale price used as the cost field for the Issue line
                Notes    = l.Description
            }).ToList()
        };

        var (success, documentId) = await _stockClient.CreateAndPostAsync(request, tenantId, ct);

        if (success)
        {
            if (documentId.HasValue)
            {
                try
                {
                    invoice.StockDocumentId = documentId.Value.ToString();
                    await _invoices.UpdateAsync(invoice, autoSave: true);
                }
                catch (Exception ex)
                {
                    // Non-fatal: stock doc already created; just couldn't persist the reference.
                    _logger.LogWarning(ex, "[Sales→Inventory] Stock doc posted ({DocId}) but could not save StockDocumentId on invoice {Number}.",
                        documentId, invoice.Number);
                }
            }

            return documentId;
        }

        // Failure path
        var msg = $"[Sales→Inventory] Stock issue post failed for invoice {invoice.Number}.";
        if (opts.RequireStock)
        {
            _logger.LogError("{Msg} RequireStock=true — throwing BusinessException.", msg);
            throw new BusinessException("H.Sales:StockPostFailed")
                .WithData("InvoiceNumber", invoice.Number)
                .WithData("Reference", reference);
        }

        _logger.LogWarning("{Msg} Invoice is posted but no issue document was created (RequireStock=false).", msg);
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Stable source-key for idempotency. Matches Inventory Reference field.</summary>
    public static string BuildSourceKey(Guid invoiceId) => $"si:{invoiceId}";
}
