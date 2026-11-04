using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using H.Sales.Inventory;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace H.Sales;

/// <summary>
/// Typed HTTP client for H.Inventory Stock-Documents API.
/// Acquires a machine token (client_credentials) from H.Auth and caches it
/// until 60 seconds before expiry to avoid per-request round-trips.
/// Mirrors the same pattern as AccountingGlClient.
/// </summary>
public class InventoryStockClient
{
    private const string TokenCacheKey = "SalesInventory_MachineToken";

    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptions<SalesInventoryOptions> _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<InventoryStockClient> _logger;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

    public InventoryStockClient(
        IHttpClientFactory httpFactory,
        IOptions<SalesInventoryOptions> options,
        IMemoryCache cache,
        ILogger<InventoryStockClient> logger,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _httpFactory   = httpFactory;
        _options       = options;
        _cache         = cache;
        _logger        = logger;
        _configuration = configuration;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and immediately posts a stock document via H.Inventory API.
    /// Returns (success, documentId).
    /// Never throws — returns (false, null) on failure after logging.
    /// </summary>
    public async Task<(bool Success, Guid? DocumentId)> CreateAndPostAsync(
        CreateStockDocumentRequest request,
        Guid? tenantId,
        CancellationToken ct = default)
    {
        var opts = _options.Value;

        if (string.IsNullOrWhiteSpace(opts.InventoryBaseUrl))
        {
            _logger.LogWarning("[Sales→Inventory] SalesInventory:InventoryBaseUrl not configured — skipping stock post for Reference={Reference}", request.Reference);
            return (false, null);
        }

        string? token;
        try
        {
            token = await GetMachineTokenAsync(opts, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Sales→Inventory] Could not obtain machine token from H.Auth — skipping stock post for Reference={Reference}", request.Reference);
            return (false, null);
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("[Sales→Inventory] Empty machine token — skipping stock post for Reference={Reference}", request.Reference);
            return (false, null);
        }

        try
        {
            using var client = _httpFactory.CreateClient("H.Inventory.Stock");
            client.BaseAddress = new Uri(opts.InventoryBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Forward tenant header so the stock document lands in the correct tenant DB.
            if (tenantId.HasValue)
                client.DefaultRequestHeaders.TryAddWithoutValidation("__tenant", tenantId.Value.ToString());

            // Step 1: Create the document (with Post=true so Inventory creates+posts in one call).
            using var createResponse = await client.PostAsJsonAsync("api/v1/documents", request, ct);
            var createBody = await createResponse.Content.ReadAsStringAsync(ct);

            if (!createResponse.IsSuccessStatusCode)
            {
                // 409 / 422 — treat as "already posted" idempotency
                if ((int)createResponse.StatusCode == 409 || (int)createResponse.StatusCode == 422)
                {
                    _logger.LogWarning("[Sales→Inventory] Inventory returned {Status} for Reference={Reference} — treating as already posted. Body: {Body}",
                        (int)createResponse.StatusCode, request.Reference, createBody);
                    return (true, null);
                }

                _logger.LogError("[Sales→Inventory] Inventory returned {Status} for Reference={Reference}. Body: {Body}",
                    (int)createResponse.StatusCode, request.Reference, createBody);
                return (false, null);
            }

            // Parse document id / status from response.
            // Status may be string ("Posted") or numeric enum (Posted=2).
            using var doc = JsonDocument.Parse(createBody);
            var root = doc.RootElement;

            Guid? docId = root.TryGetProperty("id", out var idProp) && idProp.TryGetGuid(out var parsedId)
                ? parsedId
                : null;

            var alreadyPosted = IsPostedStatus(root);
            var journalNumber = root.TryGetProperty("journalNumber", out var jnProp) ? jnProp.GetString() : null;

            _logger.LogInformation(
                "[Sales→Inventory] Stock document created. Id={DocId} AlreadyPosted={AlreadyPosted} JournalNumber={JournalNumber} Reference={Reference}",
                docId, alreadyPosted, journalNumber, request.Reference);

            // Step 2: Only post explicitly when Create left the document as Draft
            // (Post=true not honoured by older Inventory builds).
            if (docId.HasValue && !alreadyPosted)
            {
                using var postResponse = await client.PostAsync($"api/v1/documents/{docId.Value}/post", null, ct);
                var postBody = await postResponse.Content.ReadAsStringAsync(ct);

                if (!postResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[Sales→Inventory] Stock document {DocId} created but post step returned {Status}. Body: {Body}",
                        docId, (int)postResponse.StatusCode, postBody);
                    // Document created (we have the id) — treat as partial success
                    return (true, docId);
                }

                _logger.LogInformation("[Sales→Inventory] Stock document {DocId} posted successfully.", docId);
            }

            return (true, docId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Sales→Inventory] HTTP call to Inventory failed for Reference={Reference}", request.Reference);
            return (false, null);
        }
    }

    /// <summary>
    /// True when Create response already reflects StockDocumentStatus.Posted
    /// (string "Posted"/"posted" or numeric enum value 2).
    /// </summary>
    private static bool IsPostedStatus(JsonElement root)
    {
        if (!root.TryGetProperty("status", out var statusProp))
            return false;

        if (statusProp.ValueKind == JsonValueKind.Number && statusProp.TryGetInt32(out var n))
            return n == 2; // StockDocumentStatus.Posted

        if (statusProp.ValueKind == JsonValueKind.String)
        {
            var s = statusProp.GetString();
            return string.Equals(s, "posted", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(s, "2", StringComparison.Ordinal);
        }

        return false;
    }

    // ── Token acquisition ─────────────────────────────────────────────────────

    private async Task<string?> GetMachineTokenAsync(SalesInventoryOptions opts, CancellationToken ct)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;

        var authority = (_configuration["AuthServer:Authority"] ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(authority))
            throw new InvalidOperationException("AuthServer:Authority is not configured.");

        using var client = _httpFactory.CreateClient("H.Auth");
        var form = new System.Collections.Generic.Dictionary<string, string>
        {
            ["grant_type"]    = "client_credentials",
            ["client_id"]     = opts.ClientId,
            ["client_secret"] = opts.ClientSecret,
            ["scope"]         = opts.Scope
        };

        using var tokenResponse = await client.PostAsync(
            authority + "/connect/token",
            new FormUrlEncodedContent(form), ct);

        var tokenBody = await tokenResponse.Content.ReadAsStringAsync(ct);
        if (!tokenResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"H.Auth returned {(int)tokenResponse.StatusCode} for machine token: {tokenBody}");

        using var tokenDoc  = JsonDocument.Parse(tokenBody);
        var accessToken     = tokenDoc.RootElement.GetProperty("access_token").GetString();
        var expiresIn       = tokenDoc.RootElement.TryGetProperty("expires_in", out var expProp)
            ? expProp.GetInt32()
            : 3600;

        // Cache until 60 s before expiry.
        var ttl = TimeSpan.FromSeconds(Math.Max(expiresIn - 60, 10));
        _cache.Set(TokenCacheKey, accessToken, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });

        return accessToken;
    }
}

// ── Request DTO (mirrors CreateStockDocumentDto in H.Inventory) ───────────────

public class CreateStockDocumentRequest
{
    /// <summary>StockDocumentType as int: Receipt=1, Issue=2, Transfer=3.</summary>
    public int DocType { get; set; } = 2;
    /// <summary>Maps to CreateStockDocumentDto.DocDate (ISO date preferred).</summary>
    public string DocDate { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string? Reference { get; set; }
    public string? Notes     { get; set; }
    /// <summary>When true, Inventory creates and immediately posts the document.</summary>
    public bool Post { get; set; } = true;
    public System.Collections.Generic.List<StockDocumentLineRequest> Lines { get; set; } = new();
}

public class StockDocumentLineRequest
{
    public Guid    ItemId   { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? Notes    { get; set; }
}
