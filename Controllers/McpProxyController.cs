using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

/// <summary>
/// Proxies McpService inspection endpoints for the SuperAdmin MCP Console.
/// Angular → GET /api/v1/mcp/tools → McpService /tools
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

    [HttpGet("tools")]
    public async Task<ActionResult> GetTools(CancellationToken ct)
    {
        var mcpUrl = _configuration["ServiceUrls:McpService"] ?? "http://svc-sbd-mcp";
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"{mcpUrl}/tools", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode
            ? Content(body, "application/json")
            : StatusCode((int)response.StatusCode, body);
    }

    [HttpGet("health")]
    public async Task<ActionResult> GetHealth(CancellationToken ct)
    {
        var mcpUrl = _configuration["ServiceUrls:McpService"] ?? "http://svc-sbd-mcp";
        var client = _factory.CreateClient();
        try
        {
            var response = await client.GetAsync($"{mcpUrl}/health", ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return response.IsSuccessStatusCode
                ? Content(body, "application/json")
                : StatusCode((int)response.StatusCode, body);
        }
        catch
        {
            return StatusCode(503, new { status = "Unreachable" });
        }
    }
}
