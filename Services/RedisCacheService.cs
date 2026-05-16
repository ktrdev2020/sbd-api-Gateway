using System.Text.Json;
using StackExchange.Redis;

namespace Gateway.Services;

/// <summary>
/// Plan #54 — every Redis operation is wrapped in try/catch so callers (all
/// over the Gateway) get a clean cache-miss behavior when Redis is down,
/// instead of an unhandled exception → 500.
///
/// Contract:
///   • GetAsync   → returns default(T) on cache miss OR Redis outage.
///   • SetAsync   → no-op on outage; caller's source-of-truth update isn't blocked.
///   • RemoveAsync / RemoveByPrefixAsync → no-op on outage.
///   • ExistsAsync → returns false on outage (treat as "not cached").
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly IDatabase _database;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;

    // Must match ASP.NET Core's default JSON policy so cached responses
    // arrive at Angular with the same camelCase keys as non-cached responses.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _redis    = redis;
        _database = redis.GetDatabase();
        _logger   = logger;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var value = await _database.StringGetAsync(key);
            if (!value.HasValue)
                return default;

            return JsonSerializer.Deserialize<T>(value!, JsonOptions);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "Redis GET '{Key}' failed; returning cache miss (caller falls back to DB)", key);
            return default;
        }
        catch (JsonException ex)
        {
            // Corrupt entry: drop it and treat as miss.
            _logger.LogWarning(ex, "Corrupt cache entry for '{Key}', dropping", key);
            try { await _database.KeyDeleteAsync(key); }
            catch (Exception inner) when (inner is RedisException or TimeoutException) { /* swallow */ }
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        try
        {
            var serialized = JsonSerializer.Serialize(value, JsonOptions);
            await _database.StringSetAsync(key, serialized);
            if (expiration.HasValue)
            {
                await _database.KeyExpireAsync(key, expiration.Value);
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "Redis SET '{Key}' failed; cache write skipped", key);
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            await _database.KeyDeleteAsync(key);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "Redis DEL '{Key}' failed; cache evict skipped", key);
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            return await _database.KeyExistsAsync(key);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "Redis EXISTS '{Key}' failed; returning false", key);
            return false;
        }
    }

    public async Task RemoveByPrefixAsync(string prefix)
    {
        try
        {
            foreach (var endpoint in _redis.GetEndPoints())
            {
                var server = _redis.GetServer(endpoint);
                await foreach (var key in server.KeysAsync(pattern: $"{prefix}*"))
                    await _database.KeyDeleteAsync(key);
            }
        }
        catch (Exception ex)
        {
            // Graceful: Redis may be unavailable — no-op
            _logger.LogWarning(ex, "Redis KEYS+DEL by prefix '{Prefix}*' failed; skipped", prefix);
        }
    }
}
