using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using H.Sales.Gl;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace H.Sales;

/// <summary>
/// Typed HTTP client for H.Accounting GL API.
/// Acquires a machine token (client_credentials) from H.Auth and caches it
/// until 60 seconds before expiry to avoid per-request round-trips.
/// Mirrors the same pattern as H.Inventory / H.Treasury AccountingGlClient.
/// </summary>
public class AccountingGlClient
{
    private const string TokenCacheKey = "SalesGl_MachineToken";

    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptions<SalesGlOptions> _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AccountingGlClient> _logger;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

    public AccountingGlClient(
        IHttpClientFactory httpFactory,
        IOptions<SalesGlOptions> options,
        IMemoryCache cache,
        ILogger<AccountingGlClient> logger,
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
    /// POSTs a new journal to Accounting.
    /// Returns (success, journalNumber, alreadyExisted).
    /// Never throws: returns (false, null, false) on failure after logging.
    /// </summary>
    public async Task<(bool Success, string? JournalNumber, bool AlreadyExisted)> CreateJournalAsync(
        CreateGlJournalRequest request,
        Guid? tenantId,
        CancellationToken ct = default)
    {
        var opts = _options.Value;

        if (string.IsNullOrWhiteSpace(opts.AccountingBaseUrl))
        {
            _logger.LogWarning("[SALES→GL] SalesGl:AccountingBaseUrl not configured — skipping GL post for SourceKey={SourceKey}", request.SourceKey);
            return (false, null, false);
        }

        string? token;
        try
        {
            token = await GetMachineTokenAsync(opts, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SALES→GL] Could not obtain machine token from H.Auth — skipping GL post for SourceKey={SourceKey}", request.SourceKey);
            return (false, null, false);
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("[SALES→GL] Empty machine token — skipping GL post for SourceKey={SourceKey}", request.SourceKey);
            return (false, null, false);
        }

        try
        {
            using var client = _httpFactory.CreateClient("H.Accounting.GL");
            client.BaseAddress = new Uri(opts.AccountingBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Forward tenant header so the GL entry lands in the correct tenant DB.
            if (tenantId.HasValue)
                client.DefaultRequestHeaders.TryAddWithoutValidation("__tenant", tenantId.Value.ToString());

            using var response = await client.PostAsJsonAsync("api/v1/journals", request, ct);

            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                using var doc   = JsonDocument.Parse(body);
                var root        = doc.RootElement;
                string? number = null;
                if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    number = idProp.GetString();
                if (string.IsNullOrWhiteSpace(number) && root.TryGetProperty("number", out var n))
                    number = n.GetString();
                var already     = root.TryGetProperty("alreadyExisted", out var ae) && ae.ValueKind == JsonValueKind.True;
                _logger.LogInformation("[SALES→GL] Journal posted. Number={Number} AlreadyExisted={Already} SourceKey={SourceKey}",
                    number, already, request.SourceKey);
                return (true, number, already);
            }

            // HTTP 409/422 from Accounting with SourceKeyAlreadyPosted → treat as already-existed
            if ((int)response.StatusCode == 409 || (int)response.StatusCode == 422)
            {
                _logger.LogWarning("[SALES→GL] Accounting returned {Status} for SourceKey={SourceKey} — treating as already posted. Body: {Body}",
                    (int)response.StatusCode, request.SourceKey, body);
                return (true, null, true);
            }

            _logger.LogError("[SALES→GL] Accounting returned {Status} for SourceKey={SourceKey}. Body: {Body}",
                (int)response.StatusCode, request.SourceKey, body);
            return (false, null, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SALES→GL] HTTP call to Accounting failed for SourceKey={SourceKey}", request.SourceKey);
            return (false, null, false);
        }
    }

    // ── Token acquisition ─────────────────────────────────────────────────────

    private async Task<string?> GetMachineTokenAsync(SalesGlOptions opts, CancellationToken ct)
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

// ── Request DTO (mirrors CreateJournalInput + SourceModule/SourceKey) ─────────

public class CreateGlJournalRequest
{
    public string Date         { get; set; } = string.Empty;
    public string? Ref         { get; set; }
    public string Type         { get; set; } = "GJ";
    public string DescAr       { get; set; } = string.Empty;
    public string? DescEn      { get; set; }
    public string Status       { get; set; } = "posted";
    public string SourceModule  { get; set; } = "SALES";
    public string SourceKey    { get; set; } = string.Empty;
    public System.Collections.Generic.List<GlLineRequest> Lines { get; set; } = new();
}

public class GlLineRequest
{
    public string  Acct   { get; set; } = string.Empty;
    public decimal Debit  { get; set; }
    public decimal Credit { get; set; }
    public string? Desc   { get; set; }
    public string? Cc     { get; set; }
}

