using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

[ApiController]
[Route("api/v1/school/{schoolId:int}/module")]
[Authorize]
public class SchoolModuleController : ControllerBase
{
    private readonly SbdDbContext _context;

    public SchoolModuleController(SbdDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// List all modules installed for a school, including teacher assignments.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SchoolModuleDto>>> GetSchoolModules(int schoolId)
    {
        var schoolExists = await _context.Schools.AnyAsync(s => s.Id == schoolId);
        if (!schoolExists)
            return NotFound(new { message = "School not found" });

        var schoolModules = await _context.SchoolModules
            .AsNoTracking()
            .Where(sm => sm.SchoolId == schoolId)
            .Include(sm => sm.Module)
            .Include(sm => sm.TeacherAssignments)
                .ThenInclude(ta => ta.Teacher)
                    .ThenInclude(t => t.TitlePrefix)
            .OrderBy(sm => sm.Module.SortOrder)
            .ThenBy(sm => sm.Module.Name)
            .Select(sm => new SchoolModuleDto(
                sm.Id,
                sm.SchoolId,
                sm.ModuleId,
                sm.Module.Code,
                sm.Module.Name,
                sm.Module.Description,
                sm.Module.Icon,
                sm.Module.Category,
                sm.Module.Version,
                sm.IsEnabled,
                sm.Module.AssignableToTeacher,
                sm.InstalledAt,
                sm.IsPilot,
                sm.Notes,
                sm.TeacherAssignments
                    .Where(ta => ta.IsActive)
                    .Select(ta => new TeacherAssignmentDto(
                        ta.Id,
                        ta.TeacherId,
                        (ta.Teacher.TitlePrefix != null ? ta.Teacher.TitlePrefix.NameTh : "")
                            + ta.Teacher.FirstName + " " + ta.Teacher.LastName,
                        ta.IsActive,
                        ta.AssignedAt
                    )).ToList()
            ))
            .ToListAsync();

        return Ok(schoolModules);
    }

    /// <summary>
    /// Get modules available for this school (from area assignments).
    /// </summary>
    [HttpGet("available")]
    public async Task<ActionResult> GetAvailableModules(int schoolId)
    {
        var school = await _context.Schools
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == schoolId);
        if (school == null)
            return NotFound(new { message = "School not found" });

        if (school.AreaId == null)
            return Ok(new List<object>());

        // Get modules assigned to the school's area
        var areaModules = await _context.AreaModuleAssignments
            .AsNoTracking()
            .Where(ama => ama.AreaId == school.AreaId && ama.IsEnabled)
            .Include(ama => ama.Module)
            .Select(ama => new
            {
                ama.ModuleId,
                ama.Module.Code,
                ama.Module.Name,
                ama.Module.Description,
                ama.Module.Icon,
                ama.Module.Category,
                ama.Module.Version,
                ama.AllowSchoolSelfEnable,
                IsInstalled = _context.SchoolModules
                    .Any(sm => sm.SchoolId == schoolId && sm.ModuleId == ama.ModuleId)
            })
            .ToListAsync();

        return Ok(areaModules);
    }

