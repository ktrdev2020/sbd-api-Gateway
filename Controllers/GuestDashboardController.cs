using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #44 — Public dashboard endpoints for the home landing page.
/// 3 anonymous, response-cached endpoints aggregated server-side so the home
/// page makes 3 calls instead of 6+ (mobile-friendly).
///
/// Sources:
///   • <c>Schools</c> (Gateway DB)        — counts, districts, established dates
///   • <c>Personnel*</c> (Gateway DB)     — live personnel breakdown by type
///   • <c>StudentApi</c> (cross-pod)      — DMC-imported student counts (full
///     2568 T2 snapshot · 27,966 students across 197 schools)
///
/// Multi-year: only AY 2568 has full DMC data today. Other years return
/// <c>{ academicYear, dataAvailable: false }</c> so the frontend can render
/// "เร็วๆ นี้" placeholders for past years (per user direction 2026-05-11).
/// </summary>
[ApiController]
[Route("api/v1/guest/dashboard")]
[AllowAnonymous]
public class GuestDashboardController : ControllerBase
{
    private const int AreaId = 33030000;
    private const int CacheSeconds = 3600;
    /// <summary>The single AY with full DMC data today (Plan #44). Other years
    /// in the trends array are flagged dataAvailable=false.</summary>
    private const int CurrentDmcYear = 2568;

    private readonly SbdDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<GuestDashboardController> _logger;

    public GuestDashboardController(
        SbdDbContext db,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<GuestDashboardController> logger)
    {
        _db = db;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private string StudentApiBase =>
        _config["ServiceUrls:StudentApi"]
        ?? Environment.GetEnvironmentVariable("STUDENT_API_URL")
        ?? "http://localhost:5032";

    // ── 1. Overview (KPI strip) ───────────────────────────────────────────

    /// <summary>Top-of-page snapshot: 4 KPIs + earliest/latest established year.</summary>
    [HttpGet("overview")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<DashboardOverviewDto>> GetOverview(CancellationToken ct)
    {
        var schoolsQ = _db.Schools
            .Where(s => s.AreaId == AreaId && s.IsActive && s.DeletedAt == null);

        var totalSchools = await schoolsQ.CountAsync(ct);

        var totalDistricts = await schoolsQ
            .Where(s => s.Address != null && s.Address.SubDistrict != null)
            .Select(s => s.Address!.SubDistrict!.District.Id)
            .Distinct()
            .CountAsync(ct);

        // Personnel — live aggregate (Director + Teacher + Staff types).
        var totalPersonnel = await (
            from psa in _db.Set<SBD.Domain.Entities.PersonnelSchoolAssignment>().AsNoTracking()
            join s in schoolsQ on psa.SchoolCode equals s.SchoolCode
            where psa.EndDate == null || psa.EndDate >= DateOnly.FromDateTime(DateTime.Today)
            select psa.PersonnelId
        ).Distinct().CountAsync(ct);

        // Established date range — for "ก่อตั้ง 2466 - 2532" stat card.
        var estRange = await schoolsQ
            .Where(s => s.EstablishedDate != null)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Earliest = g.Min(s => s.EstablishedDate),
                Latest = g.Max(s => s.EstablishedDate),
                WithDate = g.Count(),
            })
            .FirstOrDefaultAsync(ct);

        // Students — cross-pod call (current DMC AY 2568 T2). Soft fallback.
        var totalStudents = await FetchStudentCountAsync(ct);

        var earliestYearTh = estRange?.Earliest?.Year + 543;
        var latestYearTh = estRange?.Latest?.Year + 543;

        return Ok(new DashboardOverviewDto(
            AreaId: AreaId,
            AreaNameTh: "สำนักงานเขตพื้นที่การศึกษาประถมศึกษาศรีสะเกษ เขต 3",
            TotalSchools: totalSchools,
            TotalStudents: totalStudents,
            TotalPersonnel: totalPersonnel,
            TotalDistricts: totalDistricts,
            EarliestEstablishedYearTh: earliestYearTh,
            LatestEstablishedYearTh: latestYearTh,
            SchoolsWithEstablishedDate: estRange?.WithDate ?? 0,
            DataSourceAcademicYear: CurrentDmcYear,
            DataSourceTerm: 2));
    }

    // ── 2. Distributions (donut data) ─────────────────────────────────────

