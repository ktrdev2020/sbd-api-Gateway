using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

[ApiController]
[Route("api/v1/area/{areaId:int}/personnel")]
[Authorize]
public class AreaPersonnelController : ControllerBase
{
    private readonly SbdDbContext _context;

    public AreaPersonnelController(SbdDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// List personnel with pagination, filtering by area, school, type, search.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> GetAll(
        int areaId,
        [FromQuery] string? schoolCode,
        [FromQuery] string? type,
        [FromQuery] string? scope,
        [FromQuery] string? search,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 30)
    {
        var query = _context.Personnel
            .Include(p => p.TitlePrefix)
            .Include(p => p.PersonnelTypeNav)
            .Include(p => p.SubjectAreaNav)
            .Include(p => p.SchoolAssignments)
                .ThenInclude(a => a.School)
            .AsQueryable();

        // Filter by area
        query = query.Where(p =>
            p.SchoolAssignments.Any(a => a.School.AreaId == areaId && a.IsPrimary));

        // Scope: school = has school assignment, area = area officers (no school)
        if (scope == "school")
        {
            query = query.Where(p =>
                p.SchoolAssignments.Any(a => a.IsPrimary && a.SchoolCode != null && a.SchoolCode != ""));
        }
        else if (scope == "area")
        {
            query = query.Where(p => p.PersonnelTypeNav.Code == "Staff");
        }

        // Filter by specific school
        if (!string.IsNullOrWhiteSpace(schoolCode))
        {
            query = query.Where(p =>
                p.SchoolAssignments.Any(a => a.SchoolCode == schoolCode && a.IsPrimary));
        }

        // Filter by personnel type (by code)
        if (!string.IsNullOrEmpty(type))
        {
            query = query.Where(p => p.PersonnelTypeNav.Code == type);
        }

        // Search by name
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(p =>
                p.FirstName.Contains(search) ||
                p.LastName.Contains(search) ||
                p.PersonnelCode.Contains(search));
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .Skip(offset)
            .Take(limit)
            .Select(p => new PersonnelListItemDto
            {
                Id = p.Id,
                PersonnelCode = p.PersonnelCode,
                TitlePrefix = p.TitlePrefix != null ? p.TitlePrefix.NameTh : null,
                FirstName = p.FirstName,
                LastName = p.LastName,
                PersonnelTypeCode = p.PersonnelTypeNav.Code,
                PersonnelTypeNameTh = p.PersonnelTypeNav.NameTh,
                Gender = p.Gender,
                Phone = p.Phone,
                Email = p.Email,
                Photo = p.Photo,
                Position = p.SchoolAssignments
                    .Where(a => a.IsPrimary)
                    .Select(a => a.Position)
                    .FirstOrDefault(),
                AcademicRank = p.SchoolAssignments
                    .Where(a => a.IsPrimary)
                    .Select(a => a.AcademicRank)
                    .FirstOrDefault(),
                SchoolCode = p.SchoolAssignments
                    .Where(a => a.IsPrimary)
                    .Select(a => a.SchoolCode)
                    .FirstOrDefault(),
                SchoolName = p.SchoolAssignments
                    .Where(a => a.IsPrimary)
                    .Select(a => a.School.NameTh)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return Ok(new { data = items, total, offset, limit });
    }

    /// <summary>Get personnel detail by ID.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult> GetById(int id)
    {
        var p = await _context.Personnel
            .Include(x => x.TitlePrefix)
            .Include(x => x.PersonnelTypeNav)
            .Include(x => x.SubjectAreaNav)
            .Include(x => x.SpecialtyNav)
            .Include(x => x.SchoolAssignments).ThenInclude(a => a.School)
            .Include(x => x.Educations).ThenInclude(e => e.EducationLevel)
            .Include(x => x.Certifications)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p == null) return NotFound();

        return Ok(new
        {
            p.Id, p.PersonnelCode,
            TitlePrefix = p.TitlePrefix?.NameTh,
            p.FirstName, p.LastName,
            FullName = $"{p.TitlePrefix?.NameTh ?? ""}{p.FirstName} {p.LastName}".Trim(),
            PersonnelTypeCode = p.PersonnelTypeNav.Code,
            PersonnelTypeNameTh = p.PersonnelTypeNav.NameTh,
            p.Gender, p.BirthDate, p.IdCard,
            SubjectAreaId = p.SubjectAreaId,
            SubjectAreaNameTh = p.SubjectAreaNav != null ? p.SubjectAreaNav.NameTh : null,
            SpecialtyId = p.SpecialtyId,
            SpecialtyNameTh = p.SpecialtyNav != null ? p.SpecialtyNav.NameTh : null,
            p.Phone, p.Email, p.LineId, p.Photo,
            Assignments = p.SchoolAssignments.Select(a => new
            {
                a.Id, a.SchoolCode, SchoolName = a.School.NameTh,
                a.Position, a.AcademicRank, a.SalaryLevel,
                a.IsPrimary, a.StartDate, a.EndDate
            }),
            Educations = p.Educations.Select(e => new
            {
                e.Id,
                EducationLevel  = e.EducationLevel != null ? e.EducationLevel.NameTh : null,
                e.QualificationName,
                e.Major,
                e.Institution,
                e.GraduatedYear,
            }),
            Certifications = p.Certifications.Select(c => new
            {
                c.Id, c.Name, c.IssuedBy, c.IssuedDate, c.ExpiryDate
            })
        });
    }

    /// <summary>Create a new personnel.</summary>
    [HttpPost]
    public async Task<ActionResult> Create([FromBody] CreatePersonnelRequest request)
    {
        var codeExists = await _context.Personnel.AnyAsync(p => p.PersonnelCode == request.PersonnelCode);
        if (codeExists) return Conflict(new { message = $"รหัสบุคลากร '{request.PersonnelCode}' ซ้ำ" });

        var person = new Personnel
        {
            PersonnelCode = request.PersonnelCode,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PersonnelTypeId = request.PersonnelTypeId,
            Gender = request.Gender,
            TitlePrefixId = request.TitlePrefixId,
            IdCard = request.IdCard,
            BirthDate = request.BirthDate,
            Phone = request.Phone,
            Email = request.Email,
            LineId = request.LineId,
        };
        _context.Personnel.Add(person);
        await _context.SaveChangesAsync();

        // Create school assignment if schoolCode provided
        if (!string.IsNullOrWhiteSpace(request.SchoolCode))
        {
            _context.PersonnelSchoolAssignments.Add(new PersonnelSchoolAssignment
            {
                PersonnelId = person.Id,
                SchoolCode = request.SchoolCode,
                Position = request.Position,
                AcademicRank = request.AcademicRank,
                IsPrimary = true,
                StartDate = DateOnly.FromDateTime(DateTime.Today)
            });
            await _context.SaveChangesAsync();
        }

        return CreatedAtAction(nameof(GetById), new { id = person.Id },
            new { person.Id, person.PersonnelCode, person.FirstName, person.LastName });
    }

    /// <summary>Update personnel info.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, [FromBody] UpdatePersonnelRequest request)
    {
        var person = await _context.Personnel.FindAsync(id);
        if (person == null) return NotFound();

        if (request.FirstName != null) person.FirstName = request.FirstName;
        if (request.LastName != null) person.LastName = request.LastName;
        if (request.TitlePrefixId.HasValue) person.TitlePrefixId = request.TitlePrefixId;
        if (request.Phone != null) person.Phone = request.Phone;
        if (request.Email != null) person.Email = request.Email;
        if (request.LineId != null) person.LineId = request.LineId;
        if (request.PersonnelTypeId.HasValue) person.PersonnelTypeId = request.PersonnelTypeId.Value;
        if (request.SubjectAreaId.HasValue) person.SubjectAreaId = request.SubjectAreaId;
        if (request.SpecialtyId.HasValue) person.SpecialtyId = request.SpecialtyId;

        await _context.SaveChangesAsync();
        return Ok(new { person.Id, person.FirstName, person.LastName });
    }

    /// <summary>Delete personnel.</summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var person = await _context.Personnel.FindAsync(id);
        if (person == null) return NotFound();

        _context.Personnel.Remove(person);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Get area-level summary stats.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult> GetSummary(int areaId, [FromQuery] string? schoolCode)
    {
        var query = _context.PersonnelSchoolAssignments
            .Where(a => a.IsPrimary && (a.EndDate == null || a.EndDate >= DateOnly.FromDateTime(DateTime.Today)))
            .Where(a => a.School.AreaId == areaId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(schoolCode))
            query = query.Where(a => a.SchoolCode == schoolCode);

        var assignments = await query
            .Include(a => a.Personnel).ThenInclude(p => p.PersonnelTypeNav)
            .ToListAsync();

        return Ok(new
        {
            Total = assignments.Count,
            Teachers = assignments.Count(a => a.Personnel.PersonnelTypeNav?.Code == "Teacher"),
            Directors = assignments.Count(a => a.Personnel.PersonnelTypeNav?.Code == "Director"),
            Staff = assignments.Count(a => a.Personnel.PersonnelTypeNav?.Code == "Staff"),
            ByPosition = assignments
                .Where(a => a.Position != null)
                .GroupBy(a => a.Position!)
                .Select(g => new { Position = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(10)
        });
    }
}

// ── DTOs ──

public class PersonnelListItemDto
{
    public int Id { get; set; }
    public string PersonnelCode { get; set; } = "";
    public string? TitlePrefix { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public int PersonnelTypeId { get; set; }
    public string PersonnelTypeCode { get; set; } = "";
    public string PersonnelTypeNameTh { get; set; } = "";
    public char Gender { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Photo { get; set; }
    public string? Position { get; set; }
    public string? AcademicRank { get; set; }
    public string? SchoolCode { get; set; }
    public string? SchoolName { get; set; }
}

public record CreatePersonnelRequest(
    string PersonnelCode, string FirstName, string LastName,
    int PersonnelTypeId, char Gender,
    int? TitlePrefixId = null, string? IdCard = null,
    DateOnly? BirthDate = null, string? Phone = null,
    string? Email = null, string? LineId = null,
    string? SchoolCode = null, string? Position = null,
    string? AcademicRank = null,
    int? SubjectAreaId = null, int? SpecialtyId = null
);

public record UpdatePersonnelRequest(
    string? FirstName = null, string? LastName = null,
    int? TitlePrefixId = null, string? Phone = null,
    string? Email = null, string? LineId = null,
    int? PersonnelTypeId = null,
    int? SubjectAreaId = null, int? SpecialtyId = null
);
// Gateway v1775015381
