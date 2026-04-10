using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;
using Gateway.Filters;

namespace Gateway.Controllers;

/// <summary>
/// Phase D authority enterprise endpoints implemented directly in Gateway.
/// Reads/writes JitElevationRequest, RecertificationCampaign, UserRiskScore
/// via SbdDbContext — no AuthorityService proxy needed.
/// </summary>
[ApiController]
[Authorize]
public class AuthorityEnterpriseController(SbdDbContext db) : ControllerBase
{
    private int? CurrentUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(v, out var id) ? id : null;
    }

    // ── JIT Elevations ───────────────────────────────────────────────────────

    /// <summary>GET /api/v1/elevations/me — active JIT elevations for the caller.</summary>
    [HttpGet("api/v1/elevations/me")]
    public async Task<IActionResult> GetMyElevations(CancellationToken ct = default)
    {
        var uid = CurrentUserId();
        if (uid is null) return Unauthorized();

        var now = DateTimeOffset.UtcNow;
        var rows = await db.JitElevations
            .Where(e => e.UserId == uid && e.RevokedAt == null && e.ExpiresAt > now)
            .OrderByDescending(e => e.GrantedAt)
            .Select(e => new {
                e.Id,
                e.CapabilityCode,
                e.ScopeType,
                e.ScopeId,
                e.Reason,
                e.GrantedAt,
                e.ExpiresAt,
                remainingMinutes = (int)(e.ExpiresAt - now).TotalMinutes
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>POST /api/v1/elevations — request a JIT elevation.</summary>
    [HttpPost("api/v1/elevations")]
    public async Task<IActionResult> RequestElevation(
        [FromBody] JitElevationRequestDto body, CancellationToken ct = default)
    {
        var uid = CurrentUserId();
        if (uid is null) return Unauthorized();

        var hours = Math.Clamp(body.DurationHours, 1, 8);
        var now = DateTimeOffset.UtcNow;

        var elevation = new JitElevationRequest
        {
            UserId = uid.Value,
            CapabilityCode = body.CapabilityCode,
            ScopeType = body.ScopeType ?? "system",
            ScopeId = body.ScopeId,
            GrantedByUserId = uid.Value,
            Reason = body.Reason,
            GrantedAt = now,
            ExpiresAt = now.AddHours(hours)
        };

        db.JitElevations.Add(elevation);
        await db.SaveChangesAsync(ct);

        return Ok(new {
            elevation.Id,
            elevation.CapabilityCode,
            elevation.ScopeType,
            elevation.ScopeId,
            elevation.Reason,
            elevation.GrantedAt,
            elevation.ExpiresAt,
            remainingMinutes = hours * 60
        });
    }

    /// <summary>DELETE /api/v1/elevations/{id} — revoke a JIT elevation early.</summary>
    [HttpDelete("api/v1/elevations/{id:long}")]
    public async Task<IActionResult> RevokeElevation(long id, CancellationToken ct = default)
    {
        var uid = CurrentUserId();
        if (uid is null) return Unauthorized();

        var row = await db.JitElevations
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == uid && e.RevokedAt == null, ct);

        if (row is null) return NotFound();

        row.RevokedAt = DateTimeOffset.UtcNow;
        row.RevokedByUserId = uid;
        row.RevokeReason = "manual";
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ── Break-Glass ──────────────────────────────────────────────────────────

    /// <summary>POST /api/v1/break-glass — activate break-glass (SuperAdmin only, fresh JWT required).</summary>
    [HttpPost("api/v1/break-glass")]
    [Authorize(Roles = "super_admin,SuperAdmin")]
    [RequireStepUp]
    public IActionResult ActivateBreakGlass([FromBody] BreakGlassDto body)
    {
        // Break-glass broadcast is handled via SignalR (RealtimeService).
        // This endpoint acknowledges the intent; actual SignalR push is out-of-band.
        return Ok(new { activated = true, reason = body.Reason, activatedAt = DateTimeOffset.UtcNow });
    }

    /// <summary>DELETE /api/v1/break-glass — deactivate break-glass (SuperAdmin only).</summary>
    [HttpDelete("api/v1/break-glass")]
    [Authorize(Roles = "super_admin,SuperAdmin")]
    public IActionResult DeactivateBreakGlass()
    {
        return Ok(new { deactivated = true, deactivatedAt = DateTimeOffset.UtcNow });
    }

    // ── Recertification ──────────────────────────────────────────────────────

    /// <summary>GET /api/v1/recertification/campaigns — active campaigns with pending item counts.</summary>
    [HttpGet("api/v1/recertification/campaigns")]
    public async Task<IActionResult> GetCampaigns(CancellationToken ct = default)
    {
        var campaigns = await db.RecertificationCampaigns
            .Where(c => c.Status == "active")
            .OrderByDescending(c => c.StartedAt)
            .Select(c => new {
                c.Id,
                c.Name,
                c.StartedAt,
                c.Deadline,
                c.Status,
                totalItems   = db.RecertificationItems.Count(i => i.CampaignId == c.Id),
                pendingItems = db.RecertificationItems.Count(i => i.CampaignId == c.Id && i.Status == "pending")
            })
            .ToListAsync(ct);

        return Ok(campaigns);
    }

    /// <summary>POST /api/v1/recertification/campaigns — start a new campaign (admin).</summary>
    [HttpPost("api/v1/recertification/campaigns")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> StartCampaign(
        [FromBody] StartCampaignDto body, CancellationToken ct = default)
    {
        var uid = CurrentUserId();
        if (uid is null) return Unauthorized();

        var campaign = new RecertificationCampaign
        {
            Name      = body.Name,
            StartedBy = uid.Value,
            Deadline  = body.Deadline,
            Status    = "active"
        };

        db.RecertificationCampaigns.Add(campaign);
        await db.SaveChangesAsync(ct);
        return Ok(campaign);
    }

    /// <summary>GET /api/v1/recertification/campaigns/{id}/items.</summary>
    [HttpGet("api/v1/recertification/campaigns/{id:long}/items")]
    public async Task<IActionResult> GetCampaignItems(long id, CancellationToken ct = default)
    {
        var items = await db.RecertificationItems
            .Where(i => i.CampaignId == id)
            .OrderBy(i => i.Status)
            .Select(i => new {
                i.Id,
                i.CampaignId,
                i.GrantId,
                i.GranteeUserId,
                i.ReviewerUserId,
                i.Status,
                i.ReviewedAt,
                i.ReviewNote
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>POST /api/v1/recertification/items/{id}/decide — approve or revoke a grant.</summary>
    [HttpPost("api/v1/recertification/items/{id:long}/decide")]
    public async Task<IActionResult> DecideItem(
        long id, [FromBody] DecideItemDto body, CancellationToken ct = default)
    {
        var uid = CurrentUserId();
        if (uid is null) return Unauthorized();

        var item = await db.RecertificationItems.FindAsync([id], ct);
        if (item is null) return NotFound();

        item.Status     = body.Decision == "recertify" ? "recertified" : "revoked";
        item.ReviewerUserId = uid;
        item.ReviewedAt = DateTimeOffset.UtcNow;
        item.ReviewNote = body.Note;
        await db.SaveChangesAsync(ct);

        return Ok(new { item.Id, item.Status, item.ReviewedAt });
    }

    // ── Risk Scoring ─────────────────────────────────────────────────────────

    /// <summary>GET /api/v1/risk/me — risk score for the caller.</summary>
    [HttpGet("api/v1/risk/me")]
    public async Task<IActionResult> GetMyRisk(CancellationToken ct = default)
    {
        var uid = CurrentUserId();
        if (uid is null) return Unauthorized();

        var score = await db.UserRiskScores.FindAsync([uid], ct);
        return Ok(score ?? new UserRiskScore { UserId = uid.Value, Score = 0, Level = "low" });
    }

    /// <summary>GET /api/v1/risk/summary — system-wide risk breakdown (admin).</summary>
    [HttpGet("api/v1/risk/summary")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> GetRiskSummary(CancellationToken ct = default)
    {
        var counts = await db.UserRiskScores
            .GroupBy(r => r.Level)
            .Select(g => new { level = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var top = await db.UserRiskScores
            .Where(r => r.Level == "critical" || r.Level == "high")
            .OrderByDescending(r => r.Score)
            .Take(10)
            .Join(db.Users, r => r.UserId, u => u.Id,
                (r, u) => new { u.Id, u.Username, u.DisplayName, r.Score, r.Level })
            .ToListAsync(ct);

        return Ok(new {
            critical = counts.FirstOrDefault(c => c.level == "critical")?.count ?? 0,
            high     = counts.FirstOrDefault(c => c.level == "high")?.count     ?? 0,
            medium   = counts.FirstOrDefault(c => c.level == "medium")?.count   ?? 0,
            low      = counts.FirstOrDefault(c => c.level == "low")?.count      ?? 0,
            topRisks = top
        });
    }

    // ── Compliance Reports ───────────────────────────────────────────────────

    /// <summary>GET /api/v1/reports/audit — paginated audit log.</summary>
    [HttpGet("api/v1/reports/audit")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> GetAuditLog(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var q = db.GrantAuditLogs.OrderByDescending(l => l.OccurredAt);
        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new { total, page, pageSize, items });
    }

    /// <summary>GET /api/v1/reports/grants/summary — grant distribution (admin).</summary>
    [HttpGet("api/v1/reports/grants/summary")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> GetGrantsSummary(CancellationToken ct = default)
    {
        var byScope = await db.CapabilityGrants
            .Where(g => g.RevokedAt == null)
            .GroupBy(g => g.ScopeType)
            .Select(g => new { scope = g.Key, count = g.Count() })
            .ToListAsync(ct);

        return Ok(new { activeGrants = byScope.Sum(x => x.count), byScope });
    }
}

// ── Request DTOs (local to controller, no shared contract needed) ────────────

public record JitElevationRequestDto(
    string CapabilityCode,
    string? ScopeType,
    int? ScopeId,
    string Reason,
    int DurationHours);

public record BreakGlassDto(string Reason);

public record StartCampaignDto(string Name, DateOnly Deadline);

public record DecideItemDto(string Decision, string? Note); // Decision: "recertify" | "revoke"
