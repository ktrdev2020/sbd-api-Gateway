using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;
using SBD.Messaging.Events;

namespace Gateway.Controllers;

/// <summary>
/// Admin endpoints to link/unlink a User account to a Personnel/AreaPersonnel/Student record.
/// SuperAdmin only — this is a manual override for cases not covered by automated linking
/// (e.g. imported data, missing PersonnelLinkedToUserEvent, or cross-context reassignment).
///
/// Phase C.4 — SBD Authority System.
/// See docs/architecture/SBD-AUTHORITY-SYSTEM.md
/// </summary>
[ApiController]
[Route("api/v1/admin/users")]
[Authorize(Roles = "super_admin,SuperAdmin")]
public class UserPersonnelLinkController(
    SbdDbContext db,
    IPublishEndpoint publish,
    ILogger<UserPersonnelLinkController> logger) : ControllerBase
{
    private int ActorUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("No user id in token"));

    // ── PATCH /api/v1/admin/users/{id}/link-personnel ──────────────────────
    /// <summary>
    /// Link (or re-link) a user account to a personnel record.
    /// Publishes <see cref="PersonnelLinkedToUserEvent"/> consumed by AuthService
    /// and other interested services.
    /// </summary>
    [HttpPatch("{id:int}/link-personnel")]
    public async Task<IActionResult> LinkPersonnel(
        int id,
        [FromBody] LinkPersonnelRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.PersonnelContext) ||
            !new[] { "school", "area", "student" }.Contains(request.PersonnelContext))
            return BadRequest(new { error = "PersonnelContext must be 'school', 'area', or 'student'." });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound(new { error = "User not found." });

        var previousContext = user.PersonnelContext;
        var previousRefId   = user.PersonnelRefId;

        user.PersonnelContext = request.PersonnelContext;
        user.PersonnelRefId   = request.PersonnelRefId;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "User {UserId} linked to {Context}:{RefId} by actor {ActorId} (was {PrevContext}:{PrevRefId})",
            id, request.PersonnelContext, request.PersonnelRefId, ActorUserId, previousContext, previousRefId);

        await publish.Publish(new PersonnelLinkedToUserEvent(
            UserId:          id,
            PersonnelContext: request.PersonnelContext,
            PersonnelRefId:  request.PersonnelRefId,
            LinkedByUserId:  ActorUserId,
            OccurredAt:      DateTimeOffset.UtcNow), ct);

        return Ok(new
        {
            userId           = id,
            personnelContext = user.PersonnelContext,
            personnelRefId   = user.PersonnelRefId,
        });
    }

    // ── DELETE /api/v1/admin/users/{id}/link-personnel ─────────────────────
    /// <summary>
    /// Unlink a user from their current personnel record.
    /// Publishes <see cref="PersonnelUnlinkedFromUserEvent"/>.
    /// </summary>
    [HttpDelete("{id:int}/link-personnel")]
    public async Task<IActionResult> UnlinkPersonnel(
        int id,
        [FromBody] UnlinkPersonnelRequest request,
        CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound(new { error = "User not found." });

        if (user.PersonnelContext is null || user.PersonnelRefId is null)
            return BadRequest(new { error = "User has no linked personnel record." });

        var context = user.PersonnelContext;
        var refId   = user.PersonnelRefId.Value;

        user.PersonnelContext = null;
        user.PersonnelRefId   = null;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "User {UserId} unlinked from {Context}:{RefId} by actor {ActorId}",
            id, context, refId, ActorUserId);

        await publish.Publish(new PersonnelUnlinkedFromUserEvent(
            UserId:          id,
            PersonnelContext: context,
            PersonnelRefId:  refId,
            UnlinkedByUserId: ActorUserId,
            Reason:          request.Reason,
            OccurredAt:      DateTimeOffset.UtcNow), ct);

        return Ok(new { userId = id, unlinked = true });
    }

    // ── GET /api/v1/admin/users/{id}/link-personnel ────────────────────────
    [HttpGet("{id:int}/link-personnel")]
    public async Task<IActionResult> GetLink(int id, CancellationToken ct = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new { u.Id, u.Username, u.DisplayName, u.PersonnelContext, u.PersonnelRefId })
            .FirstOrDefaultAsync(ct);

        if (user is null) return NotFound(new { error = "User not found." });
        return Ok(user);
    }
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public record LinkPersonnelRequest(string PersonnelContext, int PersonnelRefId);
public record UnlinkPersonnelRequest(string? Reason);