    /// <summary>
    /// Install a module for a school.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SchoolModuleDto>> InstallModule(int schoolId, [FromBody] InstallModuleRequest request)
    {
        var schoolExists = await _context.Schools.AnyAsync(s => s.Id == schoolId);
        if (!schoolExists)
            return NotFound(new { message = "School not found" });

        var module = await _context.Modules.FirstOrDefaultAsync(m => m.Id == request.ModuleId);
        if (module == null)
            return NotFound(new { message = "Module not found" });

        var alreadyInstalled = await _context.SchoolModules
            .AnyAsync(sm => sm.SchoolId == schoolId && sm.ModuleId == request.ModuleId);
        if (alreadyInstalled)
            return Conflict(new { message = "Module is already installed for this school" });

        var schoolModule = new SchoolModule
        {
            SchoolId = schoolId,
            ModuleId = request.ModuleId,
            IsEnabled = true,
            IsPilot = request.IsPilot,
            Notes = request.Notes,
            InstalledAt = DateTimeOffset.UtcNow
        };

        _context.SchoolModules.Add(schoolModule);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetSchoolModules), new { schoolId },
            new SchoolModuleDto(
                schoolModule.Id, schoolId, module.Id, module.Code, module.Name,
                module.Description, module.Icon, module.Category, module.Version,
                schoolModule.IsEnabled, module.AssignableToTeacher, schoolModule.InstalledAt,
                schoolModule.IsPilot, schoolModule.Notes,
                new List<TeacherAssignmentDto>()
            ));
    }

    /// <summary>
    /// Uninstall a module from a school.
    /// </summary>
    [HttpDelete("{schoolModuleId:int}")]
    public async Task<ActionResult> UninstallModule(int schoolId, int schoolModuleId)
    {
        var schoolModule = await _context.SchoolModules
            .AsTracking()
            .FirstOrDefaultAsync(sm => sm.Id == schoolModuleId && sm.SchoolId == schoolId);

        if (schoolModule == null)
            return NotFound(new { message = "School module not found" });

        // Remove related teacher assignments first
        var assignments = await _context.Set<TeacherModuleAssignment>()
            .Where(ta => ta.SchoolModuleId == schoolModuleId)
            .ToListAsync();
        _context.Set<TeacherModuleAssignment>().RemoveRange(assignments);

        _context.SchoolModules.Remove(schoolModule);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Toggle enable/disable for a school module.
    /// </summary>
    [HttpPut("{schoolModuleId:int}/toggle")]
    public async Task<ActionResult> ToggleModule(int schoolId, int schoolModuleId)
    {
        var schoolModule = await _context.SchoolModules
            .AsTracking()
            .FirstOrDefaultAsync(sm => sm.Id == schoolModuleId && sm.SchoolId == schoolId);

        if (schoolModule == null)
            return NotFound(new { message = "School module not found" });

        schoolModule.IsEnabled = !schoolModule.IsEnabled;
        await _context.SaveChangesAsync();

        return Ok(new { schoolModule.Id, schoolModule.IsEnabled });
    }

    /// <summary>
    /// Assign a teacher to a school module.
    /// </summary>
    [HttpPost("{schoolModuleId:int}/teacher")]
    public async Task<ActionResult<TeacherAssignmentDto>> AssignTeacher(
        int schoolId, int schoolModuleId, [FromBody] AssignTeacherRequest request)
    {
        var schoolModule = await _context.SchoolModules
            .Include(sm => sm.Module)
            .FirstOrDefaultAsync(sm => sm.Id == schoolModuleId && sm.SchoolId == schoolId);

        if (schoolModule == null)
            return NotFound(new { message = "School module not found" });

        if (!schoolModule.Module.AssignableToTeacher)
            return BadRequest(new { message = "This module does not support teacher assignment" });

        // Verify the teacher belongs to this school
        var teacherInSchool = await _context.Set<PersonnelSchoolAssignment>()
            .AnyAsync(psa => psa.PersonnelId == request.TeacherId && psa.SchoolId == schoolId);
        if (!teacherInSchool)
            return BadRequest(new { message = "Teacher is not assigned to this school" });

        // Check for duplicate assignment
        var alreadyAssigned = await _context.Set<TeacherModuleAssignment>()
            .AnyAsync(ta => ta.TeacherId == request.TeacherId && ta.SchoolModuleId == schoolModuleId);
        if (alreadyAssigned)
            return Conflict(new { message = "Teacher is already assigned to this module" });

        var assignment = new TeacherModuleAssignment
        {
            TeacherId = request.TeacherId,
            SchoolModuleId = schoolModuleId,
            IsActive = true,
            AssignedAt = DateTimeOffset.UtcNow
        };

        _context.Set<TeacherModuleAssignment>().Add(assignment);
        await _context.SaveChangesAsync();

        // Fetch teacher name for response
        var teacher = await _context.Set<Personnel>()
            .AsNoTracking()
            .Include(p => p.TitlePrefix)
            .FirstAsync(p => p.Id == request.TeacherId);

        var teacherName = (teacher.TitlePrefix?.NameTh ?? "") + teacher.FirstName + " " + teacher.LastName;

        return CreatedAtAction(nameof(GetSchoolModules), new { schoolId },
            new TeacherAssignmentDto(assignment.Id, teacher.Id, teacherName, true, assignment.AssignedAt));
    }

    /// <summary>
    /// Get all teacher assignments for a school module.
    /// </summary>
    [HttpGet("{schoolModuleId:int}/teacher")]
    public async Task<ActionResult<IEnumerable<TeacherAssignmentDto>>> GetTeacherAssignments(
        int schoolId, int schoolModuleId)
    {
        var schoolModule = await _context.SchoolModules
            .AnyAsync(sm => sm.Id == schoolModuleId && sm.SchoolId == schoolId);
        if (!schoolModule)
            return NotFound(new { message = "School module not found" });

        var assignments = await _context.Set<TeacherModuleAssignment>()
            .AsNoTracking()
            .Where(ta => ta.SchoolModuleId == schoolModuleId)
            .Include(ta => ta.Teacher)
                .ThenInclude(t => t.TitlePrefix)
            .Select(ta => new TeacherAssignmentDto(
                ta.Id,
                ta.TeacherId,
                (ta.Teacher.TitlePrefix != null ? ta.Teacher.TitlePrefix.NameTh : "")
                    + ta.Teacher.FirstName + " " + ta.Teacher.LastName,
                ta.IsActive,
                ta.AssignedAt
            ))
            .ToListAsync();

        return Ok(assignments);
    }

    /// <summary>
    /// Remove a teacher from a school module.
    /// </summary>
    [HttpDelete("{schoolModuleId:int}/teacher/{assignmentId:int}")]
    public async Task<ActionResult> RemoveTeacher(int schoolId, int schoolModuleId, int assignmentId)
    {
        var assignment = await _context.Set<TeacherModuleAssignment>()
            .AsTracking()
            .FirstOrDefaultAsync(ta =>
                ta.Id == assignmentId
                && ta.SchoolModuleId == schoolModuleId
                && ta.SchoolModule.SchoolId == schoolId);

        if (assignment == null)
            return NotFound(new { message = "Teacher assignment not found" });

        _context.Set<TeacherModuleAssignment>().Remove(assignment);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}

// --- DTOs ---

public record SchoolModuleDto(
    int Id,
    int SchoolId,
    int ModuleId,
    string ModuleCode,
    string ModuleName,
    string? ModuleDescription,
    string? ModuleIcon,
    string ModuleCategory,
    string? ModuleVersion,
    bool IsEnabled,
    bool AssignableToTeacher,
    DateTimeOffset InstalledAt,
    bool IsPilot,
    string? Notes,
    List<TeacherAssignmentDto> TeacherAssignments
);

public record TeacherAssignmentDto(
    int Id,
    int TeacherId,
    string TeacherName,
    bool IsActive,
    DateTimeOffset AssignedAt
);

public record InstallModuleRequest(int ModuleId, bool IsPilot = false, string? Notes = null);
public record AssignTeacherRequest(int TeacherId);
