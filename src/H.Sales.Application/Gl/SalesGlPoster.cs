using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using H.Sales.Gl;
using H.Sales.Invoices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Domain.Repositories;

namespace H.Sales;

/// <summary>
/// Builds a sales invoice journal from a posted SalesInvoice and sends it to H.Accounting GL.
///
/// Journal shape:
///
///   INVOICE POST:
///     DEBIT  AccountsReceivableAccount   grandTotal   — customer owes us
///     CREDIT SalesRevenueAccount          grandTotal   — revenue recognized
///
/// For simplicity the full GrandTotal (including tax) is posted as a single debit leg;
/// tax split can be extended later using a TaxPayableAccount option.
///
/// SourceKey = "si:{id}" — idempotent via Accounting SourceKey uniqueness contract.
/// SourceModule = "Sales"
/// </summary>
public class SalesGlPoster
{
    private readonly AccountingGlClient _glClient;
    private readonly IOptions<SalesGlOptions> _options;
    private readonly IRepository<SalesInvoice, Guid> _invoices;
    private readonly ILogger<SalesGlPoster> _logger;

    public SalesGlPoster(
        AccountingGlClient glClient,
        IOptions<SalesGlOptions> options,
        IRepository<SalesInvoice, Guid> invoices,
        ILogger<SalesGlPoster> logger)
    {
        _glClient = glClient;
        _options  = options;
        _invoices = invoices;
        _logger   = logger;
    }

    /// <summary>
    /// Posts (or skips) the sales invoice GL journal.
    /// Never throws — logs and returns null on failure.
    /// </summary>
    /// <param name="invoice">The posted SalesInvoice entity (must already be saved).</param>
    /// <param name="tenantId">Tenant ID forwarded to the Accounting API header.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>GL journal number if posted successfully, null otherwise.</returns>
    public async Task<string?> PostInvoiceJournalAsync(
        SalesInvoice invoice,
        Guid? tenantId,
        CancellationToken ct = default)
    {
        var opts = _options.Value;

        var grandTotal = invoice.GrandTotal;
        if (grandTotal <= 0m)
        {
            _logger.LogInformation("[Sales→GL] Invoice {Number} has zero grand total — skipping GL post.", invoice.Number);
            return null;
        }

        var sourceKey = BuildSourceKey(invoice.Id);

        // Idempotency guard on Sales side
        if (!string.IsNullOrWhiteSpace(invoice.JournalNumber))
        {
            _logger.LogInformation("[Sales→GL] Invoice {Number} already has GL journal {Journal} — skipping.",
                invoice.Number, invoice.JournalNumber);
            return invoice.JournalNumber;
        }

        var date  = invoice.DocDate.ToString("yyyy-MM-dd");
        var lines = BuildLines(opts, grandTotal);

        if (lines.Count == 0)
        {
            _logger.LogWarning("[Sales→GL] No GL lines could be built for invoice {Number} — skipping.", invoice.Number);
            return null;
        }

        var request = new CreateGlJournalRequest
        {
            Date         = date,
            Ref          = invoice.Number,
            Type         = opts.JournalType,
            DescAr       = $"فاتورة مبيعات {invoice.Number}",
            DescEn       = $"Sales invoice {invoice.Number}",
            Status       = "posted",
            SourceModule = "Sales",
            SourceKey    = sourceKey,
            Lines        = lines
        };

        var (success, journalNumber, alreadyExisted) = await _glClient.CreateJournalAsync(request, tenantId, ct);

        if (success)
        {
            var effective = !string.IsNullOrWhiteSpace(journalNumber)
                ? journalNumber!
                : $"ACC-{sourceKey}";
            try
            {
                invoice.JournalNumber = effective;
                await _invoices.UpdateAsync(invoice, autoSave: true);
            }
            catch (Exception ex)
            {
                // Non-fatal: GL already posted; we just couldn't persist the reference locally.
                _logger.LogWarning(ex, "[Sales→GL] GL posted ({Journal}) but could not save JournalNumber on invoice {Number}.",
                    effective, invoice.Number);
            }

            return invoice.JournalNumber ?? effective;
        }

        _logger.LogWarning("[Sales→GL] GL post failed for invoice {Number}. Invoice is posted but no journal was created.", invoice.Number);
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Stable source key for idempotency.</summary>
    public static string BuildSourceKey(Guid invoiceId) => $"si:{invoiceId}";

    private static List<GlLineRequest> BuildLines(SalesGlOptions opts, decimal grandTotal)
    {
        var lines = new List<GlLineRequest>();

        if (string.IsNullOrWhiteSpace(opts.AccountsReceivableAccount) ||
            string.IsNullOrWhiteSpace(opts.SalesRevenueAccount))
            return lines;

        var cc = string.IsNullOrWhiteSpace(opts.CostCenterCode) ? null : opts.CostCenterCode.Trim();
        // DEBIT: Accounts Receivable (customer owes)
        // CREDIT: Sales Revenue (revenue recognized — requires cost center)
        lines.Add(new GlLineRequest { Acct = opts.AccountsReceivableAccount, Debit  = grandTotal, Desc = "مدينون — عملاء" });
        lines.Add(new GlLineRequest { Acct = opts.SalesRevenueAccount, Credit = grandTotal, Desc = "إيراد مبيعات", Cc = cc });

        return lines;
    }
}