    /// <summary>5 donut datasets in one call: sizes, levels, grades (15-bar
    /// stack), personnel types, districts.</summary>
    [HttpGet("distributions")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<DashboardDistributionsDto>> GetDistributions(CancellationToken ct)
    {
        var schoolsQ = _db.Schools
            .Where(s => s.AreaId == AreaId && s.IsActive && s.DeletedAt == null);

        // Personnel by type · live · classified the same way as
        // SchoolStatsController.AutoFetchPersonnel for naming consistency.
        var personnelRaw = await (
            from psa in _db.Set<SBD.Domain.Entities.PersonnelSchoolAssignment>().AsNoTracking()
            join p in _db.Personnel.AsNoTracking() on psa.PersonnelId equals p.Id
            join pt in _db.Set<SBD.Domain.Entities.PersonnelType>().AsNoTracking() on p.PersonnelTypeId equals pt.Id
            join s in schoolsQ on psa.SchoolCode equals s.SchoolCode
            where psa.EndDate == null || psa.EndDate >= DateOnly.FromDateTime(DateTime.Today)
            select new { Code = pt.Code, Position = psa.Position ?? string.Empty, p.Gender }
        ).ToListAsync(ct);

        var personnelTypes = personnelRaw
            .GroupBy(r => ClassifyPersonnel(r.Code, r.Position))
            .Select(g => new DashboardSliceDto(
                Id: SlugifyPersonnelType(g.Key.Name),
                Label: g.Key.Name,
                Value: g.Count(),
                ColorHint: g.Key.Color,
                SortOrder: g.Key.Order))
            .OrderBy(s => s.SortOrder)
            .ToList();

        // Districts · school count · same chain as GuestSchoolInfoController.
        var districtsRaw = await schoolsQ
            .Where(s => s.Address != null && s.Address.SubDistrict != null)
            .GroupBy(s => s.Address!.SubDistrict!.District.NameTh)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var districtPalette = new[] { "#38bdf8", "#a78bfa", "#34d399", "#fbbf24", "#f472b6", "#22d3ee" };
        var districts = districtsRaw
            .OrderByDescending(d => d.Count)
            .Select((d, i) => new DashboardSliceDto(
                Id: SlugFromDistrict(d.Name),
                Label: d.Name,
                Value: d.Count,
                ColorHint: districtPalette[i % districtPalette.Length],
                SortOrder: i))
            .ToList();

        // Cross-pod · sizes + levels + grades from StudentApi (DMC).
        var studentBundle = await FetchStudentBundleAsync(ct);

        return Ok(new DashboardDistributionsDto(
            AcademicYear: CurrentDmcYear,
            Term: 2,
            SchoolSizes: studentBundle.Sizes,
            StudentLevels: studentBundle.Levels,
            StudentGrades: studentBundle.Grades,
            PersonnelTypes: personnelTypes,
            Districts: districts));
    }

    // ── 3. Trends (multi-year line chart) ─────────────────────────────────

    /// <summary>5-year (or N-year) array. Only the year(s) with real data
    /// have <c>dataAvailable=true</c>; placeholders otherwise so the
    /// frontend can render dashed segments + "เร็วๆ นี้" overlay.</summary>
    [HttpGet("trends")]
    // Plan #44 — VaryByQueryKeys would throw InvalidOperationException (Gateway has
    // no ResponseCachingMiddleware registered, per gateway-response-cache-vary-trap
    // memory). Edge cache (Cloudflare/HAProxy) varies per-URL by default anyway.
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<DashboardTrendsDto>> GetTrends(
        [FromQuery] int years = 5,
        CancellationToken ct = default)
    {
        years = Math.Clamp(years, 1, 10);

        // Anchor on current Thai AY (calendar > May = +543; else +542 — same
        // logic as SchoolStatsController.CurrentAcademicYear).
        var now = DateTime.Now;
        var currentAy = now.Year + 543;
        if (now.Month < 5) currentAy -= 1;

        var startAy = currentAy - years + 1;

        // Real data only exists for CurrentDmcYear (2568) right now.
        var currentSnapshot = await FetchStudentCountAsync(ct);
        var currentSchools = await _db.Schools
            .Where(s => s.AreaId == AreaId && s.IsActive && s.DeletedAt == null)
            .CountAsync(ct);
        var currentPersonnel = await (
            from psa in _db.Set<SBD.Domain.Entities.PersonnelSchoolAssignment>().AsNoTracking()
            join s in _db.Schools.AsNoTracking()
                on psa.SchoolCode equals s.SchoolCode
            where s.AreaId == AreaId && s.IsActive && s.DeletedAt == null
              && (psa.EndDate == null || psa.EndDate >= DateOnly.FromDateTime(DateTime.Today))
            select psa.PersonnelId
        ).Distinct().CountAsync(ct);

        var points = new List<DashboardTrendPointDto>();
        for (int ay = startAy; ay <= currentAy; ay++)
        {
            if (ay == CurrentDmcYear)
            {
                points.Add(new DashboardTrendPointDto(
                    AcademicYear: ay,
                    DataAvailable: true,
                    Schools: currentSchools,
                    Students: currentSnapshot,
                    Personnel: currentPersonnel));
            }
            else
            {
                points.Add(new DashboardTrendPointDto(
                    AcademicYear: ay,
                    DataAvailable: false,
                    Schools: null,
                    Students: null,
                    Personnel: null));
            }
        }

        return Ok(new DashboardTrendsDto(
            StartYear: startAy,
            EndYear: currentAy,
            Points: points,
            ComingSoonNote: "ข้อมูลย้อนหลัง 5 ปี กำลังนำเข้าจาก DMC archive — เร็วๆ นี้"));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<int> FetchStudentCountAsync(CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(8);
            var url = $"{StudentApiBase}/api/v1/guest/student-info/summary";
            var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("StudentApi summary returned {Status}", resp.StatusCode);
                return 0;
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("totalStudents", out var v) && v.TryGetInt32(out var n) ? n : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch student summary from StudentApi");
            return 0;
        }
    }

    private async Task<StudentBundle> FetchStudentBundleAsync(CancellationToken ct)
    {
        var bundle = new StudentBundle(
            new List<DashboardSliceDto>(),
            new List<DashboardSliceDto>(),
            new List<DashboardGradeBarDto>());

        try
        {
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(8);
            var baseUrl = StudentApiBase;

            // Sizes
            var sizesResp = await http.GetAsync($"{baseUrl}/api/v1/guest/student-info/school-size-buckets", ct);
            if (sizesResp.IsSuccessStatusCode)
            {
                var json = await sizesResp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                int Read(string n) => doc.RootElement.TryGetProperty(n, out var v) && v.TryGetInt32(out var x) ? x : 0;
                bundle = bundle with
                {
                    Sizes = new List<DashboardSliceDto>
                    {
                        new("small",  "ขนาดเล็ก (<120)",          Read("small"),  "#38bdf8", 0),
                        new("medium", "ขนาดกลาง (120-499)",      Read("medium"), "#a78bfa", 1),
                        new("large",  "ขนาดใหญ่ (500-1499)",     Read("large"),  "#fbbf24", 2),
                        new("xlarge", "ขนาดใหญ่พิเศษ (≥1500)", Read("xlarge"), "#f472b6", 3),
                    },
                };
            }

            // Levels
            var levelsResp = await http.GetAsync($"{baseUrl}/api/v1/guest/student-info/by-level", ct);
            if (levelsResp.IsSuccessStatusCode)
            {
                var json = await levelsResp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var palette = new[] { "#34d399", "#38bdf8", "#a78bfa", "#fbbf24" };
                var levelsList = new List<DashboardSliceDto>();
                int idx = 0;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var id = el.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                    var name = el.TryGetProperty("name", out var nv) ? nv.GetString() ?? "" : "";
                    var count = el.TryGetProperty("count", out var c) && c.TryGetInt32(out var cn) ? cn : 0;
                    levelsList.Add(new DashboardSliceDto(id, name, count, palette[idx % palette.Length], idx));
                    idx++;
                }
                bundle = bundle with { Levels = levelsList };
            }

            // Grades (15-bar stack)
            var gradesResp = await http.GetAsync($"{baseUrl}/api/v1/guest/student-info/by-class", ct);
            if (gradesResp.IsSuccessStatusCode)
            {
                var json = await gradesResp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var grades = new List<DashboardGradeBarDto>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    grades.Add(new DashboardGradeBarDto(
                        Code: el.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "",
                        NameTh: el.TryGetProperty("nameTh", out var n) ? n.GetString() ?? "" : "",
                        LevelOrder: el.TryGetProperty("levelOrder", out var lo) && lo.TryGetInt32(out var loi) ? loi : 0,
                        Total: el.TryGetProperty("total", out var t) && t.TryGetInt32(out var ti) ? ti : 0,
                        Male: el.TryGetProperty("male", out var m) && m.TryGetInt32(out var mi) ? mi : 0,
                        Female: el.TryGetProperty("female", out var f) && f.TryGetInt32(out var fi) ? fi : 0,
                        Schools: el.TryGetProperty("schools", out var sc) && sc.TryGetInt32(out var sci) ? sci : 0));
                }
                bundle = bundle with { Grades = grades };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch student bundle from StudentApi");
        }

        return bundle;
    }

