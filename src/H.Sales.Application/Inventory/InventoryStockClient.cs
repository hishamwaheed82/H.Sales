using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using H.Sales.Inventory;
using H.Sales.Sales;
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
    /// Best-effort warehouse list from Inventory GET /api/v1/warehouses.
    /// Returns empty list when Inventory is unavailable or misconfigured.
    /// </summary>
    public async Task<List<WarehouseLookupDto>> ListWarehousesAsync(
        Guid? tenantId,
        CancellationToken ct = default)
    {
        var (ok, root) = await TryGetInventoryJsonAsync("api/v1/warehouses", tenantId, ct);
        if (!ok || root.ValueKind == JsonValueKind.Undefined)
            return new List<WarehouseLookupDto>();

        return ParseWarehouseArray(root);
    }

    /// <summary>
    /// Best-effort item list from Inventory GET /api/v1/items.
    /// Returns empty list when Inventory is unavailable or misconfigured.
    /// </summary>
    public async Task<List<ItemLookupDto>> ListItemsAsync(
        Guid? tenantId,
        CancellationToken ct = default)
    {
        var (ok, root) = await TryGetInventoryJsonAsync("api/v1/items", tenantId, ct);
        if (!ok || root.ValueKind == JsonValueKind.Undefined)
            return new List<ItemLookupDto>();

        return ParseItemArray(root);
    }

    private async Task<(bool Ok, JsonElement Root)> TryGetInventoryJsonAsync(
        string relativeUrl,
        Guid? tenantId,
        CancellationToken ct)
    {
        var opts = _options.Value;
        if (string.IsNullOrWhiteSpace(opts.InventoryBaseUrl))
        {
            _logger.LogDebug("[Sales→Inventory] InventoryBaseUrl not configured — lookup {Url} skipped.", relativeUrl);
            return (false, default);
        }

        string? token;
        try
        {
            token = await GetMachineTokenAsync(opts, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Sales→Inventory] Machine token failed — lookup {Url} skipped.", relativeUrl);
            return (false, default);
        }

        if (string.IsNullOrWhiteSpace(token))
            return (false, default);

        try
        {
            using var client = _httpFactory.CreateClient("H.Inventory.Stock");
            client.BaseAddress = new Uri(opts.InventoryBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (tenantId.HasValue)
                client.DefaultRequestHeaders.TryAddWithoutValidation("__tenant", tenantId.Value.ToString());

            using var response = await client.GetAsync(relativeUrl, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Sales→Inventory] Lookup {Url} returned {Status}: {Body}",
                    relativeUrl, (int)response.StatusCode, body);
                return (false, default);
            }

            using var doc = JsonDocument.Parse(body);
            return (true, doc.RootElement.Clone());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Sales→Inventory] Lookup {Url} failed.", relativeUrl);
            return (false, default);
        }
    }

    private static List<WarehouseLookupDto> ParseWarehouseArray(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return new List<WarehouseLookupDto>();

        var list = new List<WarehouseLookupDto>();
        foreach (var el in root.EnumerateArray())
        {
            if (!TryGetGuid(el, "id", out var id))
                continue;
            list.Add(new WarehouseLookupDto
            {
                Id        = id,
                Code      = GetString(el, "code") ?? "",
                Name      = GetString(el, "name") ?? "",
                IsDefault = GetBool(el, "isDefault"),
                IsActive  = GetBool(el, "isActive", defaultValue: true)
            });
        }
        return list.OrderByDescending(w => w.IsDefault).ThenBy(w => w.Code).ToList();
    }

    private static List<ItemLookupDto> ParseItemArray(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return new List<ItemLookupDto>();

        var list = new List<ItemLookupDto>();
        foreach (var el in root.EnumerateArray())
        {
            if (!TryGetGuid(el, "id", out var id))
                continue;
            list.Add(new ItemLookupDto
            {
                Id       = id,
                Code     = GetString(el, "code") ?? "",
                Name     = GetString(el, "name") ?? "",
                IsActive = GetBool(el, "isActive", defaultValue: true)
            });
        }
        return list.OrderBy(i => i.Code).ToList();
    }

    private static bool TryGetGuid(JsonElement el, string name, out Guid id)
    {
        id = default;
        if (!el.TryGetProperty(name, out var prop))
            return false;
        if (prop.ValueKind == JsonValueKind.String && Guid.TryParse(prop.GetString(), out id))
            return true;
        return prop.TryGetGuid(out id);
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static bool GetBool(JsonElement el, string name, bool defaultValue = false)
    {
        if (!el.TryGetProperty(name, out var prop))
            return defaultValue;
        return prop.ValueKind == JsonValueKind.True
               || (prop.ValueKind == JsonValueKind.False ? false : defaultValue);
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
