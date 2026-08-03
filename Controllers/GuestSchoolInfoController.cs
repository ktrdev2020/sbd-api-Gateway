using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #7 — Public guest endpoints for the /school-info portal page.
/// All actions are anonymous, response-cached for 1 hour.
/// Scoped to สพป.ศก.3 (AreaId = 33030000), IsActive=true, DeletedAt IS NULL.
///
/// `GetSummary` calls StudentApi cross-pod for the OBEC size buckets since
/// Schools.SchoolSizeStd4/Std7 columns are unpopulated (per plan #7 D2).
/// </summary>
[ApiController]
[Route("api/v1/guest/school-info")]
[AllowAnonymous]
public class GuestSchoolInfoController : ControllerBase
{
    private const int AreaId = 33030000;
    private const int CacheSeconds = 3600;

    private readonly SbdDbContext _context;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<GuestSchoolInfoController> _logger;

    public GuestSchoolInfoController(
        SbdDbContext context,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<GuestSchoolInfoController> logger)
    {
        _context = context;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private string StudentApiBase =>
        _config["ServiceUrls:StudentApi"]
        ?? Environment.GetEnvironmentVariable("STUDENT_API_URL")
        ?? "http://localhost:5032";

    /// <summary>4 stat cards (total + 3 size buckets) + district breakdown.</summary>
    [HttpGet("summary")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestSchoolSummaryDto>> GetSummary(CancellationToken ct)
    {
        var schoolsQ = _context.Schools
            .Where(s => s.AreaId == AreaId && s.IsActive && s.DeletedAt == null);

        var total = await schoolsQ.CountAsync(ct);

        // Districts via Schools→Address→SubDistrict→District chain.
        var districtsRaw = await schoolsQ
            .Where(s => s.Address != null && s.Address.SubDistrict != null)
            .GroupBy(s => s.Address!.SubDistrict!.District.NameTh)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var districts = districtsRaw
            .OrderByDescending(d => d.Count)
            .Select(d => new GuestSchoolDistrictDto(
                Id: SlugFromDistrict(d.Name),
                Name: d.Name,
                Count: d.Count
            ))
            .ToList();

        // Cross-pod call StudentApi for size buckets (D2). Soft fallback to zeros
        // if StudentApi is unreachable so the page still renders.
        var sizes = await FetchSizeBucketsAsync(ct);

        return Ok(new GuestSchoolSummaryDto(
            TotalSchools: total,
            Sizes: sizes,
            Districts: districts
        ));
    }

    /// <summary>Paginated registration list — schoolCode + nameTh + principal + establishedYearTh.</summary>
    [HttpGet("registration")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestSchoolListPageDto<GuestSchoolRegistrationRowDto>>> GetRegistration(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? q = null,
        CancellationToken ct = default)
    {
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 200);

        var baseQ = _context.Schools
            .Where(s => s.AreaId == AreaId && s.IsActive && s.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            baseQ = baseQ.Where(s => EF.Functions.ILike(s.NameTh, like) || EF.Functions.ILike(s.SchoolCode, like));
        }

        var total = await baseQ.CountAsync(ct);

        var raw = await baseQ
            .OrderBy(s => s.SchoolCode)
            .Skip(offset)
            .Take(limit)
            .Select(s => new
            {
                s.SchoolCode,
                s.SmisCode,
                s.NameTh,
                Principal = s.Principal,
                s.EstablishedDate,
            })
            .ToListAsync(ct);

        var rows = raw
            .Select(s => new GuestSchoolRegistrationRowDto(
                SchoolCode: s.SchoolCode,
                SmisCode: s.SmisCode,
                NameTh: s.NameTh,
                Principal: s.Principal,
                EstablishedYearTh: s.EstablishedDate.HasValue
                    ? s.EstablishedDate.Value.Year + 543
                    : (int?)null
            ))
            .ToList();

        return Ok(new GuestSchoolListPageDto<GuestSchoolRegistrationRowDto>(
            Data: rows,
            Total: total,
            Offset: offset,
            Limit: limit
        ));
    }

    /// <summary>Paginated address list — village + sub-district + district + phone.</summary>
    [HttpGet("address")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestSchoolListPageDto<GuestSchoolAddressRowDto>>> GetAddress(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? q = null,
        CancellationToken ct = default)
    {
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 200);

        var baseQ = _context.Schools
            .Where(s => s.AreaId == AreaId && s.IsActive && s.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            baseQ = baseQ.Where(s => EF.Functions.ILike(s.NameTh, like) || EF.Functions.ILike(s.SchoolCode, like));
        }

        var total = await baseQ.CountAsync(ct);

        var raw = await baseQ
            .OrderBy(s => s.SchoolCode)
            .Skip(offset)
            .Take(limit)
            .Select(s => new
            {
                s.SchoolCode,
                s.NameTh,
                s.Phone,
                VillageName = s.Address != null ? s.Address.VillageName : null,
                SubDistrict = s.Address != null && s.Address.SubDistrict != null
                    ? s.Address.SubDistrict.NameTh
                    : null,
                District = s.Address != null && s.Address.SubDistrict != null
                    ? s.Address.SubDistrict.District.NameTh
                    : null,
            })
            .ToListAsync(ct);

        var rows = raw
            .Select(s => new GuestSchoolAddressRowDto(
                SchoolCode: s.SchoolCode,
                NameTh: s.NameTh,
                VillageName: s.VillageName,
                SubDistrict: s.SubDistrict,
                District: s.District,
                Phone: s.Phone
            ))
            .ToList();

        return Ok(new GuestSchoolListPageDto<GuestSchoolAddressRowDto>(
            Data: rows,
            Total: total,
            Offset: offset,
            Limit: limit
        ));
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<GuestSchoolSizeDto>> FetchSizeBucketsAsync(CancellationToken ct)
    {
        // Soft fallback values — used if cross-pod call fails so /summary still renders.
        var fallback = new List<GuestSchoolSizeDto>
        {
            new("small",  "ขนาดเล็ก",        0, "fas fa-school"),
            new("medium", "ขนาดกลาง",         0, "fas fa-school"),
            new("large",  "ขนาดใหญ่",         0, "fas fa-school"),
            new("xlarge", "ขนาดใหญ่พิเศษ", 0, "fas fa-school"),
        };

        try
        {
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(8);
            var url = $"{StudentApiBase}/api/v1/guest/student-info/school-size-buckets";
            var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("StudentApi school-size-buckets returned {Status}", resp.StatusCode);
                return fallback;
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int Read(string name) =>
                root.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
            // Property names from .NET serializer are camelCase by default in this project.
            int small  = Read("small");
            int medium = Read("medium");
            int large  = Read("large");
            int xlarge = Read("xlarge");

            return new List<GuestSchoolSizeDto>
            {
                new("small",  "ขนาดเล็ก",        small,  "fas fa-school"),
                new("medium", "ขนาดกลาง",         medium, "fas fa-school"),
                new("large",  "ขนาดใหญ่",         large,  "fas fa-school"),
                new("xlarge", "ขนาดใหญ่พิเศษ", xlarge, "fas fa-school"),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch school-size-buckets from StudentApi");
            return fallback;
        }
    }

    // ═══════════════ Plan #107 — classification + map (real DMC school data) ═══

    /// <summary>
    /// School classification from real data: teaching levels (DMC ชั้นต่ำสุด/สูงสุด),
    /// establishment decades, infrastructure (ไฟฟ้า/เน็ต/น้ำ), director coverage.
    /// </summary>
    [HttpGet("classification")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestSchoolClassificationDto>> GetClassification(CancellationToken ct)
    {
        var levels = await _context.Database.SqlQuery<LevelAggRow>($"""
            SELECT
              COUNT(*) FILTER (WHERE "TeachesUpperSecondary")::int AS "ToM6",
              COUNT(*) FILTER (WHERE "TeachesLowerSecondary" AND NOT COALESCE("TeachesUpperSecondary", false))::int AS "ToM3",
              COUNT(*) FILTER (WHERE "TeachesPrimary" AND NOT COALESCE("TeachesLowerSecondary", false))::int AS "ToP6",
              COUNT(*) FILTER (WHERE COALESCE("TeachesPreschool", false) AND NOT COALESCE("TeachesPrimary", false))::int AS "PreOnly",
              COUNT(*)::int AS "Total"
            FROM "Schools" WHERE "DeletedAt" IS NULL AND "IsActive"
            """).FirstAsync(ct);

        var decades = await _context.Database.SqlQuery<DecadeRow>($"""
            SELECT ((EXTRACT(YEAR FROM "EstablishedDate")::int + 543) / 10 * 10) AS "DecadeBe",
                   COUNT(*)::int AS "Count"
            FROM "Schools" WHERE "DeletedAt" IS NULL AND "IsActive" AND "EstablishedDate" IS NOT NULL
            GROUP BY 1 ORDER BY 1
            """).ToListAsync(ct);

        var infra = await _context.Database.SqlQuery<InfraAggRow>($"""
            SELECT
              COUNT(*) FILTER (WHERE "HasElectricity")::int AS "Electricity",
              COUNT(*) FILTER (WHERE "HasInternet")::int AS "Internet",
              COUNT(*) FILTER (WHERE "HasWater")::int AS "Water",
              COUNT(*)::int AS "Total"
            FROM "SchoolInfraSnapshots"
            WHERE "Year" = (SELECT MAX("Year") FROM "SchoolInfraSnapshots")
            """).FirstOrDefaultAsync(ct);

        var inetTypes = await _context.Database.SqlQuery<NameCountRow>($"""
            SELECT COALESCE("InternetType", 'ไม่ระบุ') AS "Name", COUNT(*)::int AS "Count"
            FROM "SchoolInfraSnapshots"
            WHERE "Year" = (SELECT MAX("Year") FROM "SchoolInfraSnapshots") AND "HasInternet"
            GROUP BY 1 ORDER BY 2 DESC
            """).ToListAsync(ct);

        var director = await _context.Database.SqlQuery<DirectorAggRow>($"""
            SELECT
              COUNT(DISTINCT a."SchoolCode") FILTER (WHERE a."Position" = 'ผู้อำนวยการสถานศึกษา')::int AS "WithDirector",
              COUNT(DISTINCT a."SchoolCode")::int AS "SchoolsWithStaff",
              COUNT(*)::int AS "TotalStaff"
            FROM "PersonnelSchoolAssignments" a WHERE a."EndDate" IS NULL
            """).FirstAsync(ct);

        return Ok(new GuestSchoolClassificationDto(
            new GuestLevelBreakdownDto(levels.Total, levels.ToP6, levels.ToM3, levels.ToM6, levels.PreOnly),
            decades.Select(d => new GuestDecadeDto((int)d.DecadeBe, d.Count)).ToList(),
            infra is null ? null : new GuestInfraDto(infra.Total, infra.Electricity, infra.Internet, infra.Water,
                inetTypes.Select(t => new GuestNameCountDto(t.Name, t.Count)).ToList()),
            new GuestDirectorCoverageDto(director.WithDirector, levels.Total - director.WithDirector, director.TotalStaff)));
    }

    /// <summary>All schools with coordinates (196/196 from DMC) for the map view.</summary>
    [HttpGet("map")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<IEnumerable<GuestSchoolMapDto>>> GetMap(CancellationToken ct)
    {
        var rows = await _context.Database.SqlQuery<GuestSchoolMapDto>($"""
            SELECT s."SmisCode" AS "SmisCode", s."NameTh" AS "Name",
                   s."Latitude" AS "Lat", s."Longitude" AS "Lng",
                   CASE WHEN "TeachesUpperSecondary" THEN 'm6'
                        WHEN "TeachesLowerSecondary" THEN 'm3'
                        ELSE 'p6' END AS "LevelType",
                   d."NameTh" AS "District"
            FROM "Schools" s
            LEFT JOIN "Addresses" a ON a."Id" = s."AddressId"
            LEFT JOIN "SubDistricts" sd ON sd."Id" = a."SubDistrictId"
            LEFT JOIN "Districts" d ON d."Id" = sd."DistrictId"
            WHERE s."DeletedAt" IS NULL AND s."IsActive" AND s."Latitude" IS NOT NULL
            ORDER BY s."NameTh"
            """).ToListAsync(ct);
        return Ok(rows);
    }

    private static string SlugFromDistrict(string name) => name switch
    {
        "ขุขันธ์"   => "khukhan",
        "ปรางค์กู่" => "prangku",
        "ภูสิงห์"    => "phusing",
        "ไพรบึง"    => "phraibung",
        _            => name.ToLowerInvariant(),
    };
}

// ── DTOs ────────────────────────────────────────────────────────────────

public record GuestSchoolSummaryDto(
    int TotalSchools,
    IReadOnlyList<GuestSchoolSizeDto> Sizes,
    IReadOnlyList<GuestSchoolDistrictDto> Districts
);

public record GuestSchoolSizeDto(string Id, string Name, int Count, string Icon);

public record GuestSchoolDistrictDto(string Id, string Name, int Count);

public record GuestSchoolListPageDto<T>(
    IReadOnlyList<T> Data,
    int Total,
    int Offset,
    int Limit
);

public record GuestSchoolRegistrationRowDto(
    string SchoolCode,
    string? SmisCode,
    string NameTh,
    string? Principal,
    int? EstablishedYearTh
);

public record GuestSchoolAddressRowDto(
    string SchoolCode,
    string NameTh,
    string? VillageName,
    string? SubDistrict,
    string? District,
    string? Phone
);

// ── Plan #107 — classification/map row shapes + DTOs ─────────────────────────

internal sealed class LevelAggRow
{
    public int ToM6 { get; set; }
    public int ToM3 { get; set; }
    public int ToP6 { get; set; }
    public int PreOnly { get; set; }
    public int Total { get; set; }
}

internal sealed class DecadeRow
{
    public decimal DecadeBe { get; set; }
    public int Count { get; set; }
}

internal sealed class InfraAggRow
{
    public int Electricity { get; set; }
    public int Internet { get; set; }
    public int Water { get; set; }
    public int Total { get; set; }
}

internal sealed class NameCountRow
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

internal sealed class DirectorAggRow
{
    public int WithDirector { get; set; }
    public int SchoolsWithStaff { get; set; }
    public int TotalStaff { get; set; }
}

public record GuestLevelBreakdownDto(int Total, int ToP6, int ToM3, int ToM6, int PreschoolOnly);
public record GuestDecadeDto(int DecadeBe, int Count);
public record GuestNameCountDto(string Name, int Count);
public record GuestInfraDto(int Total, int Electricity, int Internet, int Water, List<GuestNameCountDto> InternetTypes);
public record GuestDirectorCoverageDto(int WithDirector, int WithoutDirector, int TotalStaff);
public record GuestSchoolClassificationDto(
    GuestLevelBreakdownDto Levels,
    List<GuestDecadeDto> EstablishedDecades,
    GuestInfraDto? Infrastructure,
    GuestDirectorCoverageDto Directors);

public class GuestSchoolMapDto
{
    public string SmisCode { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? Lat { get; set; }
    public decimal? Lng { get; set; }
    public string LevelType { get; set; } = "p6";
    public string? District { get; set; }
}
