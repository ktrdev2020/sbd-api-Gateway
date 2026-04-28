using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Public guest endpoints for the /personnel-info portal page.
/// All actions are anonymous, response-cached for 1 hour.
/// Scoped to สพป.ศก.3 (AreaId = 33030000).
/// </summary>
[ApiController]
[Route("api/v1/guest/personnel-info")]
[AllowAnonymous]
public class GuestPersonnelInfoController : ControllerBase
{
    private const int AreaId = 33030000;
    private const int CacheSeconds = 3600;

    private readonly SbdDbContext _context;

    public GuestPersonnelInfoController(SbdDbContext context)
    {
        _context = context;
    }

    /// <summary>4 stat cards + district breakdown for the dashboard.</summary>
    [HttpGet("summary")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestPersonnelSummaryDto>> GetSummary()
    {
        var personnel = _context.Personnel
            .Where(p => p.TrashedAt == null)
            .Where(p => p.SchoolAssignments.Any(a => a.IsPrimary && a.School.AreaId == AreaId));

        var total = await personnel.CountAsync();

        var typesRaw = await personnel
            .Where(p => p.PersonnelTypeNav != null)
            .GroupBy(p => new { p.PersonnelTypeNav!.Code, p.PersonnelTypeNav.NameTh, p.PersonnelTypeNav.SortOrder })
            .Select(g => new { g.Key.Code, g.Key.NameTh, g.Key.SortOrder, Count = g.Count() })
            .OrderBy(g => g.SortOrder)
            .ToListAsync();

        var types = typesRaw
            .Select(t => new GuestPersonnelTypeDto(
                Id: t.Code,
                Name: t.NameTh,
                Count: t.Count,
                Icon: IconForCode(t.Code)
            ))
            .ToList();

        var districtsRaw = await personnel
            .SelectMany(p => p.SchoolAssignments
                .Where(a => a.IsPrimary)
                .Select(a => new
                {
                    PersonnelId = p.Id,
                    DistrictName = a.School.Address != null && a.School.Address.SubDistrict != null
                        ? a.School.Address.SubDistrict.District.NameTh
                        : null,
                }))
            .Where(x => x.DistrictName != null)
            .Distinct()
            .GroupBy(x => x.DistrictName!)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync();

        var districts = districtsRaw
            .OrderByDescending(d => d.Count)
            .Select(d => new GuestPersonnelDistrictDto(
                Id: SlugFromDistrict(d.Name),
                Name: d.Name,
                Count: d.Count
            ))
            .ToList();

        return Ok(new GuestPersonnelSummaryDto(
            TotalPersonnel: total,
            Types: types,
            Districts: districts
        ));
    }

    /// <summary>Personnel breakdown by type × gender.</summary>
    [HttpGet("by-gender")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<IEnumerable<GuestGenderRowDto>>> GetByGender()
    {
        var rowsRaw = await _context.Personnel
            .Where(p => p.TrashedAt == null && p.PersonnelTypeNav != null)
            .Where(p => p.SchoolAssignments.Any(a => a.IsPrimary && a.School.AreaId == AreaId))
            .GroupBy(p => new { p.PersonnelTypeNav!.Code, p.PersonnelTypeNav.NameTh, p.PersonnelTypeNav.SortOrder })
            .Select(g => new
            {
                g.Key.Code,
                g.Key.NameTh,
                g.Key.SortOrder,
                Total = g.Count(),
                Male = g.Count(p => p.Gender == 'M'),
                Female = g.Count(p => p.Gender == 'F'),
            })
            .OrderBy(g => g.SortOrder)
            .ToListAsync();

        var rows = rowsRaw
            .Select(g => new GuestGenderRowDto(
                TypeCode: g.Code,
                TypeName: g.NameTh,
                Total: g.Total,
                Male: g.Male,
                Female: g.Female
            ))
            .ToList();

        return Ok(rows);
    }

    /// <summary>Personnel breakdown by position type, sorted by count descending.</summary>
    [HttpGet("by-position")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<IEnumerable<GuestPositionRowDto>>> GetByPosition()
    {
        var rowsRaw = await _context.Personnel
            .Where(p => p.TrashedAt == null && p.PositionType != null)
            .Where(p => p.SchoolAssignments.Any(a => a.IsPrimary && a.School.AreaId == AreaId))
            .GroupBy(p => new { p.PositionType!.Code, p.PositionType.NameTh })
            .Select(g => new { g.Key.Code, g.Key.NameTh, Count = g.Count() })
            .ToListAsync();

        var rows = rowsRaw
            .OrderByDescending(g => g.Count)
            .Select(g => new GuestPositionRowDto(g.Code, g.NameTh, g.Count))
            .ToList();

        return Ok(rows);
    }

    /// <summary>Retirement projection: age band 55-65 from BirthDate.</summary>
    [HttpGet("retirement")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestRetirementDto>> GetRetirement()
    {
        var today = DateTime.UtcNow.Date;

        var withBirthDate = _context.Personnel
            .Where(p => p.TrashedAt == null && p.BirthDate != null)
            .Where(p => p.SchoolAssignments.Any(a => a.IsPrimary && a.School.AreaId == AreaId));

        var totalCovered = await withBirthDate.CountAsync();

        var totalScope = await _context.Personnel
            .Where(p => p.TrashedAt == null)
            .Where(p => p.SchoolAssignments.Any(a => a.IsPrimary && a.School.AreaId == AreaId))
            .CountAsync();

        var birthDates = await withBirthDate
            .Select(p => p.BirthDate!.Value)
            .ToListAsync();

        var bands = birthDates
            .Select(bd =>
            {
                var age = today.Year - bd.Year;
                if (bd.Month > today.Month || (bd.Month == today.Month && bd.Day > today.Day))
                    age--;
                return age;
            })
            .Where(age => age >= 55 && age <= 65)
            .GroupBy(age => age)
            .Select(g => new GuestAgeBandDto(g.Key, g.Count()))
            .OrderBy(b => b.Age)
            .ToList();

        return Ok(new GuestRetirementDto(
            TotalScope: totalScope,
            TotalWithBirthDate: totalCovered,
            CoveragePercent: totalScope > 0 ? Math.Round((double)totalCovered / totalScope * 100, 1) : 0,
            Bands: bands
        ));
    }

    /// <summary>Paginated public list of teachers + directors.</summary>
    [HttpGet("teachers")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any, VaryByQueryKeys = new[] { "offset", "limit", "q" })]
    public async Task<ActionResult> GetTeachers(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? q = null)
    {
        if (limit > 100) limit = 100;
        if (limit < 1) limit = 50;
        if (offset < 0) offset = 0;

        var query = _context.Personnel
            .Where(p => p.TrashedAt == null && p.PersonnelTypeNav != null)
            .Where(p => p.PersonnelTypeNav!.Code == "Teacher" || p.PersonnelTypeNav.Code == "Director")
            .Where(p => p.SchoolAssignments.Any(a => a.IsPrimary && a.School.AreaId == AreaId));

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(p =>
                p.FirstName.Contains(q) ||
                p.LastName.Contains(q));
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .Skip(offset).Take(limit)
            .Select(p => new GuestTeacherListItemDto(
                p.Id,
                p.TitlePrefix != null ? p.TitlePrefix.NameTh : null,
                p.FirstName,
                p.LastName,
                p.PersonnelTypeNav!.Code,
                p.PersonnelTypeNav.NameTh,
                p.PositionType != null ? p.PositionType.NameTh : null,
                p.SchoolAssignments
                    .Where(a => a.IsPrimary)
                    .Select(a => a.School.NameTh)
                    .FirstOrDefault()
            ))
            .ToListAsync();

        return Ok(new { data = items, total, offset, limit });
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static string IconForCode(string code) => code switch
    {
        "Director"       => "fas fa-user-shield",
        "Teacher"        => "fas fa-chalkboard-teacher",
        "GovEmployee"    => "fas fa-id-badge",
        "PermanentStaff" => "fas fa-user-tie",
        "TempStaff"      => "fas fa-user-clock",
        "Staff"          => "fas fa-user-tie",
        _                => "fas fa-user",
    };

    private static string SlugFromDistrict(string name) => name switch
    {
        "ขุขันธ์"    => "khukhan",
        "ปรางค์กู่" => "prangku",
        "ภูสิงห์"    => "phusing",
        "ไพรบึง"    => "phraibung",
        _            => name.ToLowerInvariant(),
    };
}

// ── DTOs ────────────────────────────────────────────────────────────────

public record GuestPersonnelSummaryDto(
    int TotalPersonnel,
    IReadOnlyList<GuestPersonnelTypeDto> Types,
    IReadOnlyList<GuestPersonnelDistrictDto> Districts
);

public record GuestPersonnelTypeDto(string Id, string Name, int Count, string Icon);

public record GuestPersonnelDistrictDto(string Id, string Name, int Count);

public record GuestGenderRowDto(string TypeCode, string TypeName, int Total, int Male, int Female);

public record GuestPositionRowDto(string Code, string NameTh, int Count);

public record GuestAgeBandDto(int Age, int Count);

public record GuestRetirementDto(
    int TotalScope,
    int TotalWithBirthDate,
    double CoveragePercent,
    IReadOnlyList<GuestAgeBandDto> Bands
);

public record GuestTeacherListItemDto(
    int Id,
    string? TitlePrefix,
    string FirstName,
    string LastName,
    string TypeCode,
    string TypeName,
    string? PositionName,
    string? SchoolName
);
