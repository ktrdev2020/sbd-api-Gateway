using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

[ApiController]
[Route("api/v1/area/{areaId:int}/modules")]
[Authorize]
public class AreaModuleController : ControllerBase
{
    private readonly SbdDbContext _context;

    public AreaModuleController(SbdDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get all modules assigned to an area.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AreaModuleDto>>> GetAreaModules(int areaId)
    {
        var areaExists = await _context.Areas.AnyAsync(a => a.Id == areaId);
        if (!areaExists)
            return NotFound(new { message = "Area not found" });

        var modules = await _context.Set<AreaModuleAssignment>()
            .AsNoTracking()
            .Where(ama => ama.AreaId == areaId)
            .Include(ama => ama.Module)
            .OrderBy(ama => ama.Module.SortOrder)
            .ThenBy(ama => ama.Module.Name)
            .Select(ama => new AreaModuleDto(
                ama.Id, ama.AreaId, ama.ModuleId,
                ama.Module.Code, ama.Module.Name, ama.Module.Description,
                ama.Module.Icon, ama.Module.Category, ama.Module.Version,
                ama.IsEnabled, ama.AllowSchoolSelfEnable,
                ama.AssignedAt, ama.AssignedBy, ama.Notes
            ))
            .ToListAsync();

        return Ok(modules);
    }

    /// <summary>
    /// Assign a module to an area.
    /// </summary>
    [HttpPost("{moduleId:int}")]
    public async Task<ActionResult<AreaModuleDto>> AssignModule(
        int areaId, int moduleId, [FromBody] AssignAreaModuleRequest? request = null)
    {
        var areaExists = await _context.Areas.AnyAsync(a => a.Id == areaId);
        if (!areaExists)
            return NotFound(new { message = "Area not found" });

        var module = await _context.Modules.FirstOrDefaultAsync(m => m.Id == moduleId);
        if (module == null)
            return NotFound(new { message = "Module not found" });

        var alreadyAssigned = await _context.Set<AreaModuleAssignment>()
            .AnyAsync(ama => ama.AreaId == areaId && ama.ModuleId == moduleId);
        if (alreadyAssigned)
            return Conflict(new { message = "Module is already assigned to this area" });

        var assignment = new AreaModuleAssignment
        {
            AreaId = areaId,
            ModuleId = moduleId,
            IsEnabled = true,
            AllowSchoolSelfEnable = request?.AllowSchoolSelfEnable ?? false,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedBy = request?.AssignedBy,
            Notes = request?.Notes
        };

        _context.Set<AreaModuleAssignment>().Add(assignment);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAreaModules), new { areaId },
            new AreaModuleDto(
                assignment.Id, areaId, moduleId, module.Code, module.Name,
                module.Description, module.Icon, module.Category, module.Version,
                assignment.IsEnabled, assignment.AllowSchoolSelfEnable,
                assignment.AssignedAt, assignment.AssignedBy, assignment.Notes
            ));
    }

    /// <summary>
    /// Update an area module assignment (enable/disable, allow school self-enable).
    /// </summary>
    [HttpPut("{moduleId:int}")]
    public async Task<ActionResult> UpdateAssignment(
        int areaId, int moduleId, [FromBody] UpdateAreaModuleRequest request)
    {
        var assignment = await _context.Set<AreaModuleAssignment>()
            .AsTracking()
            .FirstOrDefaultAsync(ama => ama.AreaId == areaId && ama.ModuleId == moduleId);

        if (assignment == null)
            return NotFound(new { message = "Module is not assigned to this area" });

        assignment.IsEnabled = request.IsEnabled;
        assignment.AllowSchoolSelfEnable = request.AllowSchoolSelfEnable;
        assignment.Notes = request.Notes;

        await _context.SaveChangesAsync();

        return Ok(new { assignment.Id, assignment.IsEnabled, assignment.AllowSchoolSelfEnable });
    }

    /// <summary>
    /// Remove a module from an area.
    /// </summary>
    [HttpDelete("{moduleId:int}")]
    public async Task<ActionResult> RemoveModule(int areaId, int moduleId)
    {
        var assignment = await _context.Set<AreaModuleAssignment>()
            .AsTracking()
            .Include(ama => ama.Module)
            .FirstOrDefaultAsync(ama => ama.AreaId == areaId && ama.ModuleId == moduleId);

        if (assignment == null)
            return NotFound(new { message = "Module is not assigned to this area" });

        // Core modules are system-mandatory — cannot be removed by area admin
        if (assignment.Module.Category == "Core")
            return StatusCode(403, new { message = "ไม่สามารถลบ Core Module ได้ โมดูลนี้เป็นระบบบังคับ" });

        _context.Set<AreaModuleAssignment>().Remove(assignment);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Get all schools in an area (for the school picker when assigning modules).
    /// </summary>
    [HttpGet("/api/v1/area/{areaId:int}/schools/list")]
    public async Task<ActionResult> GetAreaSchoolsList(int areaId)
    {
        var schools = await _context.Schools
            .AsNoTracking()
            .Where(s => s.AreaId == areaId)
            .OrderBy(s => s.NameTh)
            .Select(s => new { s.Id, NameTh = s.NameTh, s.SchoolCode })
            .ToListAsync();
        return Ok(schools);
    }

    /// <summary>
    /// Get schools using a specific module in this area.
    /// </summary>
    [HttpGet("{moduleId:int}/schools")]
    public async Task<ActionResult> GetSchoolsUsingModule(int areaId, int moduleId)
    {
        var assigned = await _context.Set<AreaModuleAssignment>()
            .AnyAsync(ama => ama.AreaId == areaId && ama.ModuleId == moduleId);
        if (!assigned)
            return NotFound(new { message = "Module is not assigned to this area" });

        var schools = await _context.SchoolModules
            .AsNoTracking()
            .Where(sm => sm.ModuleId == moduleId && sm.School.AreaId == areaId)
            .Include(sm => sm.School)
            .Select(sm => new
            {
                sm.Id, sm.SchoolId,
                SchoolName = sm.School.NameTh,
                SchoolCode = sm.School.SchoolCode,
                sm.IsEnabled,
                IsPilot = EF.Property<bool>(sm, "IsPilot"),
                sm.InstalledAt,
                Notes = EF.Property<string?>(sm, "Notes")
            })
            .ToListAsync();

        return Ok(schools);
    }

    /// <summary>
    /// Toggle enable/disable a module for a specific school within an area (data is preserved).
    /// </summary>
    [HttpPut("{moduleId:int}/schools/{schoolModuleId:int}/toggle")]
    public async Task<ActionResult> ToggleSchoolModuleByArea(int areaId, int moduleId, int schoolModuleId)
    {
        var sm = await _context.SchoolModules
            .AsTracking()
            .Include(s => s.School)
            .FirstOrDefaultAsync(s => s.Id == schoolModuleId && s.ModuleId == moduleId && s.School.AreaId == areaId);

        if (sm == null)
            return NotFound(new { message = "School module not found in this area" });

        sm.IsEnabled = !sm.IsEnabled;
        await _context.SaveChangesAsync();

        return Ok(new { sm.Id, sm.IsEnabled });
    }

    /// <summary>
    /// Assign a module to specific schools within an area (e.g. for pilot programs).
    /// </summary>
    [HttpPost("{moduleId:int}/schools")]
    public async Task<ActionResult> AssignModuleToSchools(
        int areaId, int moduleId, [FromBody] AssignModuleToSchoolsRequest request)
    {
        var areaAssigned = await _context.Set<AreaModuleAssignment>()
            .AnyAsync(ama => ama.AreaId == areaId && ama.ModuleId == moduleId);
        if (!areaAssigned)
            return NotFound(new { message = "Module is not assigned to this area" });

        if (!await _context.Modules.AnyAsync(m => m.Id == moduleId))
            return NotFound(new { message = "Module not found" });

        var schoolIds = request.SchoolIds.Distinct().ToList();
        var schoolsInArea = await _context.Schools
            .Where(s => schoolIds.Contains(s.Id) && s.AreaId == areaId)
            .Select(s => s.Id)
            .ToListAsync();

        var invalidSchools = schoolIds.Except(schoolsInArea).ToList();
        if (invalidSchools.Any())
            return BadRequest(new { message = "Some schools do not belong to this area", invalidSchoolIds = invalidSchools });

        var alreadyInstalled = await _context.SchoolModules
            .Where(sm => schoolIds.Contains(sm.SchoolId) && sm.ModuleId == moduleId)
            .Select(sm => sm.SchoolId)
            .ToListAsync();

        var newSchoolIds = schoolIds.Except(alreadyInstalled).ToList();
        foreach (var schoolId in newSchoolIds)
        {
            var sm = new SchoolModule
            {
                SchoolId = schoolId,
                ModuleId = moduleId,
                IsEnabled = true,
                InstalledAt = DateTimeOffset.UtcNow
            };
            _context.SchoolModules.Add(sm);
            var smEntry = _context.Entry(sm);
            smEntry.Property("IsPilot").CurrentValue = request.IsPilot;
            smEntry.Property("Notes").CurrentValue = request.Notes;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = $"Module assigned to {newSchoolIds.Count} school(s)",
            assignedSchoolIds = newSchoolIds,
            alreadyInstalledSchoolIds = alreadyInstalled.Intersect(schoolIds).ToList()
        });
    }
}

// --- DTOs ---

public record AreaModuleDto(
    int Id, int AreaId, int ModuleId,
    string ModuleCode, string ModuleName, string? ModuleDescription,
    string? ModuleIcon, string ModuleCategory, string? ModuleVersion,
    bool IsEnabled, bool AllowSchoolSelfEnable,
    DateTimeOffset AssignedAt, int? AssignedBy, string? Notes
);

public record AssignAreaModuleRequest(
    bool AllowSchoolSelfEnable = false,
    int? AssignedBy = null,
    string? Notes = null
);

public record UpdateAreaModuleRequest(
    bool IsEnabled,
    bool AllowSchoolSelfEnable,
    string? Notes = null
);

public record AssignModuleToSchoolsRequest(
    List<int> SchoolIds,
    bool IsPilot = false,
    string? Notes = null
);
