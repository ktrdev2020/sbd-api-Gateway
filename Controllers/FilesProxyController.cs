using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

/// <summary>
/// Thin proxy that forwards <c>/api/v1/files/*</c> calls to FileService over
/// HTTP. The frontend only ever talks to the Gateway, so this keeps Angular
/// from needing to know FileService's URL.
///
/// Why a proxy and not a redirect: we want to preserve the JWT, the auth
/// context (User.Identity.Name → uploadedBy), and the multipart stream — all
/// transparently. Streaming the request body avoids buffering large uploads
/// in Gateway memory.
/// </summary>
[ApiController]
[Route("api/v1/files")]
[Authorize]
public class FilesProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<FilesProxyController> _logger;

    public FilesProxyController(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<FilesProxyController> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private string FileServiceBase =>
        _config["Services:FileService:BaseUrl"]
        ?? Environment.GetEnvironmentVariable("FILE_SERVICE_URL")
        ?? "http://localhost:5060";

    /// <summary>POST /api/v1/files — multipart upload, forwarded to FileService.</summary>
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        if (!Request.HasFormContentType)
            return BadRequest(new { message = "Expected multipart/form-data" });

        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(2);

        // Build a streaming multipart forward — read the incoming form and
        // re-emit it without buffering large blobs in memory.
        var form = await Request.ReadFormAsync(ct);
        using var content = new MultipartFormDataContent($"----sbd-{Guid.NewGuid():N}");

        foreach (var field in form)
        {
            foreach (var value in field.Value)
                content.Add(new StringContent(value ?? string.Empty), field.Key);
        }

        foreach (var file in form.Files)
        {
            var streamContent = new StreamContent(file.OpenReadStream());
            streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
            content.Add(streamContent, file.Name, file.FileName);
        }

        var req = new HttpRequestMessage(HttpMethod.Post, $"{FileServiceBase}/api/v1/files")
        {
            Content = content
        };
        ForwardAuth(req);

        var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        return await ForwardResponse(resp, ct);
    }

    [HttpGet("{id:long}")]
    public Task<IActionResult> GetById(long id, CancellationToken ct) =>
        ForwardGet($"/api/v1/files/{id}", ct);

    [HttpGet("by-owner/{ownerType}/{ownerId:long}/{kind}")]
    public Task<IActionResult> GetByOwner(string ownerType, long ownerId, string kind, CancellationToken ct) =>
        ForwardGet($"/api/v1/files/by-owner/{ownerType}/{ownerId}/{kind}", ct);

    [HttpDelete("{id:long}")]
    public Task<IActionResult> Delete(long id, CancellationToken ct) =>
        ForwardDelete($"/api/v1/files/{id}", ct);

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<IActionResult> ForwardGet(string path, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, $"{FileServiceBase}{path}");
        ForwardAuth(req);
        try
        {
            var resp = await http.SendAsync(req, ct);
            return await ForwardResponse(resp, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FilesProxy] Forward GET {Path} failed", path);
            return StatusCode(503, new { message = "FileService unavailable" });
        }
    }

    private async Task<IActionResult> ForwardDelete(string path, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Delete, $"{FileServiceBase}{path}");
        ForwardAuth(req);
        try
        {
            var resp = await http.SendAsync(req, ct);
            return await ForwardResponse(resp, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FilesProxy] Forward DELETE {Path} failed", path);
            return StatusCode(503, new { message = "FileService unavailable" });
        }
    }

    private void ForwardAuth(HttpRequestMessage req)
    {
        // Pass the bearer token through so FileService can identify the user.
        if (Request.Headers.TryGetValue("Authorization", out var auth) && auth.Count > 0)
        {
            var raw = auth[0]!;
            if (AuthenticationHeaderValue.TryParse(raw, out var parsed))
                req.Headers.Authorization = parsed;
        }
    }

    private async Task<IActionResult> ForwardResponse(HttpResponseMessage resp, CancellationToken ct)
    {
        // Plan #15 Phase A8 — Stream the response body without UTF-8-decoding
        // it. The previous implementation called `ReadAsStringAsync` which
        // corrupts every byte > 0x7F in binary content (PDFs, DOCX, images)
        // by replacing it with U+FFFD. FilesProxy is the highest-risk caller
        // because it serves user-uploaded artefacts of arbitrary content type.
        var contentType = resp.Content.Headers.ContentType?.ToString()
            ?? "application/octet-stream";
        if (resp.Content.Headers.ContentDisposition is { } cd)
        {
            Response.Headers["Content-Disposition"] = cd.ToString();
        }
        Response.StatusCode = (int)resp.StatusCode;
        Response.ContentType = contentType;
        await using var upstream = await resp.Content.ReadAsStreamAsync(ct);
        await upstream.CopyToAsync(Response.Body, ct);
        return new EmptyResult();
    }
}
