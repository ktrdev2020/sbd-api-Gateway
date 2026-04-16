using System.Text.Json;
using StackExchange.Redis;

namespace Gateway.Services;

public class RedisCacheService : ICacheService
{
    private readonly IDatabase _database;
    private readonly IConnectionMultiplexer _redis;

    // Must match ASP.NET Core's default JSON policy so cached responses
    // arrive at Angular with the same camelCase keys as non-cached responses.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public RedisCacheService(IConnectionMultiplexer redis)
    {
        _redis    = redis;
        _database = redis.GetDatabase();
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var value = await _database.StringGetAsync(key);
        if (!value.HasValue)
            return default;

        return JsonSerializer.Deserialize<T>(value!, JsonOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        var serialized = JsonSerializer.Serialize(value, JsonOptions);
        await _database.StringSetAsync(key, serialized);
        if (expiration.HasValue)
        {
            await _database.KeyExpireAsync(key, expiration.Value);
        }
    }

    public async Task RemoveAsync(string key)
    {
        await _database.KeyDeleteAsync(key);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        return await _database.KeyExistsAsync(key);
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
        catch
        {
            // Graceful: Redis may be unavailable — no-op
        }
    }
}
