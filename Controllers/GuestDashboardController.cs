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

        // Plan #44 S4 — academic-rank (วิทยฐานะ) breakdown.
        // Personnel.AcademicStandingTypeId is currently <1% populated in
        // production (0.04% as of 2026-05-11) — chart is rendered as a
        // placeholder card with the coming-soon note. We still emit slices
        // for the few records that exist so the chart fills in as HR imports.
        var rankRaw = await (
            from p in _db.Personnel.AsNoTracking()
            join psa in _db.Set<SBD.Domain.Entities.PersonnelSchoolAssignment>().AsNoTracking()
                on p.Id equals psa.PersonnelId
            join s in schoolsQ on psa.SchoolCode equals s.SchoolCode
            where p.TrashedAt == null
              && (psa.EndDate == null || psa.EndDate >= DateOnly.FromDateTime(DateTime.Today))
            select new { p.AcademicStandingTypeId }
        ).ToListAsync(ct);
        var rankTotal = rankRaw.Count;
        var rankFilled = rankRaw.Count(r => r.AcademicStandingTypeId != null);
        var rankPct = rankTotal > 0 ? (double)rankFilled / rankTotal : 0;
        IReadOnlyList<DashboardSliceDto> academicRanks = Array.Empty<DashboardSliceDto>();
        if (rankFilled > 0)
        {
            var rankLookup = await _db.Set<SBD.Domain.Entities.AcademicStandingType>()
                .AsNoTracking()
                .ToDictionaryAsync(t => t.Id, t => t, ct);
            var rankPalette = new[] { "#94a3b8", "#34d399", "#22d3ee", "#a78bfa", "#6366f1" };
            academicRanks = rankRaw
                .Where(r => r.AcademicStandingTypeId != null)
                .GroupBy(r => r.AcademicStandingTypeId!.Value)
                .Select((g, i) =>
                {
                    var name = rankLookup.TryGetValue(g.Key, out var t) ? t.NameTh : "ไม่ระบุ";
                    return new DashboardSliceDto(
                        Id: $"rank-{g.Key}",
                        Label: name,
                        Value: g.Count(),
                        ColorHint: rankPalette[i % rankPalette.Length],
                        SortOrder: g.Key);
                })
                .OrderBy(s => s.SortOrder)
                .ToList();
        }
        var academicRankNote = rankPct < 0.05
            ? $"ข้อมูลวิทยฐานะกรอกแล้ว {rankFilled}/{rankTotal:N0} ({rankPct:P1}) — เร็วๆ นี้: รอ HR import ครบ"
            : null;

        return Ok(new DashboardDistributionsDto(
            AcademicYear: CurrentDmcYear,
            Term: 2,
            SchoolSizes: studentBundle.Sizes,
            StudentLevels: studentBundle.Levels,
            StudentGrades: studentBundle.Grades,
            PersonnelTypes: personnelTypes,
            Districts: districts,
            AcademicRanks: academicRanks,
            AcademicRankNote: academicRankNote));
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

    // ── 4. District comparison (radar chart) ──────────────────────────────

    /// <summary>Plan #47 — per-district counts across 5 metrics for the radar
    /// chart on the public home page. All metrics derive from Gateway DB
    /// (no cross-pod fan-out) so the endpoint stays sub-100ms.
    /// Each district publishes raw counts plus normalized 0..1 values relative
    /// to the cross-district max for the same axis (so radar polygons compare
    /// fairly without imposing a domain expert's weighting).</summary>
    [HttpGet("district-comparison")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<DashboardDistrictComparisonDto>> GetDistrictComparison(CancellationToken ct)
    {
        var schoolsQ = _db.Schools
            .Where(s => s.AreaId == AreaId && s.IsActive && s.DeletedAt == null);

        var schoolsByDistrict = await schoolsQ
            .Where(s => s.Address != null && s.Address.SubDistrict != null)
            .GroupBy(s => s.Address!.SubDistrict!.District.NameTh)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Personnel rows joined to school → district, classified.
        var personnelRaw = await (
            from psa in _db.Set<SBD.Domain.Entities.PersonnelSchoolAssignment>().AsNoTracking()
            join p in _db.Personnel.AsNoTracking() on psa.PersonnelId equals p.Id
            join pt in _db.Set<SBD.Domain.Entities.PersonnelType>().AsNoTracking() on p.PersonnelTypeId equals pt.Id
            join s in schoolsQ on psa.SchoolCode equals s.SchoolCode
            where (psa.EndDate == null || psa.EndDate >= DateOnly.FromDateTime(DateTime.Today))
              && s.Address != null && s.Address.SubDistrict != null
            select new
            {
                District = s.Address!.SubDistrict!.District.NameTh,
                Code = pt.Code,
                Position = psa.Position ?? string.Empty,
            }
        ).ToListAsync(ct);

        var personnelByDistrict = personnelRaw
            .GroupBy(r => r.District)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var classified = g
                        .Select(r => ClassifyPersonnel(r.Code, r.Position).Name)
                        .ToList();
                    return new
                    {
                        Total = classified.Count,
                        Director = classified.Count(n => n.Contains("ผอ.") || n.Contains("ผู้บริหาร") || n == "ผู้อำนวยการ"),
                        Teacher = classified.Count(n => n == "ครู" || n.Contains("ครู") && !n.Contains("พี่เลี้ยง")),
                        Support = classified.Count(n => n == "ภารโรง" || n == "ธุรการ" || n.Contains("สนับสนุน") || n.Contains("พี่เลี้ยง") || n.Contains("อัตราจ้าง")),
                    };
                });

        var axes = new List<DashboardAxisDto>
        {
            new("schools",   "โรงเรียน"),
            new("personnel", "บุคลากรรวม"),
            new("directors", "ผอ./รอง"),
            new("teachers",  "ครู"),
            new("support",   "สนับสนุน"),
        };

        var palette = new[] { "#38bdf8", "#a78bfa", "#34d399", "#fbbf24", "#f472b6", "#22d3ee" };

        var districtRows = schoolsByDistrict
            .OrderByDescending(d => d.Count)
            .Select((d, i) =>
            {
                personnelByDistrict.TryGetValue(d.Name, out var pp);
                var raw = new[]
                {
                    (double)d.Count,
                    (double)(pp?.Total ?? 0),
                    (double)(pp?.Director ?? 0),
                    (double)(pp?.Teacher ?? 0),
                    (double)(pp?.Support ?? 0),
                };
                return new
                {
                    Id = SlugFromDistrict(d.Name),
                    Label = d.Name,
                    Color = palette[i % palette.Length],
                    Raw = raw,
                };
            })
            .ToList();

        // Normalize 0..1 per axis using cross-district max.
        var axisCount = axes.Count;
        var maxes = new double[axisCount];
        for (int a = 0; a < axisCount; a++)
        {
            maxes[a] = districtRows.Count == 0 ? 1 : Math.Max(1, districtRows.Max(d => d.Raw[a]));
        }

        var seriesOut = districtRows
            .Select(d => new DashboardDistrictSeriesDto(
                Id: d.Id,
                Label: d.Label,
                Color: d.Color,
                Raw: d.Raw,
                Values: Enumerable.Range(0, axisCount).Select(a => d.Raw[a] / maxes[a]).ToArray()))
            .ToList();

        return Ok(new DashboardDistrictComparisonDto(Axes: axes, Series: seriesOut));
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

    // Plan #44 S4: position-first classifier so positions like "ครูผู้สอน",
    // "ครูอัตราจ้าง", "ครูขาดแคลน" land in "ครู" instead of "บุคลากรสนับสนุน"
    // (these have PersonnelType=Staff but are functionally teachers).
    private static (string Name, int Order, string Color) ClassifyPersonnel(string code, string position)
    {
        if (position.Contains("ผู้อำนวยการ"))
            return position.Contains("รอง")
                ? ("รองผู้อำนวยการ", 1, "#a78bfa")
                : ("ผู้อำนวยการ", 0, "#6366f1");
        if (position.Contains("ภารโรง") || position.Contains("นักการ"))
            return ("นักการภารโรง", 6, "#fb7185");
        if (position.Contains("ธุรการ"))
            return ("ธุรการ", 5, "#f472b6");
        if (position.Contains("พี่เลี้ยง"))
            return ("พี่เลี้ยง", 7, "#fda4af");
        if (position.Contains("ครู"))
            return ("ครู", 2, "#34d399");
        if (position.Contains("ลูกจ้างชั่วคราว") || position.Contains("อัตราจ้าง"))
            return ("ลูกจ้างชั่วคราว / อัตราจ้าง", 4, "#fb923c");
        if (position.Contains("พนักงาน"))
            return ("พนักงาน", 3, "#22d3ee");
        return code switch
        {
            "GovEmployee" => ("พนักงานราชการ", 3, "#22d3ee"),
            "PermanentStaff" => ("ลูกจ้างประจำ", 8, "#fbbf24"),
            "TempStaff" => ("ลูกจ้างชั่วคราว / อัตราจ้าง", 4, "#fb923c"),
            "Director" => ("ผู้อำนวยการ", 0, "#6366f1"),
            "Teacher" => ("ครู", 2, "#34d399"),
            _ => ("บุคลากรอื่นๆ", 9, "#94a3b8"),
        };
    }

    private static string SlugifyPersonnelType(string name) => name switch
    {
        "ผู้อำนวยการ" => "director",
        "รองผู้อำนวยการ" => "deputy-director",
        "ครู" => "teacher",
        "พนักงาน" => "officer",
        "พนักงานราชการ" => "gov-employee",
        "ลูกจ้างประจำ" => "permanent-staff",
        "ลูกจ้างชั่วคราว" => "temp-staff",
        "ลูกจ้างชั่วคราว / อัตราจ้าง" => "contract-staff",
        "ธุรการ" => "clerical",
        "นักการภารโรง" => "janitor",
        "พี่เลี้ยง" => "nanny",
        "บุคลากรอื่นๆ" => "other",
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
    IReadOnlyList<DashboardSliceDto> Districts,
    IReadOnlyList<DashboardSliceDto> AcademicRanks,
    string? AcademicRankNote);

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

public record DashboardAxisDto(
    string Id,
    string Label);

public record DashboardDistrictSeriesDto(
    string Id,
    string Label,
    string Color,
    double[] Raw,
    double[] Values);

public record DashboardDistrictComparisonDto(
    IReadOnlyList<DashboardAxisDto> Axes,
    IReadOnlyList<DashboardDistrictSeriesDto> Series);
