using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

/// <summary>
/// Single Angular entry point for agentic AI requests.
/// Validates JWT, enriches request with user context from claims, then proxies to AiService /api/ai/assist.
/// Angular → POST /api/v1/ai/assist (this controller) → AiService → McpService → Gateway APIs → Gemini
/// </summary>
[ApiController]
[Route("api/v1/ai")]
[Authorize]
public class AiProxyController : ControllerBase
{
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiProxyController> _logger;

    public AiProxyController(
        IHttpClientFactory factory,
        IConfiguration configuration,
        ILogger<AiProxyController> logger)
    {
        _factory = factory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Agentic AI assist — Angular calls this, Gateway enriches with user context, forwards to AiService.
    /// </summary>
    [HttpPost("assist")]
    public async Task<ActionResult> Assist(
        [FromBody] AiAssistRequest request,
        CancellationToken ct)
    {
        var aiUrl = _configuration["ServiceUrls:AiService"] ?? "http://svc-sbd-ai";

        // Extract validated JWT to forward to AiService → McpService → Gateway (re-validated each hop)
        var rawJwt = HttpContext.Request.Headers.Authorization
            .FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
            ?? string.Empty;

        // Enrich with claims from the already-validated token (Gateway auth middleware validated it)
        var schoolIdClaim = User.FindFirst("school_id")?.Value;
        var areaIdClaim = User.FindFirst("area_id")?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? "unknown";

        var enriched = new
        {
            request.Intent,
            request.AdditionalContext,
            UserJwt = rawJwt,
            SchoolId = request.SchoolId
                ?? (int.TryParse(schoolIdClaim, out var sid) ? sid : (int?)null),
            AreaId = request.AreaId
                ?? (int.TryParse(areaIdClaim, out var aid) ? aid : (int?)null),
            Role = role
        };

        _logger.LogInformation(
            "[AiProxy] Assist request — role:{Role} school:{School} area:{Area} intent:{Intent}",
            role, enriched.SchoolId, enriched.AreaId, request.Intent);

        var client = _factory.CreateClient();
        var body = new StringContent(
            JsonSerializer.Serialize(enriched, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }),
            Encoding.UTF8, "application/json");

        // Forward JWT so AiService can also pass it down to McpService
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", rawJwt);

        var response = await client.PostAsync($"{aiUrl}/api/ai/assist", body, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        return response.IsSuccessStatusCode
            ? Content(responseBody, "application/json")
            : StatusCode((int)response.StatusCode, responseBody);
    }
}

public record AiAssistRequest(
    string Intent,
    string? AdditionalContext = null,
    int? SchoolId = null,
    int? AreaId = null
);
