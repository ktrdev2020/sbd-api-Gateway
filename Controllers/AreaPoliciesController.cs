using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// CRUD for AreaPermissionPolicy rows.
/// Phase C.6 — lets SuperAdmin and AreaAdmin toggle self-edit policies without SQL.
///
/// GET  /api/v1/areas/{id}/permission-policies         — list all policies for area
/// PUT  /api/v1/areas/{id}/permission-policies/{code}  — toggle AllowSchoolAdmin
/// POST /api/v1/areas/{id}/permission-policies          — upsert a policy row
/// </summary>
[ApiController]
[Route("api/v1/areas/{areaId:int}/permission-policies")]
[Authorize]
public class AreaPoliciesController(
    SbdDbContext db,
    ILogger<AreaPoliciesController> logger) : ControllerBase
{
    private int? ActorUserId
    {
        get
        {
            var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return v != null && int.TryParse(v, out var id) ? id : null;
        }
    }

    // ── GET /api/v1/areas/{areaId}/permission-policies ────────────────────────
    [HttpGet]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> GetPolicies(int areaId, CancellationToken ct)
    {
        var policies = await db.AreaPermissionPolicies
            .AsNoTracking()
            .Where(p => p.AreaId == areaId)
            .OrderBy(p => p.PermissionCode)
            .Select(p => new
            {
                p.Id,
                p.AreaId,
                p.PermissionCode,
                p.AllowSchoolAdmin,
                p.Description,
                p.UpdatedAt,
                p.UpdatedBy,
            })
            .ToListAsync(ct);

        return Ok(policies);
    }

    // ── PUT /api/v1/areas/{areaId}/permission-policies/{code} ─────────────────
    [HttpPut("{code}")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> TogglePolicy(
        int areaId,
        string code,
        [FromBody] TogglePolicyRequest request,
        CancellationToken ct)
    {
        var policy = await db.AreaPermissionPolicies
            .FirstOrDefaultAsync(p => p.AreaId == areaId && p.PermissionCode == code, ct);

        if (policy is null)
        {
            // Auto-create if not seeded yet
            policy = new AreaPermissionPolicy
            {
                AreaId          = areaId,
                PermissionCode  = code,
                AllowSchoolAdmin = request.AllowSchoolAdmin,
                Description     = request.Description,
                UpdatedAt       = DateTimeOffset.UtcNow,
                UpdatedBy       = ActorUserId,
            };
            db.AreaPermissionPolicies.Add(policy);
        }
        else
        {
            policy.AllowSchoolAdmin = request.AllowSchoolAdmin;
            if (request.Description != null) policy.Description = request.Description;
            policy.UpdatedAt = DateTimeOffset.UtcNow;
            policy.UpdatedBy = ActorUserId;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Policy {Code} for area {AreaId} set to AllowSchoolAdmin={Allow} by actor {Actor}",
            code, areaId, request.AllowSchoolAdmin, ActorUserId);

        return Ok(new { areaId, permissionCode = code, allowSchoolAdmin = policy.AllowSchoolAdmin });
    }
}

public record TogglePolicyRequest(bool AllowSchoolAdmin, string? Description = null);
