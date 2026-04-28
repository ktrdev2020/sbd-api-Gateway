using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Public guest endpoints for the /student-info portal page.
/// Anonymous + response-cached for 1 hour. Scoped to สพป.ศก.3 (AreaId 33030000).
///
/// Most actions are thin proxies forwarding to StudentApi (no Authorization
/// header — StudentApi accepts these endpoints anonymously). The
/// <c>summary</c> action additionally enriches districts via the local
/// <see cref="SbdDbContext"/> Schools→Address→District chain so the cross-
/// bounded-context join lives only in the Gateway proxy (per plan #6 D1 +
/// AreaStudentsProxyController pattern).
/// </summary>
[ApiController]
[Route("api/v1/guest/student-info")]
[AllowAnonymous]
public class GuestStudentInfoController : ControllerBase
{
    private const long AreaId = 33030000L;
    private const int CacheSeconds = 3600;

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly SbdDbContext _db;
    private readonly ILogger<GuestStudentInfoController> _logger;

    public GuestStudentInfoController(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        SbdDbContext db,
        ILogger<GuestStudentInfoController> logger)
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

    [HttpGet("summary")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var (status, body, contentType) = await ForwardRawAsync("/api/v1/guest/student-info/summary", ct);
        if (status >= 200 && status < 300 && body.TrimStart().StartsWith('{'))
            body = await EnrichSummaryWithDistrictsAsync(body, ct);
        return new ContentResult { StatusCode = status, Content = body, ContentType = contentType };
    }

    [HttpGet("by-class")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public Task<IActionResult> GetByClass(CancellationToken ct) =>
        ForwardGetAsync("/api/v1/guest/student-info/by-class", ct);

    [HttpGet("by-level")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public Task<IActionResult> GetByLevel(CancellationToken ct) =>
        ForwardGetAsync("/api/v1/guest/student-info/by-level", ct);

    [HttpGet("by-age")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public Task<IActionResult> GetByAge(CancellationToken ct) =>
        ForwardGetAsync("/api/v1/guest/student-info/by-age", ct);

    [HttpGet("by-disability")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public Task<IActionResult> GetByDisability(CancellationToken ct) =>
        ForwardGetAsync("/api/v1/guest/student-info/by-disability", ct);

    [HttpGet("by-disadvantage")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public Task<IActionResult> GetByDisadvantage(CancellationToken ct) =>
        ForwardGetAsync("/api/v1/guest/student-info/by-disadvantage", ct);

    [HttpGet("by-nationality")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public Task<IActionResult> GetByNationality(CancellationToken ct) =>
        ForwardGetAsync("/api/v1/guest/student-info/by-nationality", ct);

    [HttpGet("by-religion")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public Task<IActionResult> GetByReligion(CancellationToken ct) =>
        ForwardGetAsync("/api/v1/guest/student-info/by-religion", ct);

    [HttpGet("by-shortage")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public Task<IActionResult> GetByShortage(CancellationToken ct) =>
        ForwardGetAsync("/api/v1/guest/student-info/by-shortage", ct);

    [HttpGet("by-commute")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public Task<IActionResult> GetByCommute(CancellationToken ct) =>
        ForwardGetAsync("/api/v1/guest/student-info/by-commute", ct);

    [HttpGet("nutrition")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public Task<IActionResult> GetNutrition(CancellationToken ct) =>
        ForwardGetAsync("/api/v1/guest/student-info/nutrition", ct);

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Pulls per-school student counts from StudentApi, then joins against the
    /// Gateway Schools master via SmisCode → SubDistrict → District to roll up
    /// district totals. Mutates the parsed JSON's <c>districts</c> array.
    /// On any failure returns the raw body so the dashboard never goes blank.
    /// </summary>
    private async Task<string> EnrichSummaryWithDistrictsAsync(string body, CancellationToken ct)
    {
        try
        {
            var node = JsonNode.Parse(body);
            if (node is null) return body;

            // Districts come from joining StudentApi's by-school output to the Gateway
            // Schools master. SmisCode is the bridge per the DMC-school-code memo.
            var bySchoolJson = await ForwardRawAsync("/api/v1/guest/student-info/by-school", ct);
            if (bySchoolJson.status < 200 || bySchoolJson.status >= 300) return body;

            var bySchool = JsonNode.Parse(bySchoolJson.body)?.AsArray();
            if (bySchool is null) return body;

            var countBySmis = bySchool
                .Where(x => x is not null)
                .ToDictionary(
                    x => x!["schoolCode"]?.GetValue<string>() ?? string.Empty,
                    x => x!["count"]?.GetValue<int>() ?? 0);

            // Join to Gateway Schools master scoped to area.
            var schoolsInArea = await _db.Schools.AsNoTracking()
                .Where(s => s.AreaId == AreaId
                            && s.SmisCode != null
                            && s.IsActive
                            && s.DeletedAt == null
                            && s.Address != null
                            && s.Address.SubDistrict != null
                            && s.Address.SubDistrict.District != null)
                .Select(s => new
                {
                    s.SmisCode,
                    DistrictName = s.Address!.SubDistrict!.District.NameTh,
                })
                .ToListAsync(ct);

            var districts = schoolsInArea
                .GroupBy(s => s.DistrictName)
                .Select(g => new
                {
                    Name = g.Key,
                    Count = g.Sum(s => countBySmis.TryGetValue(s.SmisCode!, out var c) ? c : 0),
                })
                .Where(d => d.Count > 0)
                .OrderByDescending(d => d.Count)
                .ToList();

            var arr = new JsonArray();
            foreach (var d in districts)
            {
                arr.Add(new JsonObject
                {
                    ["id"] = SlugFromDistrict(d.Name),
                    ["name"] = d.Name,
                    ["count"] = d.Count,
                });
            }
            node["districts"] = arr;

            return node.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich /student-info/summary with districts — returning raw");
            return body;
        }
    }

    private static string SlugFromDistrict(string name) => name switch
    {
        "ขุขันธ์"    => "khukhan",
        "ปรางค์กู่" => "prangku",
        "ภูสิงห์"    => "phusing",
        "ไพรบึง"    => "phraibung",
        _            => name.ToLowerInvariant(),
    };

    private async Task<IActionResult> ForwardGetAsync(string path, CancellationToken ct)
    {
        var (status, body, contentType) = await ForwardRawAsync(path, ct);
        return new ContentResult { StatusCode = status, Content = body, ContentType = contentType };
    }

    private async Task<(int status, string body, string contentType)> ForwardRawAsync(string path, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        var req = new HttpRequestMessage(HttpMethod.Get, StudentApiBase + path);
        // Public endpoint — do NOT forward Authorization header.

        try
        {
            using var response = await http.SendAsync(req, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            return ((int)response.StatusCode, body, contentType);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "StudentApi guest proxy failed for {Path}", path);
            return (502, "{\"error\":\"StudentApi ไม่ตอบสนอง\"}", "application/json");
        }
    }
}