    private static (string Name, int Order, string Color) ClassifyPersonnel(string code, string position)
    {
        switch (code)
        {
            case "Director":
                return position.Contains("รอง")
                    ? ("รองผู้อำนวยการ", 1, "#a78bfa")
                    : ("ผู้อำนวยการ", 0, "#6366f1");
            case "Teacher": return ("ครู", 2, "#34d399");
            case "GovEmployee": return ("พนักงานราชการ", 3, "#22d3ee");
            case "PermanentStaff": return ("ลูกจ้างประจำ", 4, "#fbbf24");
            case "TempStaff": return ("ลูกจ้างชั่วคราว", 5, "#fb923c");
            case "Staff":
                if (position.Contains("ธุรการ")) return ("ธุรการ", 6, "#f472b6");
                if (position.Contains("ภารโรง") || position.Contains("นักการ")) return ("นักการภารโรง", 7, "#fb7185");
                if (position.Contains("พี่เลี้ยง")) return ("พี่เลี้ยง", 8, "#fda4af");
                return ("บุคลากรสนับสนุน", 9, "#94a3b8");
            default: return (code, 99, "#94a3b8");
        }
    }

    private static string SlugifyPersonnelType(string name) => name switch
    {
        "ผู้อำนวยการ" => "director",
        "รองผู้อำนวยการ" => "deputy-director",
        "ครู" => "teacher",
        "พนักงานราชการ" => "gov-employee",
        "ลูกจ้างประจำ" => "permanent-staff",
        "ลูกจ้างชั่วคราว" => "temp-staff",
        "ธุรการ" => "clerical",
        "นักการภารโรง" => "janitor",
        "พี่เลี้ยง" => "nanny",
        _ => name.ToLowerInvariant(),
    };

