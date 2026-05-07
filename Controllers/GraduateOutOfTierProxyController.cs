using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #31 — clean up ghost academic_record rows at grades the school
/// doesn't actually teach. Reads the school's declared tier checklist
/// (TeachesPreschool/Primary/LowerSecondary/UpperSecondary shadow props),
/// maps to LevelOrder lists, then forwards to StudentApi for execution.
/// </summary>
[ApiController]
[Route("api/v1/school/{schoolCode}/students/graduate-out-of-tier")]
[Authorize]
public class GraduateOutOfTierProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly SbdDbContext _db;
    private readonly ILogger<GraduateOutOfTierProxyController> _logger;

    public GraduateOutOfTierProxyController(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        SbdDbContext db,
        ILogger<GraduateOutOfTierProxyController> logger)
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

    [HttpPost]
    public async Task<IActionResult> Graduate(
        [FromRoute] string schoolCode,
        [FromBody] ClientRequest req,
        CancellationToken ct = default)
    {
        // Resolve declared tiers from Schools shadow props
        // AsTracking required so shadow properties (Teaches*) hydrate.
        // AsNoTracking skips them and Entry().Property() returns null.
        var school = await _db.Schools.AsTracking()
            .FirstOrDefaultAsync(s => s.SchoolCode == schoolCode, ct);
        if (school is null) return NotFound(new { error = "School not found" });

        var entry = _db.Entry(school);
        var teachesPreschool = entry.Property<bool?>("TeachesPreschool").CurrentValue ?? false;
        var teachesPrimary = entry.Property<bool?>("TeachesPrimary").CurrentValue ?? false;
        var teachesLowerSec = entry.Property<bool?>("TeachesLowerSecondary").CurrentValue ?? false;
        var teachesUpperSec = entry.Property<bool?>("TeachesUpperSecondary").CurrentValue ?? false;

        var allowed = new List<int>();
        if (teachesPreschool) allowed.AddRange(new[] { 1, 2, 3 });
        if (teachesPrimary) allowed.AddRange(new[] { 4, 5, 6, 7, 8, 9 });
        if (teachesLowerSec) allowed.AddRange(new[] { 10, 11, 12 });
        if (teachesUpperSec) allowed.AddRange(new[] { 13, 14, 15 });

        if (allowed.Count == 0)
            return BadRequest(new { error = "โรงเรียนยังไม่ได้ระบุระดับที่เปิดสอน — โปรดกรอกใน /school/profile ก่อน" });

        // Translate OBEC SchoolCode → DMC SmisCode if available
        var smis = school.SmisCode;
        var resolvedCode = string.IsNullOrWhiteSpace(smis) ? schoolCode : smis;

        var forwardBody = new
        {
            academicYear = req.AcademicYear,
            allowedLevelOrders = allowed,
            dryRun = req.DryRun,
        };
        var json = JsonSerializer.Serialize(forwardBody);

        var url = $"{StudentApiBase}/api/v1/school/{resolvedCode}/students/graduate-out-of-tier";
        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(60);

        var http_req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (Request.Headers.TryGetValue("Authorization", out var auth))
            http_req.Headers.TryAddWithoutValidation("Authorization", auth.ToString());

        try
        {
            var resp = await http.SendAsync(http_req, HttpCompletionOption.ResponseHeadersRead, ct);
            Response.StatusCode = (int)resp.StatusCode;
            Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
            await using var upstream = await resp.Content.ReadAsStreamAsync(ct);
            await upstream.CopyToAsync(Response.Body, ct);
            return new EmptyResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "StudentApi graduate-out-of-tier proxy failed for {SchoolCode}", schoolCode);
            return StatusCode(502, new { error = "StudentApi ไม่ตอบสนอง" });
        }
    }

    public class ClientRequest
    {
        public short AcademicYear { get; set; }
        public bool DryRun { get; set; }
    }
}
