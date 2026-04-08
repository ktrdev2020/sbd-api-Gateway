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
        [FromQuery] int? areaId,
        [FromQuery] string? district,
        [FromQuery] string? subDistrict,
        [FromQuery] bool? isActive,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50)
    {
        var query = _context.Schools
            .Where(s => s.DeletedAt == null)
            .Include(s => s.Area)
            .Include(s => s.AreaType)
            .Include(s => s.Address)
                .ThenInclude(a => a!.SubDistrict)
                    .ThenInclude(sd => sd.District)
            .AsQueryable();

        if (areaId.HasValue)
            query = query.Where(s => s.AreaId == areaId.Value);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(s => s.NameTh.Contains(search) || s.SchoolCode.Contains(search));

        if (isActive.HasValue)
            query = query.Where(s => s.IsActive == isActive.Value);

        if (!string.IsNullOrEmpty(district))
            query = query.Where(s => s.Address != null && s.Address.SubDistrict.District.NameTh == district);

        if (!string.IsNullOrEmpty(subDistrict))
            query = query.Where(s => s.Address != null && s.Address.SubDistrict.NameTh == subDistrict);

        var total = await query.CountAsync();

        var schools = await query
            .OrderBy(s => s.NameTh)
            .Skip(offset)
            .Take(limit)
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
                s.TeacherCount,
                s.LogoThumbnailUrl
            ))
            .ToListAsync();

        return Ok(new { data = schools, total, offset, limit });
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

    /// <summary>
    /// Move a school to the recycle bin (soft delete). The school is hidden
    /// from normal queries but can be restored. Permanent removal requires a
    /// separate explicit call to <see cref="PermanentDelete"/>.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var school = await _context.Schools.AsTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (school == null)
            return NotFound(new { message = "School not found" });

        if (school.DeletedAt != null)
            return Ok(new { message = "Already in recycle bin", deletedAt = school.DeletedAt });

        school.DeletedAt = DateTimeOffset.UtcNow;
        school.DeletedBy = User?.Identity?.Name ?? "system";
        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKey);

        return Ok(new
        {
            message = "Moved to recycle bin",
            deletedAt = school.DeletedAt,
            deletedBy = school.DeletedBy
        });
    }

    // ─── Recycle Bin ──────────────────────────────────────────

    /// <summary>List schools currently in the recycle bin (soft-deleted).</summary>
    [HttpGet("recycle-bin")]
    public async Task<ActionResult<IEnumerable<RecycledSchoolDto>>> GetRecycleBin(
        [FromQuery] int? areaId)
    {
        var query = _context.Schools
            .Where(s => s.DeletedAt != null)
            .Include(s => s.Area)
            .Include(s => s.Address)
                .ThenInclude(a => a!.SubDistrict)
                    .ThenInclude(sd => sd.District)
            .AsQueryable();

        if (areaId.HasValue)
            query = query.Where(s => s.AreaId == areaId.Value);

        var items = await query
            .OrderByDescending(s => s.DeletedAt)
            .Select(s => new RecycledSchoolDto(
                s.Id,
                s.SchoolCode,
                s.NameTh,
                s.Principal,
                s.Address != null && s.Address.SubDistrict != null
                    ? s.Address.SubDistrict.NameTh
                    : null,
                s.Address != null && s.Address.SubDistrict != null && s.Address.SubDistrict.District != null
                    ? s.Address.SubDistrict.District.NameTh
                    : null,
                s.SchoolLevel,
                s.StudentCount,
                s.TeacherCount,
                s.DeletedAt!.Value,
                s.DeletedBy
            ))
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>Restore a school from the recycle bin back to live state.</summary>
    [HttpPost("{id:int}/restore")]
    public async Task<ActionResult<SchoolDto>> RestoreFromBin(int id)
    {
        var school = await _context.Schools.AsTracking()
            .Include(s => s.Area)
            .Include(s => s.AreaType)
            .Include(s => s.Address)
                .ThenInclude(a => a!.SubDistrict)
                    .ThenInclude(sd => sd.District)
                        .ThenInclude(d => d.Province)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (school == null)
            return NotFound(new { message = "School not found" });

        if (school.DeletedAt == null)
            return BadRequest(new { message = "School is not in the recycle bin" });

        school.DeletedAt = null;
        school.DeletedBy = null;
        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKey);

        return Ok(MapToDto(school));
    }

    /// <summary>
    /// Permanently delete a school. This is destructive and cannot be undone.
    /// The school must already be in the recycle bin.
    /// </summary>
    [HttpDelete("{id:int}/permanent")]
    public async Task<ActionResult> PermanentDelete(int id)
    {
        var school = await _context.Schools.AsTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (school == null)
            return NotFound(new { message = "School not found" });

        if (school.DeletedAt == null)
            return BadRequest(new
            {
                message = "School must be in the recycle bin before permanent deletion. Soft-delete first."
            });

        _context.Schools.Remove(school);
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
            .Where(s => s.IsActive && s.DeletedAt == null)
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
            .Where(s => s.IsActive && s.DeletedAt == null)
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
        var activeSchools = _context.Schools.Where(s => s.IsActive && s.DeletedAt == null);

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
            LogoThumbnailUrl = s.LogoThumbnailUrl,
            LogoVersion = s.LogoVersion,
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

    // ─── Area-scoped endpoints ─────────────────────────────────

    /// <summary>Summary statistics for all schools in an area.</summary>
    [HttpGet("area/{areaId:int}/summary")]
    public async Task<ActionResult<AreaSchoolsSummaryDto>> GetAreaSummary(int areaId)
    {
        var schools = await _context.Schools
            .Where(s => s.AreaId == areaId && s.DeletedAt == null)
            .ToListAsync();

        var districts = await _context.Schools
            .Where(s => s.AreaId == areaId && s.DeletedAt == null && s.Address != null)
            .Include(s => s.Address!.SubDistrict.District)
            .Select(s => s.Address!.SubDistrict.District.NameTh)
            .Distinct()
            .ToListAsync();

        return Ok(new AreaSchoolsSummaryDto(
            TotalSchools: schools.Count,
            ActiveSchools: schools.Count(s => s.IsActive),
            TotalTeachers: schools.Sum(s => s.TeacherCount ?? 0),
            TotalStudents: schools.Sum(s => s.StudentCount ?? 0),
            Districts: districts
        ));
    }

    /// <summary>Assign a principal to a school (from personnel with Director position).</summary>
    [HttpPut("{id:int}/principal")]
    public async Task<ActionResult> AssignPrincipal(int id, [FromBody] AssignPrincipalRequest request)
    {
        var school = await _context.Schools.FindAsync(id);
        if (school == null) return NotFound(new { message = "School not found" });

        if (request.PersonnelId.HasValue)
        {
            var personnel = await _context.Personnel.FindAsync(request.PersonnelId.Value);
            if (personnel == null) return NotFound(new { message = "Personnel not found" });

            school.Principal = $"{personnel.FirstName} {personnel.LastName}";

            // Ensure personnel has school assignment
            var assignment = await _context.PersonnelSchoolAssignments
                .FirstOrDefaultAsync(a => a.PersonnelId == request.PersonnelId.Value && a.SchoolId == id);

            if (assignment == null)
            {
                _context.PersonnelSchoolAssignments.Add(new PersonnelSchoolAssignment
                {
                    PersonnelId = request.PersonnelId.Value,
                    SchoolId = id,
                    Position = request.Position ?? "ผู้อำนวยการโรงเรียน",
                    IsPrimary = true,
                    StartDate = DateOnly.FromDateTime(DateTime.Today)
                });
            }
            else
            {
                assignment.Position = request.Position ?? "ผู้อำนวยการโรงเรียน";
                assignment.IsPrimary = true;
            }

            // Auto-assign SchoolAdmin role if personnel has a User account
            if (personnel.UserId.HasValue)
            {
                var schoolAdminRole = await _context.Roles.FirstOrDefaultAsync(r => r.Code == "school_admin");
                if (schoolAdminRole != null)
                {
                    var existingRole = await _context.UserRoles
                        .FirstOrDefaultAsync(ur => ur.UserId == personnel.UserId.Value
                            && ur.RoleId == schoolAdminRole.Id
                            && ur.ScopeType == "School" && ur.ScopeId == id);
                    if (existingRole == null)
                    {
                        _context.UserRoles.Add(new UserRole
                        {
                            UserId = personnel.UserId.Value,
                            RoleId = schoolAdminRole.Id,
                            ScopeType = "School",
                            ScopeId = id,
                            AssignedAt = DateTimeOffset.UtcNow
                        });
                    }
                }
            }
        }
        else
        {
            school.Principal = null;
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "Principal updated", principal = school.Principal });
    }

    /// <summary>Get personnel eligible to be principal (Director/Acting Director position types).</summary>
    [HttpGet("area/{areaId:int}/principals")]
    public async Task<ActionResult> GetEligiblePrincipals(int areaId)
    {
        var eligible = await _context.PersonnelSchoolAssignments
            .Where(a => a.School.AreaId == areaId && (a.EndDate == null || a.EndDate >= DateOnly.FromDateTime(DateTime.Today)))
            .Where(a => a.Position != null && (
                a.Position.Contains("ผู้บริหารสถานศึกษา") ||
                a.Position.Contains("ผู้อำนวยการ") ||
                a.Position.Contains("รักษาการ")))
            .Include(a => a.Personnel)
            .Select(a => new
            {
                a.Personnel.Id,
                a.Personnel.FirstName,
                a.Personnel.LastName,
                FullName = a.Personnel.FirstName + " " + a.Personnel.LastName,
                a.Position,
                a.Personnel.PersonnelType,
                CurrentSchoolId = a.SchoolId,
                CurrentSchoolName = a.School.NameTh
            })
            .ToListAsync();

        // Also include personnel with Director type regardless of position text
        var directors = await _context.Personnel
            .Where(p => p.PersonnelType == "Director")
            .Where(p => p.SchoolAssignments.Any(a => a.School.AreaId == areaId && (a.EndDate == null || a.EndDate >= DateOnly.FromDateTime(DateTime.Today))))
            .Select(p => new
            {
                p.Id,
                p.FirstName,
                p.LastName,
                FullName = p.FirstName + " " + p.LastName,
                Position = p.SchoolAssignments.First().Position ?? "ผู้บริหารสถานศึกษา",
                p.PersonnelType,
                CurrentSchoolId = p.SchoolAssignments.First().SchoolId,
                CurrentSchoolName = p.SchoolAssignments.First().School.NameTh
            })
            .ToListAsync();

        var merged = eligible.Concat(directors)
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .OrderBy(p => p.FullName)
            .ToList();

        return Ok(merged);
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
    int? TeacherCount,
    string? LogoThumbnailUrl
);

public record SchoolSummaryDto(
    int TotalSchools,
    int TotalTeachers,
    int TotalStudents,
    List<string> Districts
);

public record AreaSchoolsSummaryDto(
    int TotalSchools,
    int ActiveSchools,
    int TotalTeachers,
    int TotalStudents,
    List<string> Districts
);

public record AssignPrincipalRequest(
    int? PersonnelId,
    string? Position
);

public record RecycledSchoolDto(
    int Id,
    string SchoolCode,
    string NameTh,
    string? Principal,
    string? SubDistrictName,
    string? DistrictName,
    string? SchoolLevel,
    int? StudentCount,
    int? TeacherCount,
    DateTimeOffset DeletedAt,
    string? DeletedBy
);
