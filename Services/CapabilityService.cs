using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SBD.Application.DTOs;
using SBD.Infrastructure.Data;
using StackExchange.Redis;

namespace Gateway.Services;

/// <inheritdoc />
public class CapabilityService(
    SbdDbContext db,
    IConnectionMultiplexer redis,
    ILogger<CapabilityService> logger) : ICapabilityService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static string CacheKey(int userId, long capVersion) =>
        $"authz:user_grants:{userId}:{capVersion}";

    public async Task<IReadOnlyList<CapabilityGrantDto>> GetActiveGrantsAsync(
        int userId,
        long capVersion,
        CancellationToken ct = default)
    {
        // 1. Try Redis cache first. Plan #54 — wire failure is treated as cache
        //    miss (caller falls through to DB) instead of bubbling up as 500.
        //    Plan #55 follow-up — IsConnected gate skips the SyncTimeout wait
        //    entirely when the multiplexer has no live connection.
        var redisDb = redis.GetDatabase();
        var cacheKey = CacheKey(userId, capVersion);
        RedisValue cached = RedisValue.Null;
        if (redis.IsConnected)
        {
            try
            {
                cached = await redisDb.StringGetAsync(cacheKey);
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                logger.LogWarning(ex, "Redis read failed for cap grants (user {UserId} cap_v={V}); falling back to DB", userId, capVersion);
                cached = RedisValue.Null;
            }
        }

        if (cached.HasValue)
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize<List<CapabilityGrantDto>>(
                    cached.ToString(), JsonOptions);
                if (deserialized != null) return deserialized;
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Corrupt grant cache for user {UserId} cap_v={V}, rebuilding", userId, capVersion);
                try { await redisDb.KeyDeleteAsync(cacheKey); }
                catch (Exception inner) when (inner is RedisException or TimeoutException) { /* swallow */ }
            }
        }

        // 2. Query from shared DB
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var grants = await db.CapabilityGrants
            .AsNoTracking()
            .Where(g => g.GranteeUserId == userId
                     && g.RevokedAt == null
                     && (g.ExpiresAt == null || g.ExpiresAt > today))
            .OrderBy(g => g.CapabilityCode)
            .ToListAsync(ct);

        // Enrich with display names from catalog (batch lookup)
        var codes = grants.Select(g => g.CapabilityCode).Distinct().ToList();
        var nameLookup = await db.CapabilityDefinitions
            .AsNoTracking()
            .Where(c => codes.Contains(c.Code))
            .ToDictionaryAsync(c => c.Code, c => c.NameTh, ct);

        var dtos = grants.Select(g => new CapabilityGrantDto
        {
            Id             = g.Id,
            Code           = g.CapabilityCode,
            NameTh         = nameLookup.GetValueOrDefault(g.CapabilityCode),
            ScopeType      = g.ScopeType,
            ScopeId        = g.ScopeId,
            GrantedByUserId = g.GrantedByUserId,
            ParentGrantId  = g.ParentGrantId,
            RemainingDepth = g.RemainingDepth,
            CanRedelegate  = g.CanRedelegate,
            ExpiresAt      = g.ExpiresAt,
            GrantedAt      = g.GrantedAt,
            OrderRef       = g.OrderRef,
        }).ToList();

        // 3. Write to Redis (skip when disconnected)
        if (redis.IsConnected)
        {
            try
            {
                var json = JsonSerializer.Serialize(dtos, JsonOptions);
                await redisDb.StringSetAsync(cacheKey, json, CacheTtl);
            }
            catch (Exception ex)
            {
                // Cache write failure is non-fatal — degrade gracefully
                logger.LogWarning(ex, "Failed to cache grants for user {UserId}", userId);
            }
        }

        return dtos;
    }

    public async Task<bool> HasCapabilityAsync(
        int userId,
        long capVersion,
        string code,
        string? scopeType = null,
        int? scopeId = null,
        CancellationToken ct = default)
    {
        var grants = await GetActiveGrantsAsync(userId, capVersion, ct);

        // Scope names are compared case-insensitively: the grants table stores
        // them lowercase ("school") while authz_functional_assignments stores
        // "School", and both feed this check.
        static bool ScopeCovers(string grantScopeType, int? grantScopeId, string? scopeType, int? scopeId)
        {
            if (scopeType == null) return true; // caller didn't specify scope — any match
            var from = grantScopeType.ToLowerInvariant();
            var to = scopeType.ToLowerInvariant();
            // Scope check: grant must be at least as broad as requested scope
            if (from == "global") return true;
            if (from == to && grantScopeId == scopeId) return true;
            if (from == "area" && to is "school" or "department" or "classroom" or "self")
                return true;
            if (from == "school" && to is "department" or "classroom" or "self")
                return true;
            return false;
        }

        if (grants.Any(g => g.Code == code && ScopeCovers(g.ScopeType, g.ScopeId, scopeType, scopeId)))
            return true;

        // Feedback id=57 — capabilities carried by a functional role are copied
        // into authz_capability_grants when the role is *assigned*, so adding a
        // capability to a role type later never reached the people already
        // holding it. Reading the role catalog directly makes the catalog the
        // source of truth it is meant to be: extend a role, and everyone who
        // holds it gains the capability on their next request.
        //
        // The frontend's capabilityGuard already unioned both sources, so before
        // this the route opened and the API then refused — the worst split.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var roleCaps = await (
            from fa in db.FunctionalAssignments.AsNoTracking()
            join rt in db.FunctionalRoleTypes.AsNoTracking() on fa.FunctionalRoleTypeId equals rt.Id
            where fa.UserId == userId
                  && fa.RevokedAt == null
                  && fa.StartDate <= today
                  && (fa.EndDate == null || fa.EndDate >= today)
                  && rt.IsActive
            select new { rt.GrantedCapabilitiesJson, fa.ContextScopeType, fa.ContextScopeId }
        ).ToListAsync(ct);

        // GrantedCapabilitiesJson is a JSON string array; a substring test would
        // match a capability that merely shares a prefix, so parse it properly.
        return roleCaps.Any(r =>
        {
            if (!ScopeCovers(r.ContextScopeType, r.ContextScopeId, scopeType, scopeId)) return false;
            try
            {
                var codes = JsonSerializer.Deserialize<string[]>(r.GrantedCapabilitiesJson);
                return codes?.Contains(code) == true;
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Malformed GrantedCapabilitiesJson on a functional role type");
                return false;
            }
        });
    }
}
