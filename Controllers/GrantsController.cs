using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SBD.ServiceRegistry;

namespace Gateway.Controllers;

/// <summary>
/// Admin-facing proxy for capability grant management.
/// Forwards requests to AuthorityService (discovered via ServiceRegistry)
/// so Angular never calls AuthorityService directly.
///
/// Write operations (POST grant, DELETE revoke) are restricted to AreaAdmin+.
/// GET operations (catalog, user grants) are available to any authenticated user.
/// </summary>
[ApiController]
[Route("api/v1/grants")]
[Authorize]
public class GrantsController(
    IServiceRegistry registry,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<GrantsController> logger) : ControllerBase
{
    // ── Resolve AuthorityService base URL ───────────────────────────────────
    private async Task<string> GetAuthorityBaseUrl()
    {
        var instances = await registry.GetInstancesAsync("AuthorityService");
        if (instances.Count > 0) return instances[0].BaseUrl;

        // Static fallback
        var url = configuration["ServiceUrls:AuthorityService"]
               ?? configuration["ServiceRegistry:ServiceUrl"]
               ?? "http://localhost:5004";
        logger.LogWarning("AuthorityService not found in registry, using fallback: {Url}", url);
        return url;
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient();
        // Forward the caller's Bearer token to AuthorityService — it requires [Authorize]
        var bearer = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(bearer))
            client.DefaultRequestHeaders.Authorization =
                AuthenticationHeaderValue.Parse(bearer);
        return client;
    }

    // ── GET /api/v1/grants?userId={id} ──────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetForUser(
        [FromQuery] int userId,
        CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        var client = CreateClient();
        var response = await client.GetAsync($"{baseUrl}/api/v1/grants?userId={userId}", ct);
        return await ForwardResponse(response, ct);
    }

    // ── POST /api/v1/grants ─────────────────────────────────────────────────
    [HttpPost]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> Grant(
        [FromBody] JsonElement body,
        CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        var client = CreateClient();
        var content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{baseUrl}/api/v1/grants", content, ct);
        return await ForwardResponse(response, ct);
    }

    // ── DELETE /api/v1/grants/{id} ──────────────────────────────────────────
    [HttpDelete("{id:long}")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> Revoke(
        long id,
        [FromBody] JsonElement body,
        CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Delete, $"{baseUrl}/api/v1/grants/{id}")
        {
            Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json")
        };
        var response = await client.SendAsync(request, ct);
        return await ForwardResponse(response, ct);
    }

    // ── GET /api/v1/grants/capabilities ────────────────────────────────────
    /// <summary>Returns the full capability catalog from AuthorityService.</summary>
    [HttpGet("capabilities")]
    public async Task<IActionResult> GetCapabilities(
        [FromQuery] string? module = null,
        CancellationToken ct = default)
    {
        var baseUrl = await GetAuthorityBaseUrl();
        var client = CreateClient();
        var url = string.IsNullOrEmpty(module)
            ? $"{baseUrl}/api/v1/capabilities"
            : $"{baseUrl}/api/v1/capabilities?module={Uri.EscapeDataString(module)}";
        var response = await client.GetAsync(url, ct);
        return await ForwardResponse(response, ct);
    }

    // ── Private helper ──────────────────────────────────────────────────────
    private async Task<IActionResult> ForwardResponse(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        return StatusCode((int)response.StatusCode, body.Length > 0
            ? JsonSerializer.Deserialize<JsonElement>(body)
            : null);
    }
}
