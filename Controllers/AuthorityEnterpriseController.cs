using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SBD.ServiceRegistry;

namespace Gateway.Controllers;

/// <summary>
/// Thin proxy for Phase D authority enterprise endpoints.
/// Covers JIT elevations, break-glass, recertification, risk scoring, and compliance reports.
/// Forwards to AuthorityService (port 5004) via ServiceRegistry.
/// Angular never calls AuthorityService directly.
/// </summary>
[ApiController]
[Authorize]
public class AuthorityEnterpriseController(
    IServiceRegistry registry,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<AuthorityEnterpriseController> logger) : ControllerBase
{
    private async Task<string> GetAuthorityBaseUrl()
    {
        var instances = await registry.GetInstancesAsync("AuthorityService");
        if (instances.Count > 0) return instances[0].BaseUrl;
        var url = configuration["ServiceUrls:AuthorityService"] ?? "http://localhost:5004";
        logger.LogWarning("AuthorityService not in registry, fallback: {Url}", url);
        return url;
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient();
        var bearer = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(bearer))
            client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(bearer);
        return client;
    }

    private async Task<IActionResult> ForwardResponse(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        return StatusCode((int)response.StatusCode, body.Length > 0
            ? JsonSerializer.Deserialize<JsonElement>(body)
            : null);
    }

    // ── JIT Elevations ───────────────────────────────────────────────────────

    /// <summary>GET /api/v1/elevations/me — active JIT elevations for the caller.</summary>
    [HttpGet("api/v1/elevations/me")]
    public async Task<IActionResult> GetMyElevations(CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        return await ForwardResponse(
            await CreateClient().GetAsync($"{baseUrl}/api/v1/elevations/me", ct), ct);
    }

    /// <summary>GET /api/v1/elevations — all elevations (admin view).</summary>
    [HttpGet("api/v1/elevations")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> GetElevations(
        [FromQuery] int? userId = null,
        [FromQuery] bool activeOnly = false,
        CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        var qs = new List<string>();
        if (userId.HasValue) qs.Add($"userId={userId}");
        if (activeOnly) qs.Add("activeOnly=true");
        var url = $"{baseUrl}/api/v1/elevations" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        return await ForwardResponse(await CreateClient().GetAsync(url, ct), ct);
    }

    /// <summary>POST /api/v1/elevations — request a JIT elevation.</summary>
    [HttpPost("api/v1/elevations")]
    public async Task<IActionResult> RequestElevation(
        [FromBody] JsonElement body, CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        var content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        return await ForwardResponse(
            await CreateClient().PostAsync($"{baseUrl}/api/v1/elevations", content, ct), ct);
    }

    /// <summary>DELETE /api/v1/elevations/{id} — revoke a JIT elevation early.</summary>
    [HttpDelete("api/v1/elevations/{id:long}")]
    public async Task<IActionResult> RevokeElevation(long id, CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        return await ForwardResponse(
            await CreateClient().DeleteAsync($"{baseUrl}/api/v1/elevations/{id}", ct), ct);
    }

    // ── Break-Glass ──────────────────────────────────────────────────────────

    /// <summary>POST /api/v1/break-glass — activate break-glass (SuperAdmin only).</summary>
    [HttpPost("api/v1/break-glass")]
    [Authorize(Roles = "super_admin,SuperAdmin")]
    public async Task<IActionResult> ActivateBreakGlass(
        [FromBody] JsonElement body, CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        var content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        return await ForwardResponse(
            await CreateClient().PostAsync($"{baseUrl}/api/v1/break-glass", content, ct), ct);
    }

    /// <summary>DELETE /api/v1/break-glass — deactivate break-glass (SuperAdmin only).</summary>
    [HttpDelete("api/v1/break-glass")]
    [Authorize(Roles = "super_admin,SuperAdmin")]
    public async Task<IActionResult> DeactivateBreakGlass(CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        return await ForwardResponse(
            await CreateClient().DeleteAsync($"{baseUrl}/api/v1/break-glass", ct), ct);
    }

    // ── Recertification ──────────────────────────────────────────────────────

    /// <summary>GET /api/v1/recertification/campaigns — list campaigns.</summary>
    [HttpGet("api/v1/recertification/campaigns")]
    public async Task<IActionResult> GetCampaigns(CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        return await ForwardResponse(
            await CreateClient().GetAsync($"{baseUrl}/api/v1/recertification/campaigns", ct), ct);
    }

    /// <summary>POST /api/v1/recertification/campaigns — start a new campaign.</summary>
    [HttpPost("api/v1/recertification/campaigns")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> StartCampaign(
        [FromBody] JsonElement body, CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        var content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        return await ForwardResponse(
            await CreateClient().PostAsync($"{baseUrl}/api/v1/recertification/campaigns", content, ct), ct);
    }

    /// <summary>GET /api/v1/recertification/campaigns/{id}/items — items for a campaign.</summary>
    [HttpGet("api/v1/recertification/campaigns/{id:long}/items")]
    public async Task<IActionResult> GetCampaignItems(long id, CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        return await ForwardResponse(
            await CreateClient().GetAsync($"{baseUrl}/api/v1/recertification/campaigns/{id}/items", ct), ct);
    }

    /// <summary>POST /api/v1/recertification/items/{id}/decide — approve or revoke a grant.</summary>
    [HttpPost("api/v1/recertification/items/{id:long}/decide")]
    public async Task<IActionResult> DecideItem(
        long id, [FromBody] JsonElement body, CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        var content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        return await ForwardResponse(
            await CreateClient().PostAsync($"{baseUrl}/api/v1/recertification/items/{id}/decide", content, ct), ct);
    }

    // ── Risk Scoring ─────────────────────────────────────────────────────────

    /// <summary>GET /api/v1/risk/me — risk score for the caller.</summary>
    [HttpGet("api/v1/risk/me")]
    public async Task<IActionResult> GetMyRisk(CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        return await ForwardResponse(
            await CreateClient().GetAsync($"{baseUrl}/api/v1/risk/me", ct), ct);
    }

    /// <summary>GET /api/v1/risk/{userId} — risk score for a specific user (admin).</summary>
    [HttpGet("api/v1/risk/{userId:int}")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> GetUserRisk(int userId, CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        return await ForwardResponse(
            await CreateClient().GetAsync($"{baseUrl}/api/v1/risk/{userId}", ct), ct);
    }

    /// <summary>GET /api/v1/risk/summary — system-wide risk breakdown (admin).</summary>
    [HttpGet("api/v1/risk/summary")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> GetRiskSummary(CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        return await ForwardResponse(
            await CreateClient().GetAsync($"{baseUrl}/api/v1/risk/summary", ct), ct);
    }

    // ── Compliance Reports ───────────────────────────────────────────────────

    /// <summary>GET /api/v1/reports/audit — paginated audit log.</summary>
    [HttpGet("api/v1/reports/audit")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> GetAuditLog(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        return await ForwardResponse(
            await CreateClient().GetAsync(
                $"{baseUrl}/api/v1/reports/audit?page={page}&pageSize={pageSize}", ct), ct);
    }

    /// <summary>GET /api/v1/reports/audit/export — CSV export of audit log.</summary>
    [HttpGet("api/v1/reports/audit/export")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> ExportAuditLog(CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        var response = await CreateClient().GetAsync($"{baseUrl}/api/v1/reports/audit/export", ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return File(bytes, "text/csv", "audit-log.csv");
    }

    /// <summary>GET /api/v1/reports/grants/summary — grant distribution summary.</summary>
    [HttpGet("api/v1/reports/grants/summary")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> GetGrantsSummary(CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        return await ForwardResponse(
            await CreateClient().GetAsync($"{baseUrl}/api/v1/reports/grants/summary", ct), ct);
    }
}
