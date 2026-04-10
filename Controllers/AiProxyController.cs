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

    /// <summary>
    /// SSE streaming — proxies text/event-stream from AiService to Angular.
    /// Angular reads events: thinking → tool_call → tool_result → final
    /// </summary>
    [HttpPost("assist/stream")]
    public async Task AssistStream(
        [FromBody] AiAssistRequest request,
        CancellationToken ct)
    {
        var aiUrl = _configuration["ServiceUrls:AiService"] ?? "http://svc-sbd-ai";

        var rawJwt = HttpContext.Request.Headers.Authorization
            .FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
            ?? string.Empty;

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

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Append("X-Accel-Buffering", "no");

        using var httpClient = _factory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", rawJwt);
        httpClient.Timeout = TimeSpan.FromMinutes(5);

        var bodyContent = new StringContent(
            JsonSerializer.Serialize(enriched, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }),
            Encoding.UTF8, "application/json");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{aiUrl}/api/ai/assist/stream")
            {
                Content = bodyContent
            };
            using var upstreamResponse = await httpClient.SendAsync(
                req, HttpCompletionOption.ResponseHeadersRead, ct);

            using var stream = await upstreamResponse.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                await Response.WriteAsync(line + "\n", ct);
                if (line.StartsWith("data:"))
                    await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiProxy] Stream error");
            var errJson = JsonSerializer.Serialize(new { type = "error", data = ex.Message });
            await Response.WriteAsync($"data: {errJson}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}

public record AiAssistRequest(
    string Intent,
    string? AdditionalContext = null,
    int? SchoolId = null,
    int? AreaId = null
);
