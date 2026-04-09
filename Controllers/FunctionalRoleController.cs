using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SBD.ServiceRegistry;

namespace Gateway.Controllers;

/// <summary>
/// Proxy for functional role catalog and assignment management.
/// Forwards to AuthorityService (port 5004) via ServiceRegistry.
/// Angular never calls AuthorityService directly.
/// </summary>
[ApiController]
[Authorize]
public class FunctionalRoleController(
    IServiceRegistry registry,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<FunctionalRoleController> logger) : ControllerBase
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

    // ── GET /api/v1/functional-roles ─────────────────────────────────────────
    [HttpGet("api/v1/functional-roles")]
    public async Task<IActionResult> GetCatalog(
        [FromQuery] string? category = null,
        [FromQuery] string? contextScope = null,
        CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(category)) qs.Add($"category={Uri.EscapeDataString(category)}");
        if (!string.IsNullOrEmpty(contextScope)) qs.Add($"contextScope={Uri.EscapeDataString(contextScope)}");
        var url = $"{baseUrl}/api/v1/functional-roles" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        return await ForwardResponse(await CreateClient().GetAsync(url, ct), ct);
    }

    // ── GET /api/v1/functional-assignments?userId={id} ───────────────────────
    [HttpGet("api/v1/functional-assignments")]
    public async Task<IActionResult> GetAssignments(
        [FromQuery] int userId,
        CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        return await ForwardResponse(
            await CreateClient().GetAsync($"{baseUrl}/api/v1/functional-assignments?userId={userId}", ct), ct);
    }

    // ── GET /api/v1/functional-assignments/school/{schoolId} ────────────────
    [HttpGet("api/v1/functional-assignments/school/{schoolId:int}")]
    [Authorize(Roles = "super_admin,area_admin,school_admin,SuperAdmin,AreaAdmin,SchoolAdmin")]
    public async Task<IActionResult> GetAssignmentsForSchool(
        int schoolId,
        CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        return await ForwardResponse(
            await CreateClient().GetAsync($"{baseUrl}/api/v1/functional-assignments/school/{schoolId}", ct), ct);
    }

    // ── GET /api/v1/functional-assignments/area/{areaId} ─────────────────────
    [HttpGet("api/v1/functional-assignments/area/{areaId:int}")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> GetAssignmentsForArea(
        int areaId,
        CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        return await ForwardResponse(
            await CreateClient().GetAsync($"{baseUrl}/api/v1/functional-assignments/area/{areaId}", ct), ct);
    }

    // ── POST /api/v1/functional-assignments ──────────────────────────────────
    [HttpPost("api/v1/functional-assignments")]
    [Authorize(Roles = "super_admin,area_admin,school_admin,SuperAdmin,AreaAdmin,SchoolAdmin")]
    public async Task<IActionResult> Assign(
        [FromBody] JsonElement body,
        CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        var content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        return await ForwardResponse(
            await CreateClient().PostAsync($"{baseUrl}/api/v1/functional-assignments", content, ct), ct);
    }

    // ── DELETE /api/v1/functional-assignments/{id} ───────────────────────────
    [HttpDelete("api/v1/functional-assignments/{id:long}")]
    [Authorize(Roles = "super_admin,area_admin,school_admin,SuperAdmin,AreaAdmin,SchoolAdmin")]
    public async Task<IActionResult> Revoke(
        long id,
        [FromBody] JsonElement body,
        CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        var req = new HttpRequestMessage(HttpMethod.Delete, $"{baseUrl}/api/v1/functional-assignments/{id}")
        {
            Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json")
        };
        return await ForwardResponse(await CreateClient().SendAsync(req, ct), ct);
    }

    private async Task<IActionResult> ForwardResponse(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        return StatusCode((int)response.StatusCode, body.Length > 0
            ? JsonSerializer.Deserialize<JsonElement>(body)
            : null);
    }
}
