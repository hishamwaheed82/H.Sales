namespace H.Sales.Inventory;

/// <summary>
/// Configuration for Sales → Inventory stock-issue posting.
/// Section name: "SalesInventory"
///
/// Mirrors the SalesGlOptions shape so both integrations share the same
/// machine-client pattern (H_Ops_Machine client_credentials).
/// </summary>
public class SalesInventoryOptions
{
    public const string SectionName = "SalesInventory";

    // ── Inventory API ─────────────────────────────────────────────────────────

    /// <summary>Base URL of the H.Inventory HttpApi.Host, e.g. https://localhost:44420</summary>
    public string InventoryBaseUrl { get; set; } = string.Empty;

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

    /// <summary>
    /// Scope requested in the client_credentials grant. Must include H.Inventory
    /// (H_Ops_Machine must be seeded with this scope in H.Auth).
    /// </summary>
    public string Scope { get; set; } = "H.Inventory";

    // ── Behaviour ─────────────────────────────────────────────────────────────

    /// <summary>
    /// When true, a failure to create the stock-issue document causes a
    /// BusinessException and the invoice post is rolled back.
    /// Default: false (best-effort — log warning and continue).
    /// </summary>
    public bool RequireStock { get; set; } = false;
}
