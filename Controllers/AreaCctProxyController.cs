using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

/// <summary>
/// Plan #4 T8 — Gateway proxy for `/api/v1/area/{areaId}/cct/*` to StudentApi.
/// Handles GET (list, summary), PATCH (per-row edit), DELETE (per-row remove),
/// and POST multipart (CSV upload). Preserves JWT + body + content-type.
/// </summary>
[ApiController]
[Route("api/v1/area/{areaId:long}/cct")]
[Authorize]
public class AreaCctProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AreaCctProxyController> _logger;

    public AreaCctProxyController(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<AreaCctProxyController> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private string StudentApiBase =>
        _config["ServiceUrls:StudentApi"]
        ?? Environment.GetEnvironmentVariable("STUDENT_API_URL")
        ?? "http://localhost:5032";

    [HttpGet]
    public Task<IActionResult> List([FromRoute] long areaId, CancellationToken ct)
        => ForwardAsync(HttpMethod.Get, $"/api/v1/area/{areaId}/cct{Request.QueryString}", ct);

    [HttpGet("summary")]
    public Task<IActionResult> Summary([FromRoute] long areaId, CancellationToken ct)
        => ForwardAsync(HttpMethod.Get, $"/api/v1/area/{areaId}/cct/summary{Request.QueryString}", ct);

    [HttpPatch("{id:long}")]
    public Task<IActionResult> Patch([FromRoute] long areaId, [FromRoute] long id, CancellationToken ct)
        => ForwardAsync(HttpMethod.Patch, $"/api/v1/area/{areaId}/cct/{id}", ct);

    [HttpDelete("{id:long}")]
    public Task<IActionResult> Delete([FromRoute] long areaId, [FromRoute] long id, CancellationToken ct)
        => ForwardAsync(HttpMethod.Delete, $"/api/v1/area/{areaId}/cct/{id}", ct);

    [HttpPost("upload")]
    [RequestSizeLimit(20_000_000)]
    public Task<IActionResult> Upload([FromRoute] long areaId, CancellationToken ct)
        => ForwardAsync(HttpMethod.Post, $"/api/v1/area/{areaId}/cct/upload", ct);

    /// <summary>
    /// Forward request to StudentApi · preserves JWT, body bytes, content-type.
    /// Multipart uploads pass through as-is via raw stream copy.
    /// </summary>
    private async Task<IActionResult> ForwardAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(2);

        var req = new HttpRequestMessage(method, StudentApiBase + path);

        if (Request.Headers.TryGetValue("Authorization", out var auth))
            req.Headers.TryAddWithoutValidation("Authorization", auth.ToString());

        // Forward request body for non-GET / non-DELETE.
        if (method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            // Read body into a buffered stream so HttpClient can re-read if needed.
            var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms, ct);
            ms.Position = 0;
            req.Content = new StreamContent(ms);
            if (Request.ContentType is not null)
                req.Content.Headers.TryAddWithoutValidation("Content-Type", Request.ContentType);
        }

        try
        {
            // Plan #15 Phase A8 — stream the response body to preserve binary
            // content (CSV downloads, future PDF/image content) without
            // UTF-8-decode corruption.
            var response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            if (response.Content.Headers.ContentDisposition is { } cd)
            {
                Response.Headers["Content-Disposition"] = cd.ToString();
            }
            Response.StatusCode = (int)response.StatusCode;
            Response.ContentType = contentType;
            await using var upstream = await response.Content.ReadAsStreamAsync(ct);
            await upstream.CopyToAsync(Response.Body, ct);
            return new EmptyResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "StudentApi CCT proxy failed for {Path}", path);
            return new ContentResult { StatusCode = 502, Content = "{\"error\":\"StudentApi ไม่ตอบสนอง\"}", ContentType = "application/json" };
        }
    }
}
