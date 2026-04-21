using System.Security.Claims;
using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Application.DTOs;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Read-only "what am I allowed to do" snapshot for /settings/permissions.
/// Phase A.2.5: UserRole + module assignments + Area Permission Policies.
/// Phase B.4: now includes active CapabilityGrants from the HCD system.
///
/// See docs/architecture/SBD-AUTHORITY-SYSTEM.md
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MePermissionsController : ControllerBase
{
    private readonly SbdDbContext _context;
    private readonly ICapabilityService _capabilities;
    private readonly ILogger<MePermissionsController> _logger;

    public MePermissionsController(
        SbdDbContext context,
        ICapabilityService capabilities,
        ILogger<MePermissionsController> logger)
    {
        _context = context;
        _capabilities = capabilities;
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

        // Read cap_v from JWT — used as Redis cache key for grants (Phase B.3/B.4)
        var capV = long.TryParse(User.FindFirstValue("cap_v"), out var v) ? v : 0L;

        // Load roles + scopes
        var userRoles = await _context.UserRoles
            .AsNoTracking()
            .Include(ur => ur.Role)
            .Where(ur => ur.UserId == userId)
            .ToListAsync(ct);

        if (userRoles.Count == 0)
        {
            var emptyGrants = await _capabilities.GetActiveGrantsAsync(userId.Value, capV, ct);
            return Ok(new PermissionsMeDto
            {
                ActiveRole = "Student",
                Roles = new(),
                Modules = new(),
                SelfEditPolicies = new(),
                Grants = emptyGrants.ToList(),
                CapVersion = capV,
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
        // Option A: polymorphic int ScopeId → string SchoolCode match
        var schoolCodesStr = schoolIds.Select(i => i.ToString()).ToList();
        var schoolNamesByCode = await _context.Schools
            .AsNoTracking()
            .Where(s => schoolCodesStr.Contains(s.SchoolCode))
            .ToDictionaryAsync(s => s.SchoolCode, s => s.NameTh, ct);
        var schoolNames = schoolNamesByCode
            .Where(kv => int.TryParse(kv.Key, out _))
            .ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);

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

        // Option A: polymorphic int ScopeId → string SchoolCode
        var schoolCode = schoolId.HasValue ? schoolId.Value.ToString() : null;
        if (!string.IsNullOrEmpty(schoolCode))
        {
            var schoolModules = await _context.SchoolModules
                .AsNoTracking()
                .Include(sm => sm.Module)
                .Where(sm => sm.SchoolCode == schoolCode && sm.IsEnabled && sm.Module.IsEnabled)
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
            if (resolvedAreaId == null && !string.IsNullOrEmpty(schoolCode))
            {
                resolvedAreaId = await _context.Schools
                    .AsNoTracking()
                    .Where(s => s.SchoolCode == schoolCode)
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

        var grants = await _capabilities.GetActiveGrantsAsync(userId.Value, capV, ct);

        // Phase C.3: load active functional assignments
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var functionalAssignments = await _context.FunctionalAssignments
            .AsNoTracking()
            .Include(a => a.FunctionalRoleType)
            .Where(a => a.UserId == userId.Value
                     && a.RevokedAt == null
                     && (a.EndDate == null || a.EndDate >= today))
            .ToListAsync(ct);

        // Resolve scope display names for assignments
        var schoolScopeIds = functionalAssignments
            .Where(a => a.ContextScopeType == "School")
            .Select(a => a.ContextScopeId).Distinct().ToList();
        // Option A: polymorphic int ScopeId → string SchoolCode
        var faSchoolCodes = schoolScopeIds.Select(i => i.ToString()).ToList();
        var faSchoolByCode = schoolScopeIds.Count > 0
            ? await _context.Schools.AsNoTracking()
                .Where(s => faSchoolCodes.Contains(s.SchoolCode))
                .ToDictionaryAsync(s => s.SchoolCode, s => s.NameTh, ct)
            : new Dictionary<string, string>();
        var faSchoolNames = faSchoolByCode
            .Where(kv => int.TryParse(kv.Key, out _))
            .ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);

        var functionalDtos = functionalAssignments.Select(a =>
        {
            var scopeName = a.ContextScopeType == "School" && faSchoolNames.TryGetValue(a.ContextScopeId, out var sn)
                ? sn : null;
            var caps = a.FunctionalRoleType?.GrantedCapabilitiesJson != null
                ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(a.FunctionalRoleType.GrantedCapabilitiesJson) ?? new()
                : new List<string>();
            return new FunctionalAssignmentDto
            {
                Id               = a.Id,
                RoleCode         = a.FunctionalRoleType?.Code ?? "",
                RoleNameTh       = a.FunctionalRoleType?.NameTh ?? "",
                Category         = a.FunctionalRoleType?.Category ?? "",
                ContextScope     = a.FunctionalRoleType?.ContextScope ?? "",
                ContextScopeType = a.ContextScopeType,
                ContextScopeId   = a.ContextScopeId,
                ContextScopeName = scopeName,
                StartDate        = a.StartDate,
                EndDate          = a.EndDate,
                AssignedByUserId = a.AssignedByUserId,
                AssignedAt       = a.AssignedAt,
                OrderRef         = a.OrderRef,
                GrantedCapabilities = caps,
            };
        }).ToList();

        return Ok(new PermissionsMeDto
        {
            ActiveRole            = activeRole,
            Roles                 = roleAssignments,
            Modules               = modules.OrderBy(m => m.Code).ToList(),
            SelfEditPolicies      = policies,
            Grants                = grants.ToList(),
            CapVersion            = capV,
            FunctionalAssignments = functionalDtos,
        });
    }
}
