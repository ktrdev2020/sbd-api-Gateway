using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Gateway.Controllers;

/// <summary>
/// Proxies McpService inspection + template management endpoints for the SuperAdmin MCP Console.
/// Angular → /api/v1/mcp/* → McpService /*
/// </summary>
[ApiController]
[Route("api/v1/mcp")]
[Authorize(Roles = "SuperAdmin")]
public class McpProxyController : ControllerBase
{
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration _configuration;

    public McpProxyController(IHttpClientFactory factory, IConfiguration configuration)
    {
        _factory = factory;
        _configuration = configuration;
    }

    private string McpUrl => _configuration["ServiceUrls:McpService"] ?? "http://svc-sbd-mcp";

    // ── Inspection ────────────────────────────────────────────────────────────

    [HttpGet("tools")]
    public Task<ActionResult> GetTools(CancellationToken ct) =>
        ProxyGet("/tools", ct);

    [HttpGet("health")]
    public async Task<ActionResult> GetHealth(CancellationToken ct)
    {
        try { return await ProxyGet("/health", ct); }
        catch { return StatusCode(503, new { status = "Unreachable" }); }
    }

    // ── Prompt Templates ──────────────────────────────────────────────────────

    [HttpGet("templates")]
    public Task<ActionResult> GetTemplates(CancellationToken ct) =>
        ProxyGet("/templates", ct);

    [HttpGet("templates/{name}")]
    public Task<ActionResult> GetTemplate(string name, CancellationToken ct) =>
        ProxyGet($"/templates/{name}", ct);

    [HttpPut("templates/{name}")]
    public async Task<ActionResult> UpsertTemplate(string name, CancellationToken ct)
    {
        var body = await new StreamReader(Request.Body).ReadToEndAsync(ct);
        var client = _factory.CreateClient();
        // Forward the Gateway-validated JWT so McpService knows who updated the template
        var jwt = Request.Headers.Authorization.FirstOrDefault() ?? "";
        if (!string.IsNullOrEmpty(jwt))
            client.DefaultRequestHeaders.Add("Authorization", jwt);

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.PutAsync($"{McpUrl}/templates/{name}", content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode
            ? Content(responseBody, "application/json")
            : StatusCode((int)response.StatusCode, responseBody);
    }

    [HttpDelete("templates/{name}")]
    public async Task<ActionResult> DeleteTemplate(string name, CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var response = await client.DeleteAsync($"{McpUrl}/templates/{name}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode
            ? Content(body, "application/json")
            : StatusCode((int)response.StatusCode, body);
    }

    // ── Analytics ─────────────────────────────────────────────────────────────

    [HttpGet("analytics")]
    public Task<ActionResult> GetAnalytics(CancellationToken ct) =>
        ProxyGet("/analytics", ct);

    [HttpDelete("analytics")]
    public async Task<ActionResult> ResetAnalytics(CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var response = await client.DeleteAsync($"{McpUrl}/analytics", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode
            ? Content(body, "application/json")
            : StatusCode((int)response.StatusCode, body);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<ActionResult> ProxyGet(string path, CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"{McpUrl}{path}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode
            ? Content(body, "application/json")
            : StatusCode((int)response.StatusCode, body);
    }
}
