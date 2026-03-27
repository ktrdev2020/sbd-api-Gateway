using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Application.DTOs;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class SchoolController : ControllerBase
{
    private readonly SbdDbContext _context;
    private readonly ICacheService _cache;
    private const string CacheKey = "refdata:schools";

    public SchoolController(SbdDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    // ─── Admin CRUD ───────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SchoolListItemDto>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? district,
        [FromQuery] string? subDistrict,
        [FromQuery] bool? isActive)
    {
        var query = _context.Schools
            .Include(s => s.Area)
            .Include(s => s.AreaType)
            .Include(s => s.Address)
                .ThenInclude(a => a!.SubDistrict)
                    .ThenInclude(sd => sd.District)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
            query = query.Where(s => s.NameTh.Contains(search) || s.SchoolCode.Contains(search));

        if (isActive.HasValue)
            query = query.Where(s => s.IsActive == isActive.Value);

        if (!string.IsNullOrEmpty(district))
            query = query.Where(s => s.Address != null && s.Address.SubDistrict.District.NameTh == district);

        if (!string.IsNullOrEmpty(subDistrict))
            query = query.Where(s => s.Address != null && s.Address.SubDistrict.NameTh == subDistrict);

        var schools = await query
            .OrderBy(s => s.NameTh)
            .Select(s => new SchoolListItemDto(
                s.Id,
                s.SchoolCode,
                s.NameTh,
                s.Principal,
                s.Phone,
                s.Address != null && s.Address.SubDistrict != null ? s.Address.SubDistrict.NameTh : null,
                s.Address != null && s.Address.SubDistrict != null && s.Address.SubDistrict.District != null ? s.Address.SubDistrict.District.NameTh : null,
                s.SchoolLevel,
                s.SchoolType,
                s.IsActive,
                s.StudentCount,
                s.TeacherCount
            ))
            .ToListAsync();

        return Ok(schools);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SchoolDto>> GetById(int id)
    {
        var school = await _context.Schools
            .Include(s => s.Area)
            .Include(s => s.AreaType)
            .Include(s => s.Address)
                .ThenInclude(a => a!.SubDistrict)
                    .ThenInclude(sd => sd.District)
                        .ThenInclude(d => d.Province)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (school == null)
            return NotFound(new { message = "School not found" });

        return Ok(MapToDto(school));
    }

    [HttpPost]
    public async Task<ActionResult<SchoolDto>> Create([FromBody] SchoolRequest request)
    {
        if (await _context.Schools.AnyAsync(s => s.SchoolCode == request.SchoolCode))
            return Conflict(new { message = $"School code '{request.SchoolCode}' already exists" });

        var school = new School
        {
            SchoolCode = request.SchoolCode,
            NameTh = request.NameTh,
            NameEn = request.NameEn,
            AreaId = request.AreaId,
            AreaTypeId = request.AreaTypeId,
            SchoolCluster = request.SchoolCluster,
            Phone = request.Phone,
            Phone2 = request.Phone2,
            Email = request.Email,
            Website = request.Website,
            TaxId = request.TaxId,
            SchoolType = request.SchoolType,
            SchoolLevel = request.SchoolLevel,
            Principal = request.Principal,
            EstablishedDate = request.EstablishedDate,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            IsActive = true,
            StudentCount = request.StudentCount,
            TeacherCount = request.TeacherCount,
            SmisCode = request.SmisCode,
            PerCode = request.PerCode,
        };

        _context.Schools.Add(school);
        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKey);

        return CreatedAtAction(nameof(GetById), new { id = school.Id }, MapToDto(school));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<SchoolDto>> Update(int id, [FromBody] SchoolRequest request)
    {
        var school = await _context.Schools.AsTracking()
            .Include(s => s.Area)
            .Include(s => s.AreaType)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (school == null)
            return NotFound(new { message = "School not found" });

        if (school.SchoolCode != request.SchoolCode &&
            await _context.Schools.AnyAsync(s => s.SchoolCode == request.SchoolCode))
            return Conflict(new { message = $"School code '{request.SchoolCode}' already exists" });

        school.SchoolCode = request.SchoolCode;
        school.NameTh = request.NameTh;
        school.NameEn = request.NameEn;
        school.AreaId = request.AreaId;
        school.AreaTypeId = request.AreaTypeId;
        school.SchoolCluster = request.SchoolCluster;
        school.Phone = request.Phone;
        school.Phone2 = request.Phone2;
        school.Email = request.Email;
        school.Website = request.Website;
        school.TaxId = request.TaxId;
        school.SchoolType = request.SchoolType;
        school.SchoolLevel = request.SchoolLevel;
        school.Principal = request.Principal;
        school.EstablishedDate = request.EstablishedDate;
        school.Latitude = request.Latitude;
        school.Longitude = request.Longitude;
        school.StudentCount = request.StudentCount;
        school.TeacherCount = request.TeacherCount;
        school.SmisCode = request.SmisCode;
        school.PerCode = request.PerCode;

        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKey);

        return Ok(MapToDto(school));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var school = await _context.Schools.AsTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (school == null)
            return NotFound(new { message = "School not found" });

        // Soft-delete
        school.IsActive = false;
        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKey);

        return NoContent();
    }

    // ─── School-Level Profile Update ──────────────────────────

    [HttpPut("{id:int}/profile")]
    public async Task<ActionResult<SchoolDto>> UpdateProfile(int id, [FromBody] SchoolProfileUpdateRequest request)
    {
        var school = await _context.Schools.AsTracking()
            .Include(s => s.Area)
            .Include(s => s.AreaType)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (school == null)
            return NotFound(new { message = "School not found" });

        school.Phone = request.Phone;
        school.Phone2 = request.Phone2;
        school.Email = request.Email;
        school.Website = request.Website;
        school.Principal = request.Principal;
        school.LogoUrl = request.LogoUrl;
        school.StudentCount = request.StudentCount;
        school.TeacherCount = request.TeacherCount;

        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKey);

        return Ok(MapToDto(school));
    }

    // ─── Personnel ────────────────────────────────────────────

    [HttpGet("{id:int}/personnel")]
    public async Task<ActionResult<IEnumerable<Personnel>>> GetSchoolPersonnel(int id)
    {
        var personnelAssignments = await _context.Set<PersonnelSchoolAssignment>()
            .Where(psa => psa.SchoolId == id)
            .Include(psa => psa.Personnel)
                .ThenInclude(p => p.TitlePrefix)
            .Select(psa => psa.Personnel)
            .ToListAsync();

        return Ok(personnelAssignments);
    }

    // ─── Public Endpoints (no auth) ───────────────────────────

    [HttpGet("public")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<SchoolListItemDto>>> GetPublicList(
        [FromQuery] string? search,
        [FromQuery] string? district)
    {
        var query = _context.Schools
            .Where(s => s.IsActive)
            .Include(s => s.Address)
                .ThenInclude(a => a!.SubDistrict)
                    .ThenInclude(sd => sd.District)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
            query = query.Where(s => s.NameTh.Contains(search) || s.SchoolCode.Contains(search));

        if (!string.IsNullOrEmpty(district))
            query = query.Where(s => s.Address != null && s.Address.SubDistrict.District.NameTh == district);

        var schools = await query
            .OrderBy(s => s.NameTh)
            .Select(s => new SchoolListItemDto(
                s.Id,
                s.SchoolCode,
                s.NameTh,
                s.Principal,
                s.Phone,
                s.Address != null && s.Address.SubDistrict != null ? s.Address.SubDistrict.NameTh : null,
                s.Address != null && s.Address.SubDistrict != null && s.Address.SubDistrict.District != null ? s.Address.SubDistrict.District.NameTh : null,
                s.SchoolLevel,
                s.SchoolType,
                s.IsActive,
                s.StudentCount,
                s.TeacherCount
            ))
            .ToListAsync();

        return Ok(schools);
    }

    [HttpGet("public/{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<SchoolDto>> GetPublicById(int id)
    {
        var school = await _context.Schools
            .Where(s => s.IsActive)
            .Include(s => s.Area)
            .Include(s => s.AreaType)
            .Include(s => s.Address)
                .ThenInclude(a => a!.SubDistrict)
                    .ThenInclude(sd => sd.District)
                        .ThenInclude(d => d.Province)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (school == null)
            return NotFound(new { message = "School not found" });

        return Ok(MapToDto(school));
    }

    [HttpGet("public/summary")]
    [AllowAnonymous]
    public async Task<ActionResult<SchoolSummaryDto>> GetPublicSummary()
    {
        var activeSchools = _context.Schools.Where(s => s.IsActive);

        var summary = new SchoolSummaryDto(
            TotalSchools: await activeSchools.CountAsync(),
            TotalTeachers: await activeSchools.SumAsync(s => s.TeacherCount ?? 0),
            TotalStudents: await activeSchools.SumAsync(s => s.StudentCount ?? 0),
            Districts: await activeSchools
                .Where(s => s.Address != null && s.Address.SubDistrict != null)
                .Select(s => s.Address!.SubDistrict.District.NameTh)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync()
        );

        return Ok(summary);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static SchoolDto MapToDto(School s)
    {
        return new SchoolDto
        {
            Id = s.Id,
            SchoolCode = s.SchoolCode,
            SmisCode = s.SmisCode,
            PerCode = s.PerCode,
            NameTh = s.NameTh,
            NameEn = s.NameEn,
            AreaId = s.AreaId,
            AreaName = s.Area?.NameTh,
            AreaTypeName = s.AreaType?.NameTh,
            AreaTypeId = s.AreaTypeId,
            SchoolCluster = s.SchoolCluster,
            Phone = s.Phone,
            Phone2 = s.Phone2,
            Email = s.Email,
            Website = s.Website,
            TaxId = s.TaxId,
            SchoolType = s.SchoolType,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            SchoolSizeStd4 = s.SchoolSizeStd4,
            SchoolSizeStd7 = s.SchoolSizeStd7,
            SchoolSizeHr = s.SchoolSizeHr,
            SchoolLevel = s.SchoolLevel,
            Principal = s.Principal,
            EstablishedDate = s.EstablishedDate,
            IsActive = s.IsActive,
            StudentCount = s.StudentCount,
            TeacherCount = s.TeacherCount,
            LogoUrl = s.LogoUrl,
            Address = s.Address != null ? new AddressDto
            {
                HouseNumber = s.Address.HouseNumber,
                VillageNo = s.Address.VillageNo,
                VillageName = s.Address.VillageName,
                Road = s.Address.Road,
                Soi = s.Address.Soi,
                SubDistrictName = s.Address.SubDistrict?.NameTh,
                DistrictName = s.Address.SubDistrict?.District?.NameTh,
                ProvinceName = s.Address.SubDistrict?.District?.Province?.NameTh,
                PostalCode = s.Address.SubDistrict?.District?.Province != null ? null : null, // postal code from SubDistrict if available
            } : null,
        };
    }
}

// ─── Request / Response Records ──────────────────────────────

public record SchoolRequest(
    string SchoolCode,
    string NameTh,
    string? NameEn,
    int AreaId,
    int AreaTypeId,
    string? SchoolCluster,
    string? Phone,
    string? Phone2,
    string? Email,
    string? Website,
    string? TaxId,
    string? SchoolType,
    string? SchoolLevel,
    string? Principal,
    DateOnly? EstablishedDate,
    decimal? Latitude,
    decimal? Longitude,
    int? StudentCount,
    int? TeacherCount,
    string? SmisCode,
    string? PerCode
);

public record SchoolProfileUpdateRequest(
    string? Phone,
    string? Phone2,
    string? Email,
    string? Website,
    string? Principal,
    string? LogoUrl,
    int? StudentCount,
    int? TeacherCount
);

public record SchoolListItemDto(
    int Id,
    string SchoolCode,
    string NameTh,
    string? Principal,
    string? Phone,
    string? SubDistrictName,
    string? DistrictName,
    string? SchoolLevel,
    string? SchoolType,
    bool IsActive,
    int? StudentCount,
    int? TeacherCount
);

public record SchoolSummaryDto(
    int TotalSchools,
    int TotalTeachers,
    int TotalStudents,
    List<string> Districts
);