    private static string SlugFromDistrict(string name) => name switch
    {
        "ขุขันธ์" => "khukhan",
        "ปรางค์กู่" => "prangku",
        "ภูสิงห์" => "phusing",
        "ไพรบึง" => "phraibung",
        _ => name.ToLowerInvariant(),
    };
}

// ── DTOs ────────────────────────────────────────────────────────────────────

public record DashboardOverviewDto(
    int AreaId,
    string AreaNameTh,
    int TotalSchools,
    int TotalStudents,
    int TotalPersonnel,
    int TotalDistricts,
    int? EarliestEstablishedYearTh,
    int? LatestEstablishedYearTh,
    int SchoolsWithEstablishedDate,
    int DataSourceAcademicYear,
    int DataSourceTerm);

public record DashboardSliceDto(
    string Id,
    string Label,
    int Value,
    string ColorHint,
    int SortOrder);

public record DashboardGradeBarDto(
    string Code,
    string NameTh,
    int LevelOrder,
    int Total,
    int Male,
    int Female,
    int Schools);

public record DashboardDistributionsDto(
    int AcademicYear,
    int Term,
    IReadOnlyList<DashboardSliceDto> SchoolSizes,
    IReadOnlyList<DashboardSliceDto> StudentLevels,
    IReadOnlyList<DashboardGradeBarDto> StudentGrades,
    IReadOnlyList<DashboardSliceDto> PersonnelTypes,
    IReadOnlyList<DashboardSliceDto> Districts);

public record DashboardTrendPointDto(
    int AcademicYear,
    bool DataAvailable,
    int? Schools,
    int? Students,
    int? Personnel);

public record DashboardTrendsDto(
    int StartYear,
    int EndYear,
    IReadOnlyList<DashboardTrendPointDto> Points,
    string? ComingSoonNote);

internal sealed record StudentBundle(
    List<DashboardSliceDto> Sizes,
    List<DashboardSliceDto> Levels,
    List<DashboardGradeBarDto> Grades);
