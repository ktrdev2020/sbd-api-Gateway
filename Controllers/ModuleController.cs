using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;
using Module = SBD.Domain.Entities.Module;

namespace Gateway.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ModuleController : ControllerBase
{
    private readonly SbdDbContext _context;
    private readonly ICacheService _cache;
    private const string CacheKey = "refdata:modules";

    public ModuleController(SbdDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Module>>> GetAll()
    {
        var modules = await _context.Modules
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name)
            .ToListAsync();
        return Ok(modules);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Module>> GetById(int id)
    {
        var module = await _context.Modules.FirstOrDefaultAsync(m => m.Id == id);
        if (module == null) return NotFound(new { message = "Module not found" });
        return Ok(module);
    }

    [HttpPost]
    public async Task<ActionResult<Module>> Create([FromBody] ModuleRequest request)
    {
        if (await _context.Modules.AnyAsync(m => m.Code == request.Code))
            return Conflict(new { message = $"Module code '{request.Code}' already exists" });

        var module = new Module
        {
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            Version = request.Version ?? "1.0.0",
            Category = request.Category,
            IsDefault = request.IsDefault,
            IsEnabled = request.IsEnabled,
            AssignableToTeacher = request.AssignableToTeacher,
            AssignableToStudent = request.AssignableToStudent,
            Icon = request.Icon,
            RoutePath = request.RoutePath,
            SortOrder = request.SortOrder
        };

        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKey);

        return CreatedAtAction(nameof(GetById), new { id = module.Id }, module);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Module>> Update(int id, [FromBody] ModuleRequest request)
    {
        var module = await _context.Modules.AsTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (module == null) return NotFound(new { message = "Module not found" });

        // Check code uniqueness (if changed)
        if (module.Code != request.Code && await _context.Modules.AnyAsync(m => m.Code == request.Code))
            return Conflict(new { message = $"Module code '{request.Code}' already exists" });

        module.Code = request.Code;
        module.Name = request.Name;
        module.Description = request.Description;
        module.Version = request.Version ?? module.Version;
        module.Category = request.Category;
        module.IsDefault = request.IsDefault;
        module.IsEnabled = request.IsEnabled;
        module.AssignableToTeacher = request.AssignableToTeacher;
        module.AssignableToStudent = request.AssignableToStudent;
        module.Icon = request.Icon;
        module.RoutePath = request.RoutePath;
        module.SortOrder = request.SortOrder;

        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKey);

        return Ok(module);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var module = await _context.Modules.AsTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (module == null) return NotFound(new { message = "Module not found" });

        // Prevent deletion if schools have installed this module
        var hasSchoolModules = await _context.SchoolModules.AnyAsync(sm => sm.ModuleId == id);
        if (hasSchoolModules)
            return Conflict(new { message = "Cannot delete module that is installed by schools. Remove school installations first." });

        _context.Modules.Remove(module);
        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKey);

        return NoContent();
    }
}

public record ModuleRequest(
    string Code,
    string Name,
    string? Description,
    string? Version,
    string Category,
    bool IsDefault,
    bool IsEnabled,
    bool AssignableToTeacher,
    bool AssignableToStudent,
    string? Icon,
    string? RoutePath,
    int SortOrder
);
