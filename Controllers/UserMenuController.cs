using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Returns feature modules visible to the authenticated user based on their
/// active role, school/area scope, and module assignments.
/// </summary>
[ApiController]
[Route("api/v1/user/menu")]
[Authorize]
public class UserMenuController : ControllerBase
{
    private readonly SbdDbContext _db;

    public UserMenuController(SbdDbContext db) => _db = db;

    /// <summary>
    /// Get the list of feature modules the current user is allowed to see.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserMenuItemDto>>> GetMenuModules()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        // Determine the user's primary role (highest privilege)
        var activeRole = DetermineActiveRole(roles);

        List<UserMenuItemDto> modules;

        switch (activeRole)
        {
            case "SuperAdmin":
                modules = await GetAllEnabledModules();
                break;

            case "AreaAdmin":
            {
                var areaId = await GetUserAreaId(userId);
                modules = areaId.HasValue
                    ? await GetAreaModules(areaId.Value)
                    : await GetModulesByVisibility("area");
                break;
            }

            case "SchoolAdmin":
            {
                var schoolCode = await GetUserSchoolCode(userId);
                modules = !string.IsNullOrEmpty(schoolCode)
                    ? await GetSchoolInstalledModules(schoolCode)
                    : await GetModulesByVisibility("school");
                break;
            }

            case "Teacher":
            {
                var schoolCode = await GetUserSchoolCode(userId);
                modules = !string.IsNullOrEmpty(schoolCode)
                    ? await GetTeacherModules(userId, schoolCode)
                    : await GetModulesByVisibility("teacher");
                break;
            }

            case "Student":
            {
                var schoolCode = await GetStudentSchoolCode(userId);
                modules = !string.IsNullOrEmpty(schoolCode)
                    ? await GetStudentModules(userId, schoolCode)
                    : await GetModulesByVisibility("student");
                break;
            }

            default:
                modules = new List<UserMenuItemDto>();
                break;
        }

