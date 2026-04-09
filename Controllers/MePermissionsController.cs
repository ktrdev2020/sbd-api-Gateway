using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Application.DTOs;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Read-only "what am I allowed to do" snapshot for /settings/permissions.
/// Phase A.2.5 — built from existing UserRole + module assignments + Area
/// Permission Policies. When AuthorityService ships in Phase B, this DTO
/// gains a Capabilities[] field populated from CapabilityGrant records.
///
/// See docs/architecture/SBD-AUTHORITY-SYSTEM.md
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MePermissionsController : ControllerBase
{
    private readonly SbdDbContext _context;
    private readonly ILogger<MePermissionsController> _logger;

    public MePermissionsController(SbdDbContext context, ILogger<MePermissionsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private int? CurrentUserId
    {
        get
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst("sub")?.Value;
            return claim != null && int.TryParse(claim, out var id) ? id : null;
        }
    }

    [HttpGet("permissions")]
    public async Task<ActionResult<PermissionsMeDto>> GetMyPermissions(CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId == null) return Unauthorized();

        // Load roles + scopes
        var userRoles = await _context.UserRoles
            .AsNoTracking()
            .Include(ur => ur.Role)
            .Where(ur => ur.UserId == userId)
            .ToListAsync(ct);

        if (userRoles.Count == 0)
        {
            return Ok(new PermissionsMeDto
            {
                ActiveRole = "Student",
                Roles = new(),
                Modules = new(),
                SelfEditPolicies = new(),
            });
        }

        // Resolve scope display names
        var areaIds = userRoles
            .Where(ur => ur.ScopeType == "Area" && ur.ScopeId.HasValue)
            .Select(ur => ur.ScopeId!.Value)
            .Distinct()
            .ToList();
        var schoolIds = userRoles
            .Where(ur => ur.ScopeType == "School" && ur.ScopeId.HasValue)
            .Select(ur => ur.ScopeId!.Value)
            .Distinct()
            .ToList();

        var areaNames = await _context.Areas
            .AsNoTracking()
            .Where(a => areaIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.NameTh, ct);
        var schoolNames = await _context.Schools
            .AsNoTracking()
            .Where(s => schoolIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.NameTh, ct);

        var roleAssignments = userRoles.Select(ur => new RoleAssignmentDto
        {
            RoleCode = ur.Role.Code,
            RoleName = ur.Role.Name,
            ScopeType = ur.ScopeType,
            ScopeId = ur.ScopeId,
            ScopeName = ur.ScopeType switch
            {
                "Area" when ur.ScopeId.HasValue && areaNames.ContainsKey(ur.ScopeId.Value)
                    => areaNames[ur.ScopeId.Value],
                "School" when ur.ScopeId.HasValue && schoolNames.ContainsKey(ur.ScopeId.Value)
                    => schoolNames[ur.ScopeId.Value],
                _ => null,
            },
            AssignedAt = ur.AssignedAt,
            AssignedBy = ur.AssignedBy,
        }).ToList();

        // Active role = first role's name (matches AuthService convention)
        var activeRole = userRoles[0].Role.Name;

        // Resolve modules visible to user (cascaded Area → School → Teacher/Student)
        var schoolId = userRoles
            .Where(ur => ur.ScopeType == "School" && ur.ScopeId.HasValue)
            .Select(ur => ur.ScopeId)
            .FirstOrDefault();
        var areaId = userRoles
            .Where(ur => ur.ScopeType == "Area" && ur.ScopeId.HasValue)
            .Select(ur => ur.ScopeId)
            .FirstOrDefault();

        var modules = new List<ModuleAccessDto>();

        if (schoolId.HasValue)
        {
            var schoolModules = await _context.SchoolModules
                .AsNoTracking()
                .Include(sm => sm.Module)
                .Where(sm => sm.SchoolId == schoolId.Value && sm.IsEnabled && sm.Module.IsEnabled)
                .ToListAsync(ct);
            modules.AddRange(schoolModules.Select(sm => new ModuleAccessDto
            {
                Code = sm.Module.Code,
                Name = sm.Module.Name,
                Icon = sm.Module.Icon,
                Category = sm.Module.Category,
                IsEnabled = sm.IsEnabled,
                Source = "school",
            }));
        }
        else if (areaId.HasValue)
        {
            var areaModules = await _context.AreaModuleAssignments
                .AsNoTracking()
                .Include(am => am.Module)
                .Where(am => am.AreaId == areaId.Value && am.IsEnabled && am.Module.IsEnabled)
                .ToListAsync(ct);
            modules.AddRange(areaModules.Select(am => new ModuleAccessDto
            {
                Code = am.Module.Code,
                Name = am.Module.Name,
                Icon = am.Module.Icon,
                Category = am.Module.Category,
                IsEnabled = am.IsEnabled,
                Source = "area",
            }));
        }

        // Always-available system modules (e.g. dashboard, settings)
        var systemModules = await _context.Modules
            .AsNoTracking()
            .Where(m => m.IsDefault && m.IsEnabled)
            .ToListAsync(ct);
        foreach (var sm in systemModules)
        {
            if (modules.Any(m => m.Code == sm.Code)) continue;
            modules.Add(new ModuleAccessDto
            {
                Code = sm.Code,
                Name = sm.Name,
                Icon = sm.Icon,
                Category = sm.Category,
                IsEnabled = sm.IsEnabled,
                Source = "system",
            });
        }

        // Self-edit policies — read for the user's area (if any)
        var policies = new Dictionary<string, bool>();
        if (areaId.HasValue || schoolId.HasValue)
        {
            // For school users, resolve their school's area
            int? resolvedAreaId = areaId;
            if (resolvedAreaId == null && schoolId.HasValue)
            {
                resolvedAreaId = await _context.Schools
                    .AsNoTracking()
                    .Where(s => s.Id == schoolId.Value)
                    .Select(s => (int?)s.AreaId)
                    .FirstOrDefaultAsync(ct);
            }

            if (resolvedAreaId.HasValue)
            {
                policies = await _context.AreaPermissionPolicies
                    .AsNoTracking()
                    .Where(p => p.AreaId == resolvedAreaId.Value)
                    .ToDictionaryAsync(p => p.PermissionCode, p => p.AllowSchoolAdmin, ct);
            }
        }

        return Ok(new PermissionsMeDto
        {
            ActiveRole = activeRole,
            Roles = roleAssignments,
            Modules = modules.OrderBy(m => m.Code).ToList(),
            SelfEditPolicies = policies,
        });
    }
}
