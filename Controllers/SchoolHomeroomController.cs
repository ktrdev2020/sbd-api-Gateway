using System.Security.Claims;
using Gateway.Data;
using Gateway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;
using Gateway.Services;

namespace Gateway.Controllers;

/// <summary>
/// Plan #27 Phase A.0 — Homeroom advisor assignment per classroom × academic year.
/// M:N mapping: 1 classroom can have many advisor teachers, 1 teacher can advise
/// many classrooms. Used by Teacher dashboard "ห้องที่ดูแล".
/// </summary>
[ApiController]
[Route("api/v1/schools/{schoolCode}/homeroom-assignments")]
[Authorize]
public class SchoolHomeroomController : ControllerBase
{
    private readonly GatewayDbContext _db;
    private readonly ICapabilityService _capabilities;

    public SchoolHomeroomController(SbdDbContext db, ICapabilityService capabilities)
    {
        _db = (GatewayDbContext)db;
        _capabilities = capabilities;
    }

    private int? CurrentUserId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value, out var id) ? id : null;

    private long CapVersion =>
        long.TryParse(User.FindFirstValue("cap_v"), out var v) ? v : 0L;

    /// <summary>
    /// Authorize mutation. Allowed: SuperAdmin · SchoolAdmin scoped to this school ·
    /// holder of `school:homeroom:assign` capability scoped to this school.
    /// </summary>
    private async Task<bool> CanAssignAsync(string schoolCode, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return false;

        if (User.IsInRole("super_admin") || User.IsInRole("SuperAdmin")) return true;

        var scopeId = int.TryParse(schoolCode, out var sid) ? sid : (int?)null;

        // SchoolAdmin scoped to this school?
        if (scopeId.HasValue)
        {
            var hasSchoolAdmin = await _db.UserRoles.AsNoTracking()
                .Include(ur => ur.Role)
                .AnyAsync(ur => ur.UserId == userId
                    && ur.ScopeType == "School" && ur.ScopeId == scopeId
                    && (ur.Role!.Code == "school_admin" || ur.Role!.Code == "SchoolAdmin"), ct);
            if (hasSchoolAdmin) return true;
        }

        // Capability grant?
        return await _capabilities.HasCapabilityAsync(
            userId.Value, CapVersion, "school:homeroom:assign", "school", scopeId, ct);
    }

    public record HomeroomAssignmentDto(
        long Id, int PersonnelId, string FirstName, string LastName, string? Photo,
        string SchoolCode, short AcademicYear, short? Term,
        long GradeLevelId, short ClassroomNumber, string Role,
        int AssignedByUserId, DateTimeOffset AssignedAt, DateOnly? EndDate);

    public record AssignRequest(
        int[] PersonnelIds, long GradeLevelId, short ClassroomNumber,
        short AcademicYear, short? Term);

    /// <summary>
    /// List all advisor assignments for a school × academic year.
    /// Joined with Personnel for display (FirstName, LastName, Photo).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HomeroomAssignmentDto>>> List(
        string schoolCode, [FromQuery] short academicYear, [FromQuery] short? term, CancellationToken ct)
    {
        var rows = await (
            from a in _db.TeacherHomeroomAssignments.AsNoTracking()
            join p in _db.Personnel.AsNoTracking() on a.PersonnelId equals p.Id
            where a.SchoolCode == schoolCode
                  && a.AcademicYear == academicYear
                  && (term == null || a.Term == term)
                  && a.DeletedAt == null
            orderby a.GradeLevelId, a.ClassroomNumber, p.FirstName
            select new HomeroomAssignmentDto(
                a.Id, p.Id, p.FirstName, p.LastName, p.Photo,
                a.SchoolCode, a.AcademicYear, a.Term,
                a.GradeLevelId, a.ClassroomNumber, a.Role,
                a.AssignedByUserId, a.AssignedAt, a.EndDate)
        ).ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>
    /// Bulk assign teachers as advisors of a single classroom for an academic year.
    /// Replaces existing assignments for that (school, year, term, grade, room) tuple
    /// with the new list (additions inserted, removals soft-deleted).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<IReadOnlyList<HomeroomAssignmentDto>>> Assign(
        string schoolCode, [FromBody] AssignRequest req, CancellationToken ct)
    {
        if (!await CanAssignAsync(schoolCode, ct)) return Forbid();
        var userId = CurrentUserId!.Value;

        var existing = await _db.TeacherHomeroomAssignments
            .Where(a => a.SchoolCode == schoolCode
                        && a.AcademicYear == req.AcademicYear
                        && a.Term == req.Term
                        && a.GradeLevelId == req.GradeLevelId
                        && a.ClassroomNumber == req.ClassroomNumber
                        && a.DeletedAt == null)
            .ToListAsync(ct);

        var keepSet = req.PersonnelIds.ToHashSet();
        var existingSet = existing.Select(e => e.PersonnelId).ToHashSet();

        // Soft-delete removed
        foreach (var row in existing.Where(e => !keepSet.Contains(e.PersonnelId)))
        {
            row.DeletedAt = DateTimeOffset.UtcNow;
            row.DeletedByUserId = userId;
        }

        // Insert added
        foreach (var pid in req.PersonnelIds.Where(p => !existingSet.Contains(p)))
        {
            _db.TeacherHomeroomAssignments.Add(new TeacherHomeroomAssignment
            {
                PersonnelId = pid,
                SchoolCode = schoolCode,
                AcademicYear = req.AcademicYear,
                Term = req.Term,
                GradeLevelId = req.GradeLevelId,
                ClassroomNumber = req.ClassroomNumber,
                Role = "advisor",
                AssignedByUserId = userId,
                AssignedAt = DateTimeOffset.UtcNow,
            });
        }

        await _db.SaveChangesAsync(ct);

        return await List(schoolCode, req.AcademicYear, req.Term, ct);
    }

    /// <summary>
    /// Soft-delete a single assignment.
    /// </summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Remove(string schoolCode, long id, CancellationToken ct)
    {
        if (!await CanAssignAsync(schoolCode, ct)) return Forbid();

        var row = await _db.TeacherHomeroomAssignments
            .FirstOrDefaultAsync(a => a.Id == id && a.SchoolCode == schoolCode && a.DeletedAt == null, ct);
        if (row == null) return NotFound();

        row.DeletedAt = DateTimeOffset.UtcNow;
        row.DeletedByUserId = CurrentUserId;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
