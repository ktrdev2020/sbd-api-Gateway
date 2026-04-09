using SBD.Application.DTOs;

namespace Gateway.Services;

/// <summary>
/// Per-request capability lookup for the Gateway.
///
/// Reads from the shared <c>authz_capability_grants</c> table (via SbdDbContext)
/// with a Redis cache keyed on <c>authz:user_grants:{userId}:{capVersion}</c>.
///
/// The <c>capVersion</c> comes from the JWT <c>cap_v</c> claim and is bumped
/// whenever AuthorityService grants/revokes a capability (Phase B.3).
/// When the JWT carries an outdated <c>cap_v</c>, the caller receives the
/// cached snapshot from when the token was issued — this is intentional and
/// matches the same "cached until next refresh" model used by <c>roles_v</c>.
/// The grant list is refreshed on the next token refresh.
/// </summary>
public interface ICapabilityService
{
    /// <summary>
    /// Returns all active (non-expired, non-revoked) grants for a user,
    /// enriched with the capability display name from the catalog.
    /// Uses Redis cache: key = <c>authz:user_grants:{userId}:{capVersion}</c>, TTL 10 min.
    /// </summary>
    Task<IReadOnlyList<CapabilityGrantDto>> GetActiveGrantsAsync(
        int userId,
        long capVersion,
        CancellationToken ct = default);

    /// <summary>
    /// Returns true if the user has an active grant for <paramref name="code"/>
    /// that covers the requested scope (exact match or broader).
    /// </summary>
    Task<bool> HasCapabilityAsync(
        int userId,
        long capVersion,
        string code,
        string? scopeType = null,
        int? scopeId = null,
        CancellationToken ct = default);
}
