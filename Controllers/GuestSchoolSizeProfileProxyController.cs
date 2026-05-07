using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

/// <summary>
/// Plan #31 — public anonymous proxy for the per-school size + level batch
/// used by /school-info. Forwards verbatim to StudentApi without auth header.
/// StudentApi's `school_code` is DMC SmisCode (8-digit) — the frontend must
/// join on SmisCode (which is already present on every row of /school-info
/// registration list).
/// </summary>
[ApiController]
[Route("api/v1/guest/schools")]
[AllowAnonymous]
public class GuestSchoolSizeProfileProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<GuestSchoolSizeProfileProxyController> _logger;

    public GuestSchoolSizeProfileProxyController(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<GuestSchoolSizeProfileProxyController> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private string StudentApiBase =>
        _config["ServiceUrls:StudentApi"]
        ?? Environment.GetEnvironmentVariable("STUDENT_API_URL")
        ?? "http://localhost:5032";

    [HttpGet("size-profile-batch")]
    public async Task<IActionResult> GetBatch(CancellationToken ct)
    {
        var url = $"{StudentApiBase}/api/v1/guest/schools/size-profile-batch{Request.QueryString}";
        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        // Public endpoint — do NOT forward Authorization header.

        try
        {
            var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            Response.StatusCode = (int)resp.StatusCode;
            Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
            await using var upstream = await resp.Content.ReadAsStreamAsync(ct);
            await upstream.CopyToAsync(Response.Body, ct);
            return new EmptyResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "StudentApi guest size-profile-batch proxy failed");
            return StatusCode(502, new { error = "StudentApi ไม่ตอบสนอง" });
        }
    }
}
