using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// CRUD for AreaPermissionPolicy rows — area-wide and per-school overrides.
///
/// Area-wide (SchoolId = null):
///   GET  /api/v1/areas/{areaId}/permission-policies              — list area-wide policies
///   PUT  /api/v1/areas/{areaId}/permission-policies/{code}       — upsert area-wide policy
///
/// Per-school overrides:
///   GET  /api/v1/areas/{areaId}/permission-policies/schools-summary?code=   — all schools + their effective status
///   PUT  /api/v1/areas/{areaId}/permission-policies/{code}/schools/{schoolId} — upsert school-specific override
///   DELETE /api/v1/areas/{areaId}/permission-policies/{code}/schools/{schoolId} — remove override (revert to area default)
/// </summary>
[ApiController]
[Route("api/v1/areas/{areaId:int}/permission-policies")]
[Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
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
    /// <summary>Returns all area-wide policies (SchoolId IS NULL) for this area.</summary>
    [HttpGet]
    public async Task<IActionResult> GetPolicies(int areaId, CancellationToken ct)
    {
        var policies = await db.AreaPermissionPolicies
            .AsNoTracking()
            .Where(p => p.AreaId == areaId && p.SchoolId == null)
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
    /// <summary>Upsert area-wide policy (SchoolId = null).</summary>
    [HttpPut("{code}")]
    public async Task<IActionResult> UpsertAreaPolicy(
        int areaId,
        string code,
        [FromBody] TogglePolicyRequest request,
        CancellationToken ct)
    {
        var policy = await db.AreaPermissionPolicies
            .FirstOrDefaultAsync(p => p.AreaId == areaId && p.SchoolId == null && p.PermissionCode == code, ct);

        if (policy is null)
        {
            policy = new AreaPermissionPolicy
            {
                AreaId           = areaId,
                SchoolId         = null,
                PermissionCode   = code,
                AllowSchoolAdmin = request.AllowSchoolAdmin,
                Description      = request.Description,
                UpdatedAt        = DateTimeOffset.UtcNow,
                UpdatedBy        = ActorUserId,
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
        logger.LogInformation("Area policy {Code} for area {AreaId} → AllowSchoolAdmin={Allow} by {Actor}",
            code, areaId, request.AllowSchoolAdmin, ActorUserId);

        return Ok(new { areaId, permissionCode = code, allowSchoolAdmin = policy.AllowSchoolAdmin });
    }

    // ── GET /api/v1/areas/{areaId}/permission-policies/schools-summary ────────
    /// <summary>
    /// Returns each school in the area with its effective AllowSchoolAdmin for the given code.
    /// Precedence: school-specific override → area-wide policy → default (true).
    /// </summary>
    [HttpGet("schools-summary")]
    public async Task<IActionResult> GetSchoolsSummary(
        int areaId,
        [FromQuery] string code = "user.manage_school_users",
        CancellationToken ct = default)
    {
        var schools = await db.Schools
            .AsNoTracking()
            .Where(s => s.AreaId == areaId)
            .OrderBy(s => s.NameTh)
            .Select(s => new { s.Id, s.NameTh, s.SchoolCode })
            .ToListAsync(ct);

        // Load all relevant policies for this area + code in one query
        var policies = await db.AreaPermissionPolicies
            .AsNoTracking()
            .Where(p => p.AreaId == areaId && p.PermissionCode == code)
            .ToListAsync(ct);

        var areaDefault = policies.FirstOrDefault(p => p.SchoolId == null)?.AllowSchoolAdmin ?? true;
        var schoolOverrides = policies
            .Where(p => p.SchoolId != null)
            .ToDictionary(p => p.SchoolId!.Value, p => p.AllowSchoolAdmin);

        var result = schools.Select(s =>
        {
            var isOverridden = schoolOverrides.TryGetValue(s.Id, out var schoolAllow);
            return new
            {
                schoolId         = s.Id,
                schoolName       = s.NameTh,
                schoolCode       = s.SchoolCode,
                allowSchoolAdmin = isOverridden ? schoolAllow : areaDefault,
                isOverridden,
            };
        });

        return Ok(result);
    }

    // ── PUT /api/v1/areas/{areaId}/permission-policies/{code}/schools/{schoolId}
    /// <summary>Upsert a school-specific policy override.</summary>
    [HttpPut("{code}/schools/{schoolId:int}")]
    public async Task<IActionResult> UpsertSchoolPolicy(
        int areaId,
        string code,
        int schoolId,
        [FromBody] TogglePolicyRequest request,
        CancellationToken ct)
    {
        // Validate school belongs to this area
        var schoolExists = await db.Schools
            .AnyAsync(s => s.Id == schoolId && s.AreaId == areaId, ct);
        if (!schoolExists) return NotFound(new { error = "ไม่พบโรงเรียนในเขตนี้" });

        var policy = await db.AreaPermissionPolicies
            .FirstOrDefaultAsync(p =>
                p.AreaId == areaId && p.SchoolId == schoolId && p.PermissionCode == code, ct);

        if (policy is null)
        {
            policy = new AreaPermissionPolicy
            {
                AreaId           = areaId,
                SchoolId         = schoolId,
                PermissionCode   = code,
                AllowSchoolAdmin = request.AllowSchoolAdmin,
                Description      = request.Description,
                UpdatedAt        = DateTimeOffset.UtcNow,
                UpdatedBy        = ActorUserId,
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
            "School policy {Code} for school {SchoolId} (area {AreaId}) → AllowSchoolAdmin={Allow} by {Actor}",
            code, schoolId, areaId, request.AllowSchoolAdmin, ActorUserId);

        return Ok(new { areaId, schoolId, permissionCode = code, allowSchoolAdmin = policy.AllowSchoolAdmin });
    }

    // ── DELETE /api/v1/areas/{areaId}/permission-policies/{code}/schools/{schoolId}
    /// <summary>Remove a school-specific override (school reverts to area-wide default).</summary>
    [HttpDelete("{code}/schools/{schoolId:int}")]
    public async Task<IActionResult> DeleteSchoolPolicy(
        int areaId,
        string code,
        int schoolId,
        CancellationToken ct)
    {
        var policy = await db.AreaPermissionPolicies
            .FirstOrDefaultAsync(p =>
                p.AreaId == areaId && p.SchoolId == schoolId && p.PermissionCode == code, ct);

        if (policy is null) return NoContent(); // idempotent

        db.AreaPermissionPolicies.Remove(policy);
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "School policy {Code} for school {SchoolId} (area {AreaId}) removed by {Actor}",
            code, schoolId, areaId, ActorUserId);

        return NoContent();
    }
}

public record TogglePolicyRequest(bool AllowSchoolAdmin, string? Description = null);
