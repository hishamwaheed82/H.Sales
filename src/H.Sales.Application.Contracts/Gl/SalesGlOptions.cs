namespace H.Sales.Gl;

/// <summary>
/// Configuration for Sales → Accounting GL posting.
/// Section name: "SalesGl"
///
/// Demo / default account codes (Chart of Accounts) can be overridden per deployment.
/// All codes must exist in H.Accounting for the tenant before invoice-post fires.
/// </summary>
public class SalesGlOptions
{
    public const string SectionName = "SalesGl";

    // ── Accounting API ────────────────────────────────────────────────────────

    /// <summary>Base URL of the H.Accounting HttpApi.Host, e.g. https://localhost:44400</summary>
    public string AccountingBaseUrl { get; set; } = string.Empty;

    // ── Machine client (client_credentials) ──────────────────────────────────

    /// <summary>
    /// OAuth client_id for the Ops machine client.
    /// Default: H_Ops_Machine (shared cross-module machine client seeded in H.Auth).
    /// </summary>
    public string ClientId { get; set; } = "H_Ops_Machine";

    /// <summary>
    /// OAuth client_secret.
    /// Store in appsettings.secrets.json or environment variable; never commit.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Scope requested in the client_credentials grant. Accounting API audience.</summary>
    public string Scope { get; set; } = "H.Accounting";

    // ── Journal type ─────────────────────────────────────────────────────────

    /// <summary>Journal type code. Must exist in Accounting journal-types.</summary>
    public string JournalType { get; set; } = "GJ";

    // ── Default account codes (demo Chart of Accounts) ────────────────────────

    /// <summary>
    /// Accounts Receivable account debited on invoice post (customer owes us).
    /// Demo default: 1201 (Trade Receivables / Accounts Receivable).
    /// </summary>
    public string AccountsReceivableAccount { get; set; } = "1201";

    /// <summary>
    /// Sales revenue / income account credited on invoice post.
    /// Demo default: 4101 (Sales Revenue).
    /// </summary>
    public string SalesRevenueAccount { get; set; } = "4101";
}
