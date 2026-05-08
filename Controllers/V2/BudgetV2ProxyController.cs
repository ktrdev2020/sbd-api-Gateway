using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers.V2;

/// <summary>
/// Thin catch-all proxy forwarding every <c>/api/v2/budget/*</c> call to BudgetApi.
/// JWT + Content-Type passthrough; BudgetApi enforces scope filtering
/// (defense-in-depth). Single catchall route keeps proxy surface maintenance-free
/// as T8 sessions add new endpoints — no Gateway change needed per session.
/// Pattern-copy of <see cref="AplanProxyController"/>.
/// </summary>
[ApiController]
[Route("api/v2/budget")]
[Authorize]
public class BudgetV2ProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<BudgetV2ProxyController> _logger;

    public BudgetV2ProxyController(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<BudgetV2ProxyController> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private string BudgetApiBase =>
        _config["ServiceUrls:BudgetApi"]
        ?? Environment.GetEnvironmentVariable("BUDGET_API_URL")
        ?? "http://localhost:5040";

    [AcceptVerbs("GET", "POST", "PUT", "DELETE", "PATCH")]
    [Route("{**path}")]
    public Task<IActionResult> Passthrough([FromRoute] string path, CancellationToken ct)
    {
        var method = HttpMethod.Parse(Request.Method);
        var target = $"/api/v2/budget/{path}{Request.QueryString}";
        return Forward(method, target, ct);
    }

    private async Task<IActionResult> Forward(HttpMethod method, string path, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        var req = new HttpRequestMessage(method, BudgetApiBase + path);

        if (Request.Headers.TryGetValue("Authorization", out var auth))
            req.Headers.TryAddWithoutValidation("Authorization", auth.ToString());

        if (method != HttpMethod.Get && method != HttpMethod.Delete && Request.ContentLength > 0)
        {
            // Buffer body as raw bytes — never decode as UTF-8.
            //
            // EnableBuffering() + Position=0 guards against the case where
            // some upstream filter (model binding for [ApiController] when
            // it sees multipart/form-data, etc.) drained the stream before
            // the controller body executes. With buffering enabled the
            // stream is rewindable, so the copy is reliable regardless of
            // what touched the body earlier in the pipeline.
            //
            // Content-Type is forwarded *raw* via TryAddWithoutValidation —
            // not via MediaTypeHeaderValue.Parse — because the Parse path
            // adds quotes around parameter values like the multipart
            // `boundary=...` token. ASP.NET Core's multipart reader on the
            // BudgetApi side then can't match the quoted boundary against
            // the literal `--boundary` lines in the body bytes, leaving
            // IFormFile null and the upload action returning 400.
            //
            // (Mirrors response-stream fix from Plan #15 D10 — memory:
            // gateway-proxy-binary-stream.)
            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var bodyBuffer = new MemoryStream();
            await Request.Body.CopyToAsync(bodyBuffer, ct);
            req.Content = new ByteArrayContent(bodyBuffer.ToArray());
            req.Content.Headers.Remove("Content-Type");
            if (!string.IsNullOrEmpty(Request.ContentType))
            {
                req.Content.Headers.TryAddWithoutValidation("Content-Type", Request.ContentType);
            }
            _logger.LogInformation(
                "[BudgetV2Proxy] Forwarded body · contentType={ContentType} bytes={Bytes}",
                Request.ContentType, bodyBuffer.Length);
        }

        try
        {
            // Plan #15 D10 — Stream the response body to the client without
            // touching it as text. The previous implementation called
            // `ReadAsStringAsync` which UTF-8-decodes the bytes; that worked
            // for JSON but corrupted binary downloads (.docx render endpoint
            // produced U+FFFD replacement chars in every byte > 127, breaking
            // the ZIP central directory). Streaming preserves bytes exactly
            // for both text and binary content types.
            var response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            // Forward Content-Disposition so browsers honor the filename hint.
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
            _logger.LogError(ex, "BudgetApi v2 proxy failed for {Method} {Path}", method, path);
            return StatusCode(502, new { error = "BudgetApi ไม่ตอบสนอง" });
        }
    }
}
