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
