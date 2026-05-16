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
        var redisDb = redis.GetDatabase();
        var cacheKey = CacheKey(userId, capVersion);
        RedisValue cached;
        try
        {
            cached = await redisDb.StringGetAsync(cacheKey);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis read failed for cap grants (user {UserId} cap_v={V}); falling back to DB", userId, capVersion);
            cached = RedisValue.Null;
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

        // 3. Write to Redis
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

        return grants.Any(g =>
        {
            if (g.Code != code) return false;
            if (scopeType == null) return true; // caller didn't specify scope — any match
            // Scope check: grant must be at least as broad as requested scope
            if (g.ScopeType == "global") return true;
            if (g.ScopeType == scopeType && g.ScopeId == scopeId) return true;
            if (g.ScopeType == "area" && scopeType is "school" or "department" or "classroom" or "self")
                return true;
            if (g.ScopeType == "school" && scopeType is "department" or "classroom" or "self")
                return true;
            return false;
        });
    }
}
