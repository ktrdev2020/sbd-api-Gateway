using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #45 Phase B — proxy <c>GET /api/v1/schools/{schoolCode}/enrollment-by-grade?academicYear=Y</c>
/// to StudentApi. Translates OBEC SchoolCode → DMC SmisCode (same bridge as
/// SchoolSizeProfileProxyController) before forwarding. `academicYear` is
/// optional upstream (latest AY fallback in StudentApi).
///
/// Consumed by BudgetApi to fetch per-grade student counts for the BOOKS
/// (ค่าหนังสือเรียน) calculation. Frontend uses this directly when rendering
/// the school budget page's BOOKS section.
/// </summary>
[ApiController]
[Route("api/v1/schools/{schoolCode}/enrollment-by-grade")]
[Authorize]
public class SchoolEnrollmentByGradeProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly SbdDbContext _db;
    private readonly ILogger<SchoolEnrollmentByGradeProxyController> _logger;

    public SchoolEnrollmentByGradeProxyController(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        SbdDbContext db,
        ILogger<SchoolEnrollmentByGradeProxyController> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _db = db;
        _logger = logger;
    }

    private string StudentApiBase =>
        _config["ServiceUrls:StudentApi"]
        ?? Environment.GetEnvironmentVariable("STUDENT_API_URL")
        ?? "http://localhost:5032";

    [HttpGet]
    public async Task<IActionResult> Get([FromRoute] string schoolCode, CancellationToken ct)
    {
        var smis = await _db.Schools.AsNoTracking()
            .Where(s => s.SchoolCode == schoolCode)
            .Select(s => s.SmisCode)
            .FirstOrDefaultAsync(ct);
        var resolved = string.IsNullOrWhiteSpace(smis) ? schoolCode : smis;

        var url = $"{StudentApiBase}/api/v1/schools/{resolved}/enrollment-by-grade{Request.QueryString}";
        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(15);

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (Request.Headers.TryGetValue("Authorization", out var auth))
            req.Headers.TryAddWithoutValidation("Authorization", auth.ToString());

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
            _logger.LogError(ex, "StudentApi enrollment-by-grade proxy failed for {SchoolCode}", schoolCode);
            return StatusCode(502, new { error = "StudentApi ไม่ตอบสนอง" });
        }
    }
}