        return Ok(modules);
    }

    // -------------------------------------------------------------------------
    // Role resolution
    // -------------------------------------------------------------------------

    private static string DetermineActiveRole(List<string> roles)
    {
        // Priority order: SuperAdmin > AreaAdmin > SchoolAdmin > Teacher > Student
        string[] priority = ["SuperAdmin", "AreaAdmin", "SchoolAdmin", "Teacher", "Student"];
        return priority.FirstOrDefault(p => roles.Contains(p)) ?? roles.FirstOrDefault() ?? "Student";
    }

    // -------------------------------------------------------------------------
    // Scope resolution (userId → schoolId / areaId)
    // -------------------------------------------------------------------------

    private async Task<int?> GetUserAreaId(int userId)
    {
        return await _db.Set<UserRole>()
            .Where(ur => ur.UserId == userId && ur.ScopeType == "Area")
            .Select(ur => ur.ScopeId)
            .FirstOrDefaultAsync();
    }

    private async Task<string?> GetUserSchoolCode(int userId)
    {
        // First try UserRole scope (Option A: int ScopeId → stringify to SchoolCode)
        var scopeSchoolId = await _db.Set<UserRole>()
            .Where(ur => ur.UserId == userId && ur.ScopeType == "School")
            .Select(ur => ur.ScopeId)
            .FirstOrDefaultAsync();
        if (scopeSchoolId.HasValue) return scopeSchoolId.Value.ToString();

        // Fallback: Personnel → PersonnelSchoolAssignment (SchoolCode is already string)
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Personnel)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.Personnel == null) return null;

        return await _db.Set<PersonnelSchoolAssignment>()
            .Where(psa => psa.PersonnelId == user.Personnel.Id && psa.IsPrimary)
            .Select(psa => psa.SchoolCode)
            .FirstOrDefaultAsync();
    }

    private async Task<string?> GetStudentSchoolCode(int userId)
    {
        // Student's school from UserRole scope (Option A: int ScopeId → SchoolCode)
        var scopeSchoolId = await _db.Set<UserRole>()
            .Where(ur => ur.UserId == userId && ur.ScopeType == "School")
            .Select(ur => ur.ScopeId)
            .FirstOrDefaultAsync();
        return scopeSchoolId.HasValue ? scopeSchoolId.Value.ToString() : null;
    }

    // -------------------------------------------------------------------------
    // Module queries by context
    // -------------------------------------------------------------------------

    /// <summary>SuperAdmin: all enabled non-core modules (Core category = structural nav handled by CoreMenuItems).</summary>
    private async Task<List<UserMenuItemDto>> GetAllEnabledModules()
    {
        return await _db.Modules
            .AsNoTracking()
            .Where(m => m.IsEnabled && m.Category != "Core")
            .OrderBy(m => m.SortOrder)
            .Select(m => ToDto(m))
            .ToListAsync();
    }

    /// <summary>Fallback: feature modules by visibility level string (excludes Core category).</summary>
    private async Task<List<UserMenuItemDto>> GetModulesByVisibility(string level)
    {
        return await _db.Modules
            .AsNoTracking()
            .Where(m => m.IsEnabled && m.Category != "Core" && m.VisibilityLevels.Contains(level))
            .OrderBy(m => m.SortOrder)
            .Select(m => ToDto(m))
            .ToListAsync();
    }

    /// <summary>AreaAdmin: feature modules assigned to their area + globally visible area modules (excludes Core category).</summary>
    private async Task<List<UserMenuItemDto>> GetAreaModules(int areaId)
    {
        // Modules explicitly assigned to this area
        var assignedModuleIds = await _db.Set<AreaModuleAssignment>()
            .Where(ama => ama.AreaId == areaId && ama.IsEnabled)
            .Select(ama => ama.ModuleId)
            .ToListAsync();

        // Feature modules visible at area level OR explicitly assigned
        // Core category is excluded — structural navigation is served by CoreMenuItems table
        return await _db.Modules
            .AsNoTracking()
            .Where(m => m.IsEnabled && m.Category != "Core" && (
                m.VisibilityLevels.Contains("area") || assignedModuleIds.Contains(m.Id)))
            .OrderBy(m => m.SortOrder)
            .Select(m => ToDto(m))
            .ToListAsync();
    }

    /// <summary>SchoolAdmin: feature modules installed in their school (excludes Core category).</summary>
    private async Task<List<UserMenuItemDto>> GetSchoolInstalledModules(string schoolCode)
    {
        return await _db.SchoolModules
            .AsNoTracking()
            .Where(sm => sm.SchoolCode == schoolCode && sm.IsEnabled)
            .Include(sm => sm.Module)
            .Where(sm => sm.Module.IsEnabled && sm.Module.Category != "Core")
            .OrderBy(sm => sm.Module.SortOrder)
            .Select(sm => ToDto(sm.Module))
            .ToListAsync();
    }

    /// <summary>
    /// Teacher: school installed modules that are visible to teachers.
    /// For modules with AssignableToTeacher, only show if teacher is assigned.
    /// </summary>
    private async Task<List<UserMenuItemDto>> GetTeacherModules(int userId, string schoolCode)
    {
        // Get personnel ID for this user
        var personnelId = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.Personnel != null)
            .Select(u => u.Personnel!.Id)
            .FirstOrDefaultAsync();

        // Get teacher's assigned module IDs
        var assignedSchoolModuleIds = personnelId > 0
            ? await _db.Set<TeacherModuleAssignment>()
                .Where(ta => ta.TeacherId == personnelId && ta.IsActive)
                .Select(ta => ta.SchoolModuleId)
                .ToListAsync()
            : new List<int>();

        // School feature modules visible to teacher (Core category excluded)
        var schoolModules = await _db.SchoolModules
            .AsNoTracking()
            .Where(sm => sm.SchoolCode == schoolCode && sm.IsEnabled)
            .Include(sm => sm.Module)
            .Where(sm => sm.Module.IsEnabled && sm.Module.Category != "Core" && sm.Module.VisibilityLevels.Contains("teacher"))
            .OrderBy(sm => sm.Module.SortOrder)
            .ToListAsync();

        // Filter: if module requires teacher assignment, check assignment
        var result = schoolModules
            .Where(sm => !sm.Module.AssignableToTeacher || assignedSchoolModuleIds.Contains(sm.Id))
            .Select(sm => ToDto(sm.Module))
            .ToList();

        return result;
    }

    /// <summary>
    /// Student: school installed modules visible to students.
    /// For modules with AssignableToStudent, only show if student is assigned.
    /// </summary>
    private async Task<List<UserMenuItemDto>> GetStudentModules(int userId, string schoolCode)
    {
        // Get student profile ID — students might have a StudentProfile linked via UserRole scope
        var studentProfileId = await _db.Set<UserRole>()
            .Where(ur => ur.UserId == userId && ur.Role.Name == "Student")
            .Select(ur => ur.ScopeId)
            .FirstOrDefaultAsync() ?? 0;

        // Get student's assigned module IDs
        var assignedSchoolModuleIds = studentProfileId > 0
            ? await _db.Set<StudentModuleAssignment>()
                .Where(sa => sa.StudentId == studentProfileId && sa.IsActive)
                .Select(sa => sa.SchoolModuleId)
                .ToListAsync()
            : new List<int>();

        // School feature modules visible to student (Core category excluded)
        var schoolModules = await _db.SchoolModules
            .AsNoTracking()
            .Where(sm => sm.SchoolCode == schoolCode && sm.IsEnabled)
            .Include(sm => sm.Module)
            .Where(sm => sm.Module.IsEnabled && sm.Module.Category != "Core" && sm.Module.VisibilityLevels.Contains("student"))
            .OrderBy(sm => sm.Module.SortOrder)
            .ToListAsync();

        // Filter: if module requires student assignment, check assignment
        var result = schoolModules
            .Where(sm => !sm.Module.AssignableToStudent || assignedSchoolModuleIds.Contains(sm.Id))
            .Select(sm => ToDto(sm.Module))
            .ToList();

        return result;
    }

    // -------------------------------------------------------------------------
    // DTO mapping
    // -------------------------------------------------------------------------

    private static UserMenuItemDto ToDto(Module m) => new(
        m.Code,
        m.Name,
        m.Icon ?? "fas fa-cube",
        m.RoutePath ?? m.Code,
        m.SortOrder,
        m.VisibilityLevels
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray()
    );
}

public record UserMenuItemDto(
    string Code,
    string Name,
    string Icon,
    string RoutePath,
    int SortOrder,
    string[] VisibilityLevels
);
