using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Minio;
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
    public async Task<ActionResult<IEnumerable<ModuleListDto>>> GetAll()
    {
        var modules = await _context.Modules
            .AsNoTracking()
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name)
            .Select(m => new ModuleListDto(
                m.Id, m.Code, m.Name, m.Description, m.Version, m.Category,
                m.IsDefault, m.IsEnabled, m.AssignableToTeacher, m.AssignableToStudent,
                m.Icon, m.RoutePath, m.SortOrder,
                EF.Property<string>(m, "VisibilityLevels"),
                EF.Property<string>(m, "RegistrationType"),
                EF.Property<string?>(m, "EntryUrl"),
                EF.Property<string?>(m, "BundlePath"),
                EF.Property<string?>(m, "Author"),
                EF.Property<string?>(m, "License"),
                EF.Property<DateTimeOffset>(m, "CreatedAt"),
                EF.Property<DateTimeOffset>(m, "UpdatedAt")
            ))
            .ToListAsync();
        return Ok(modules);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ModuleDetailDto>> GetById(int id)
    {
        var module = await _context.Modules
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new
            {
                m.Id, m.Code, m.Name, m.Description, m.Version, m.Category,
                m.IsDefault, m.IsEnabled, m.AssignableToTeacher, m.AssignableToStudent,
                m.Icon, m.RoutePath, m.SortOrder,
                VisibilityLevels = EF.Property<string>(m, "VisibilityLevels"),
                RegistrationType = EF.Property<string>(m, "RegistrationType"),
                EntryUrl = EF.Property<string?>(m, "EntryUrl"),
                BundlePath = EF.Property<string?>(m, "BundlePath"),
                ConfigJson = EF.Property<string?>(m, "ConfigJson"),
                Author = EF.Property<string?>(m, "Author"),
                License = EF.Property<string?>(m, "License"),
                CreatedAt = EF.Property<DateTimeOffset>(m, "CreatedAt"),
                UpdatedAt = EF.Property<DateTimeOffset>(m, "UpdatedAt"),
            })
            .FirstOrDefaultAsync();
        if (module == null) return NotFound(new { message = "Module not found" });

        var schoolCount = await _context.SchoolModules.CountAsync(sm => sm.ModuleId == id);
        var areaCount = await _context.Set<SBD.Domain.Entities.AreaModuleAssignment>()
            .CountAsync(ama => ama.ModuleId == id);

        return Ok(new ModuleDetailDto(
            module.Id, module.Code, module.Name, module.Description, module.Version,
            module.Category, module.IsDefault, module.IsEnabled,
            module.AssignableToTeacher, module.AssignableToStudent,
            module.Icon, module.RoutePath, module.SortOrder,
            module.VisibilityLevels, module.RegistrationType, module.EntryUrl,
            module.BundlePath, module.ConfigJson, module.Author, module.License,
            module.CreatedAt, module.UpdatedAt, schoolCount, areaCount
        ));
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
            SortOrder = request.SortOrder,
        };

        _context.Modules.Add(module);

        // Set shadow properties for new fields
        var entry = _context.Entry(module);
        entry.Property("VisibilityLevels").CurrentValue = request.VisibilityLevels ?? "school";
        entry.Property("RegistrationType").CurrentValue = request.RegistrationType ?? "internal";
        entry.Property("EntryUrl").CurrentValue = request.EntryUrl;
        entry.Property("BundlePath").CurrentValue = request.BundlePath;
        entry.Property("ConfigJson").CurrentValue = request.ConfigJson;
        entry.Property("Author").CurrentValue = request.Author;
        entry.Property("License").CurrentValue = request.License;
        var now = DateTimeOffset.UtcNow;
        entry.Property("CreatedAt").CurrentValue = now;
        entry.Property("UpdatedAt").CurrentValue = now;

        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKey);

        return CreatedAtAction(nameof(GetById), new { id = module.Id }, module);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Module>> Update(int id, [FromBody] ModuleRequest request)
    {
        var module = await _context.Modules.AsTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (module == null) return NotFound(new { message = "Module not found" });

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

        // Update shadow properties
        var entry = _context.Entry(module);
        if (request.VisibilityLevels != null)
            entry.Property("VisibilityLevels").CurrentValue = request.VisibilityLevels;
        if (request.RegistrationType != null)
            entry.Property("RegistrationType").CurrentValue = request.RegistrationType;
        entry.Property("EntryUrl").CurrentValue = request.EntryUrl;
        entry.Property("BundlePath").CurrentValue = request.BundlePath;
        entry.Property("ConfigJson").CurrentValue = request.ConfigJson;
        entry.Property("Author").CurrentValue = request.Author;
        entry.Property("License").CurrentValue = request.License;
        entry.Property("UpdatedAt").CurrentValue = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKey);

        return Ok(module);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var module = await _context.Modules.AsTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (module == null) return NotFound(new { message = "Module not found" });

        var hasSchoolModules = await _context.SchoolModules.AnyAsync(sm => sm.ModuleId == id);
        if (hasSchoolModules)
            return Conflict(new { message = "Cannot delete module that is installed by schools. Remove school installations first." });

        var hasAreaAssignments = await _context.Set<SBD.Domain.Entities.AreaModuleAssignment>()
            .AnyAsync(ama => ama.ModuleId == id);
        if (hasAreaAssignments)
            return Conflict(new { message = "Cannot delete module that is assigned to areas. Remove area assignments first." });

        _context.Modules.Remove(module);
        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKey);

        return NoContent();
    }

    /// <summary>
    /// Get assignment summary for a module: which areas and schools have it.
    /// </summary>
    [HttpGet("{id:int}/assignments")]
    public async Task<ActionResult> GetAssignments(int id)
    {
        if (!await _context.Modules.AnyAsync(m => m.Id == id))
            return NotFound(new { message = "Module not found" });

        var areaAssignments = await _context.Set<SBD.Domain.Entities.AreaModuleAssignment>()
            .AsNoTracking()
            .Where(ama => ama.ModuleId == id)
            .Include(ama => ama.Area)
            .Select(ama => new
            {
                ama.Id, ama.AreaId,
                AreaName = ama.Area.NameTh,
                ama.IsEnabled, ama.AllowSchoolSelfEnable,
                ama.AssignedAt, ama.Notes
            })
            .ToListAsync();

        var schoolAssignments = await _context.SchoolModules
            .AsNoTracking()
            .Where(sm => sm.ModuleId == id)
            .Include(sm => sm.School)
            .Select(sm => new
            {
                sm.Id, sm.SchoolId,
                SchoolName = sm.School.NameTh,
                sm.IsEnabled,
                IsPilot = EF.Property<bool>(sm, "IsPilot"),
                sm.InstalledAt,
                Notes = EF.Property<string?>(sm, "Notes")
            })
            .ToListAsync();

        return Ok(new { areaAssignments, schoolAssignments });
    }

    /// <summary>
    /// Get visibility summary for a module.
    /// </summary>
    [HttpGet("{id:int}/visibility")]
    public async Task<ActionResult> GetVisibility(int id)
    {
        var module = await _context.Modules
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new
            {
                m.Id, m.Code,
                VisibilityLevels = EF.Property<string>(m, "VisibilityLevels"),
                RegistrationType = EF.Property<string>(m, "RegistrationType"),
            })
            .FirstOrDefaultAsync();
        if (module == null) return NotFound(new { message = "Module not found" });

        var levels = module.VisibilityLevels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var areaCount = await _context.Set<SBD.Domain.Entities.AreaModuleAssignment>()
            .CountAsync(ama => ama.ModuleId == id && ama.IsEnabled);
        var schoolCount = await _context.SchoolModules.CountAsync(sm => sm.ModuleId == id && sm.IsEnabled);

        return Ok(new
        {
            moduleId = module.Id,
            moduleCode = module.Code,
            visibilityLevels = levels,
            registrationType = module.RegistrationType,
            activeAreaCount = areaCount,
            activeSchoolCount = schoolCount
        });
    }

    /// <summary>
    /// Upload a module bundle to MinIO storage.
    /// </summary>
    [HttpPost("upload")]
    public async Task<ActionResult> UploadBundle([FromForm] int moduleId, IFormFile file)
    {
        var module = await _context.Modules.AsTracking().FirstOrDefaultAsync(m => m.Id == moduleId);
        if (module == null) return NotFound(new { message = "Module not found" });

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided" });

        var minioConfig = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var endpoint = minioConfig["MinIO:Endpoint"] ?? "localhost:9000";
        var accessKey = minioConfig["MinIO:AccessKey"] ?? "admin";
        var secretKeyVal = minioConfig["MinIO:SecretKey"] ?? "password";
        var bucketName = minioConfig["MinIO:BucketName"] ?? "sbd-main";
        var useSSL = bool.Parse(minioConfig["MinIO:UseSSL"] ?? "false");

        var minio = new Minio.MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKeyVal)
            .WithSSL(useSSL)
            .Build();

        var objectName = $"module-bundles/{module.Code}/{file.FileName}";

        using var stream = file.OpenReadStream();
        await minio.PutObjectAsync(new Minio.DataModel.Args.PutObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectName)
            .WithStreamData(stream)
            .WithObjectSize(file.Length)
            .WithContentType(file.ContentType));

        var entry = _context.Entry(module);
        entry.Property("BundlePath").CurrentValue = objectName;
        entry.Property("RegistrationType").CurrentValue = "uploaded";
        entry.Property("UpdatedAt").CurrentValue = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKey);

        return Ok(new { message = "Bundle uploaded successfully", bundlePath = objectName });
    }
}

// --- DTOs ---

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
    int SortOrder,
    string? VisibilityLevels,
    string? RegistrationType,
    string? EntryUrl,
    string? BundlePath,
    string? ConfigJson,
    string? Author,
    string? License
);

public record ModuleListDto(
    int Id, string Code, string Name, string? Description, string? Version,
    string Category, bool IsDefault, bool IsEnabled,
    bool AssignableToTeacher, bool AssignableToStudent,
    string? Icon, string? RoutePath, int SortOrder,
    string VisibilityLevels, string RegistrationType,
    string? EntryUrl, string? BundlePath,
    string? Author, string? License,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt
);

public record ModuleDetailDto(
    int Id, string Code, string Name, string? Description, string? Version,
    string Category, bool IsDefault, bool IsEnabled,
    bool AssignableToTeacher, bool AssignableToStudent,
    string? Icon, string? RoutePath, int SortOrder,
    string VisibilityLevels, string RegistrationType,
    string? EntryUrl, string? BundlePath, string? ConfigJson,
    string? Author, string? License,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    int SchoolCount, int AreaCount
);
