using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

[ApiController]
[Route("api/v1/school/{schoolCode}/module")]
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
    public async Task<ActionResult<IEnumerable<SchoolModuleDto>>> GetSchoolModules(string schoolCode)
    {
        var schoolExists = await _context.Schools.AnyAsync(s => s.SchoolCode == schoolCode);
        if (!schoolExists)
            return NotFound(new { message = "School not found" });

        var schoolModules = await _context.SchoolModules
            .AsNoTracking()
            .Where(sm => sm.SchoolCode == schoolCode)
            .Include(sm => sm.Module)
            .Include(sm => sm.TeacherAssignments)
                .ThenInclude(ta => ta.Teacher)
                    .ThenInclude(t => t.TitlePrefix)
            .OrderBy(sm => sm.Module.SortOrder)
            .ThenBy(sm => sm.Module.Name)
            .Select(sm => new SchoolModuleDto(
                sm.Id, sm.SchoolCode, sm.ModuleId,
                sm.Module.Code, sm.Module.Name, sm.Module.Description,
                sm.Module.Icon, sm.Module.Category, sm.Module.Version,
                sm.Module.RoutePath,
                sm.IsEnabled, sm.Module.AssignableToTeacher, sm.InstalledAt,
                EF.Property<bool>(sm, "IsPilot"),
                EF.Property<string?>(sm, "Notes"),
                sm.TeacherAssignments
                    .Where(ta => ta.IsActive)
                    .Select(ta => new TeacherAssignmentDto(
                        ta.Id, ta.TeacherId,
                        (ta.Teacher.TitlePrefix != null ? ta.Teacher.TitlePrefix.NameTh : "")
                            + ta.Teacher.FirstName + " " + ta.Teacher.LastName,
                        ta.IsActive, ta.AssignedAt
                    )).ToList()
            ))
            .ToListAsync();

        return Ok(schoolModules);
    }

    /// <summary>
    /// Get modules available for this school (from area assignments).
    /// </summary>
    [HttpGet("available")]
    public async Task<ActionResult> GetAvailableModules(string schoolCode)
    {
        var school = await _context.Schools
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SchoolCode == schoolCode);
        if (school == null)
            return NotFound(new { message = "School not found" });

        if (school.AreaId == 0)
            return Ok(new List<object>());

        // Installed module IDs for this school — used to exclude from the available list
        var installedModuleIds = await _context.SchoolModules
            .AsNoTracking()
            .Where(sm => sm.SchoolCode == schoolCode)
            .Select(sm => sm.ModuleId)
            .ToListAsync();

        var installedSet = new HashSet<int>(installedModuleIds);

        var areaModules = await _context.Set<SBD.Domain.Entities.AreaModuleAssignment>()
            .AsNoTracking()
            // Only return modules the area has enabled AND school is allowed to self-install
            .Where(ama => ama.AreaId == school.AreaId && ama.IsEnabled && ama.AllowSchoolSelfEnable)
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
                ama.Module.AssignableToTeacher,
                ama.Module.AssignableToStudent,
            })
            .ToListAsync();

        // Exclude modules already installed at DB level (double-safety)
        var result = areaModules.Where(m => !installedSet.Contains(m.ModuleId)).ToList();

        return Ok(result);
    }

    /// <summary>
    /// Install a module for a school.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SchoolModuleDto>> InstallModule(string schoolCode, [FromBody] InstallModuleRequest request)
    {
        var schoolExists = await _context.Schools.AnyAsync(s => s.SchoolCode == schoolCode);
        if (!schoolExists)
            return NotFound(new { message = "School not found" });

        var module = await _context.Modules.FirstOrDefaultAsync(m => m.Id == request.ModuleId);
        if (module == null)
            return NotFound(new { message = "Module not found" });

        var alreadyInstalled = await _context.SchoolModules
            .AnyAsync(sm => sm.SchoolCode == schoolCode && sm.ModuleId == request.ModuleId);
        if (alreadyInstalled)
            return Conflict(new { message = "Module is already installed for this school" });

        var schoolModule = new SchoolModule
        {
            SchoolCode = schoolCode,
            ModuleId = request.ModuleId,
            IsEnabled = true,
            InstalledAt = DateTimeOffset.UtcNow
        };

        _context.SchoolModules.Add(schoolModule);

        // Set shadow properties
        var entry = _context.Entry(schoolModule);
        entry.Property("IsPilot").CurrentValue = request.IsPilot;
        entry.Property("Notes").CurrentValue = request.Notes;

        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetSchoolModules), new { schoolCode },
            new SchoolModuleDto(
                schoolModule.Id, schoolCode, module.Id, module.Code, module.Name,
                module.Description, module.Icon, module.Category, module.Version,
                module.RoutePath,
                schoolModule.IsEnabled, module.AssignableToTeacher, schoolModule.InstalledAt,
                request.IsPilot, request.Notes,
                new List<TeacherAssignmentDto>()
            ));
    }

    /// <summary>
    /// Uninstall a module from a school.
    /// </summary>
    [HttpDelete("{schoolModuleId:int}")]
    public async Task<ActionResult> UninstallModule(string schoolCode, int schoolModuleId)
    {
        var schoolModule = await _context.SchoolModules
            .AsTracking()
            .Include(sm => sm.Module)
            .FirstOrDefaultAsync(sm => sm.Id == schoolModuleId && sm.SchoolCode == schoolCode);

        if (schoolModule == null)
            return NotFound(new { message = "School module not found" });

        // Core modules are system-mandatory — cannot be uninstalled by school admin
        if (schoolModule.Module.Category == "Core")
            return StatusCode(403, new { message = "ไม่สามารถถอนการติดตั้ง Core Module ได้ โมดูลนี้เป็นระบบบังคับ" });

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
    public async Task<ActionResult> ToggleModule(string schoolCode, int schoolModuleId)
    {
        var schoolModule = await _context.SchoolModules
            .AsTracking()
            .FirstOrDefaultAsync(sm => sm.Id == schoolModuleId && sm.SchoolCode == schoolCode);

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
        string schoolCode, int schoolModuleId, [FromBody] AssignTeacherRequest request)
    {
        var schoolModule = await _context.SchoolModules
            .Include(sm => sm.Module)
            .FirstOrDefaultAsync(sm => sm.Id == schoolModuleId && sm.SchoolCode == schoolCode);

        if (schoolModule == null)
            return NotFound(new { message = "School module not found" });

        if (!schoolModule.Module.AssignableToTeacher)
            return BadRequest(new { message = "This module does not support teacher assignment" });

        var teacherInSchool = await _context.Set<PersonnelSchoolAssignment>()
            .AnyAsync(psa => psa.PersonnelId == request.TeacherId && psa.SchoolCode == schoolCode);
        if (!teacherInSchool)
            return BadRequest(new { message = "Teacher is not assigned to this school" });

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

        var teacher = await _context.Set<Personnel>()
            .AsNoTracking()
            .Include(p => p.TitlePrefix)
            .FirstAsync(p => p.Id == request.TeacherId);

        var teacherName = (teacher.TitlePrefix?.NameTh ?? "") + teacher.FirstName + " " + teacher.LastName;

        return CreatedAtAction(nameof(GetSchoolModules), new { schoolCode },
            new TeacherAssignmentDto(assignment.Id, teacher.Id, teacherName, true, assignment.AssignedAt));
    }

    /// <summary>
    /// Get all teacher assignments for a school module.
    /// </summary>
    [HttpGet("{schoolModuleId:int}/teacher")]
    public async Task<ActionResult<IEnumerable<TeacherAssignmentDto>>> GetTeacherAssignments(
        string schoolCode, int schoolModuleId)
    {
        var exists = await _context.SchoolModules
            .AnyAsync(sm => sm.Id == schoolModuleId && sm.SchoolCode == schoolCode);
        if (!exists)
            return NotFound(new { message = "School module not found" });

        var assignments = await _context.Set<TeacherModuleAssignment>()
            .AsNoTracking()
            .Where(ta => ta.SchoolModuleId == schoolModuleId)
            .Include(ta => ta.Teacher)
                .ThenInclude(t => t.TitlePrefix)
            .Select(ta => new TeacherAssignmentDto(
                ta.Id, ta.TeacherId,
                (ta.Teacher.TitlePrefix != null ? ta.Teacher.TitlePrefix.NameTh : "")
                    + ta.Teacher.FirstName + " " + ta.Teacher.LastName,
                ta.IsActive, ta.AssignedAt
            ))
            .ToListAsync();

        return Ok(assignments);
    }

    /// <summary>
    /// Remove a teacher from a school module.
    /// </summary>
    [HttpDelete("{schoolModuleId:int}/teacher/{assignmentId:int}")]
    public async Task<ActionResult> RemoveTeacher(string schoolCode, int schoolModuleId, int assignmentId)
    {
        var assignment = await _context.Set<TeacherModuleAssignment>()
            .AsTracking()
            .FirstOrDefaultAsync(ta =>
                ta.Id == assignmentId
                && ta.SchoolModuleId == schoolModuleId
                && ta.SchoolModule.SchoolCode == schoolCode);

        if (assignment == null)
            return NotFound(new { message = "Teacher assignment not found" });

        _context.Set<TeacherModuleAssignment>().Remove(assignment);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}

// --- DTOs ---

public record SchoolModuleDto(
    int Id, string SchoolCode, int ModuleId,
    string ModuleCode, string ModuleName, string? ModuleDescription,
    string? ModuleIcon, string ModuleCategory, string? ModuleVersion,
    string? ModuleRoutePath,
    bool IsEnabled, bool AssignableToTeacher, DateTimeOffset InstalledAt,
    bool IsPilot, string? Notes,
    List<TeacherAssignmentDto> TeacherAssignments
);

public record TeacherAssignmentDto(
    int Id, int TeacherId, string TeacherName,
    bool IsActive, DateTimeOffset AssignedAt
);

public record InstallModuleRequest(int ModuleId, bool IsPilot = false, string? Notes = null);
public record AssignTeacherRequest(int TeacherId);
