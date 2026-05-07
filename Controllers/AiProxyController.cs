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
        var schoolCodeClaim = User.FindFirst("school_code")?.Value
            ?? User.FindFirst("school_id")?.Value;
        var areaIdClaim = User.FindFirst("area_id")?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? "unknown";

        var enriched = new
        {
            request.Intent,
            request.AdditionalContext,
            UserJwt = rawJwt,
            SchoolCode = request.SchoolCode ?? schoolCodeClaim,
            AreaId = request.AreaId
                ?? (int.TryParse(areaIdClaim, out var aid) ? aid : (int?)null),
            Role = role,
            request.TeacherId,
            request.StudentId,
            request.History,
            request.NavItems
        };

        _logger.LogInformation(
            "[AiProxy] Assist request — role:{Role} school:{School} area:{Area} intent:{Intent}",
            role, enriched.SchoolCode, enriched.AreaId, request.Intent);

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
    /// Plan #29 Track B-2 — AI project review proxy. Frontend calls
    /// POST /api/v1/ai/projects/{id}/review on Gateway; we forward to
    /// AiService which orchestrates MCP tool calls + Gemini + Redis cache.
    /// JWT carried through so the entire chain (Gateway → AiService →
    /// McpService → BudgetApi/Gateway) re-validates per hop.
    /// </summary>
    [HttpPost("projects/{projectId:int}/review")]
    public async Task<ActionResult> ProjectReview(int projectId, CancellationToken ct)
    {
        var aiUrl = _configuration["ServiceUrls:AiService"] ?? "http://svc-sbd-ai";
        var rawJwt = HttpContext.Request.Headers.Authorization
            .FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
            ?? string.Empty;

        var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60); // Gemini cold call can take ~10-15s
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", rawJwt);

        var response = await client.PostAsync(
            $"{aiUrl}/api/ai/projects/{projectId}/review",
            new StringContent(string.Empty, Encoding.UTF8, "application/json"),
            ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        return response.IsSuccessStatusCode
            ? Content(responseBody, "application/json")
            : StatusCode((int)response.StatusCode, responseBody);
    }

    /// <summary>
    /// Plan #34 AI-2 — Aplan AI drafter proxy. Frontend calls
    /// POST /api/v1/ai/aplan/projects/{id}/draft on Gateway with
    /// { role, userHint } body; we forward to AiService which calls
    /// MCP draft-context + Gemini + schema validator.
    /// </summary>
    [HttpPost("aplan/projects/{projectId:int}/draft")]
    public async Task<ActionResult> AplanDraft(
        int projectId, [FromBody] AplanDraftProxyRequest request, CancellationToken ct)
    {
        var aiUrl = _configuration["ServiceUrls:AiService"] ?? "http://svc-sbd-ai";
        var rawJwt = HttpContext.Request.Headers.Authorization
            .FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
            ?? string.Empty;

        var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", rawJwt);

        var bodyJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        var response = await client.PostAsync(
            $"{aiUrl}/api/ai/aplan/projects/{projectId}/draft",
            new StringContent(bodyJson, Encoding.UTF8, "application/json"),
            ct);
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

        var schoolCodeClaim = User.FindFirst("school_code")?.Value
            ?? User.FindFirst("school_id")?.Value;
        var areaIdClaim = User.FindFirst("area_id")?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? "unknown";

        var enriched = new
        {
            request.Intent,
            request.AdditionalContext,
            UserJwt = rawJwt,
            SchoolCode = request.SchoolCode ?? schoolCodeClaim,
            AreaId = request.AreaId
                ?? (int.TryParse(areaIdClaim, out var aid) ? aid : (int?)null),
            Role = role,
            request.TeacherId,
            request.StudentId,
            request.History,
            request.NavItems
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

    /// <summary>
    /// Public SSE streaming — unauthenticated, restricted to public data only.
    /// Used by the Public role chatbot (e.g. school directory page with no login).
    /// AiService will filter tools via ToolFilterService with role = "Public".
    /// </summary>
    [HttpPost("assist/public/stream")]
    [AllowAnonymous]
    public async Task AssistPublicStream(
        [FromBody] AiPublicAssistRequest request,
        CancellationToken ct)
    {
        var aiUrl = _configuration["ServiceUrls:AiService"] ?? "http://svc-sbd-ai";

        var enriched = new
        {
            request.Intent,
            request.AreaId,
            Role = "Public",
            request.History
        };

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Append("X-Accel-Buffering", "no");

        using var httpClient = _factory.CreateClient();
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
            _logger.LogError(ex, "[AiProxy] Public stream error");
            var errJson = JsonSerializer.Serialize(new { type = "error", data = ex.Message });
            await Response.WriteAsync($"data: {errJson}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}

public record AiAssistRequest(
    string Intent,
    string? AdditionalContext = null,
    string? SchoolCode = null,
    int? AreaId = null,
    int? TeacherId = null,
    int? StudentId = null,
    IReadOnlyList<ConversationTurn>? History = null,
    IReadOnlyList<NavItem>? NavItems = null
);

public record NavItem(string Label, string Path);

public record ConversationTurn(string Role, string Content);

public record AiPublicAssistRequest(
    string Intent,
    int? AreaId = null,
    IReadOnlyList<ConversationTurn>? History = null
);

public record AplanDraftProxyRequest(string? Role, string? UserHint);
