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

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AreaModuleDto>>> GetAreaModules(int areaId)
    {
        if (!await _context.Areas.AnyAsync(a => a.Id == areaId))
            return NotFound(new { message = "Area not found" });

        var modules = await _context.AreaModuleAssignments
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

    [HttpPost("{moduleId:int}")]
    public async Task<ActionResult<AreaModuleDto>> AssignModule(
        int areaId, int moduleId, [FromBody] AssignAreaModuleRequest? request = null)
    {
        if (!await _context.Areas.AnyAsync(a => a.Id == areaId))
            return NotFound(new { message = "Area not found" });

        var module = await _context.Modules.FirstOrDefaultAsync(m => m.Id == moduleId);
        if (module == null)
            return NotFound(new { message = "Module not found" });

        if (await _context.AreaModuleAssignments.AnyAsync(ama => ama.AreaId == areaId && ama.ModuleId == moduleId))
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

        _context.AreaModuleAssignments.Add(assignment);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAreaModules), new { areaId },
            new AreaModuleDto(
                assignment.Id, areaId, moduleId, module.Code, module.Name,
                module.Description, module.Icon, module.Category, module.Version,
                assignment.IsEnabled, assignment.AllowSchoolSelfEnable,
                assignment.AssignedAt, assignment.AssignedBy, assignment.Notes
            ));
    }

    [HttpPut("{moduleId:int}")]
    public async Task<ActionResult> UpdateAssignment(
        int areaId, int moduleId, [FromBody] UpdateAreaModuleRequest request)
    {
        var assignment = await _context.AreaModuleAssignments
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

    [HttpDelete("{moduleId:int}")]
    public async Task<ActionResult> RemoveModule(int areaId, int moduleId)
    {
        var assignment = await _context.AreaModuleAssignments
            .AsTracking()
            .FirstOrDefaultAsync(ama => ama.AreaId == areaId && ama.ModuleId == moduleId);

        if (assignment == null)
            return NotFound(new { message = "Module is not assigned to this area" });

        _context.AreaModuleAssignments.Remove(assignment);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{moduleId:int}/schools")]
    public async Task<ActionResult> GetSchoolsUsingModule(int areaId, int moduleId)
    {
        if (!await _context.AreaModuleAssignments.AnyAsync(ama => ama.AreaId == areaId && ama.ModuleId == moduleId))
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
                sm.IsEnabled, sm.IsPilot,
                sm.InstalledAt, sm.Notes
            })
            .ToListAsync();

        return Ok(schools);
    }

    [HttpPost("{moduleId:int}/schools")]
    public async Task<ActionResult> AssignModuleToSchools(
        int areaId, int moduleId, [FromBody] AssignModuleToSchoolsRequest request)
    {
        if (!await _context.AreaModuleAssignments.AnyAsync(ama => ama.AreaId == areaId && ama.ModuleId == moduleId))
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
        var newAssignments = newSchoolIds.Select(schoolId => new SchoolModule
        {
            SchoolId = schoolId,
            ModuleId = moduleId,
            IsEnabled = true,
            IsPilot = request.IsPilot,
            Notes = request.Notes,
            InstalledAt = DateTimeOffset.UtcNow
        }).ToList();

        _context.SchoolModules.AddRange(newAssignments);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = $"Module assigned to {newAssignments.Count} school(s)",
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
