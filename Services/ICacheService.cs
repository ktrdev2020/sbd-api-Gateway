namespace Gateway.Services;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
    Task RemoveAsync(string key);
    Task<bool> ExistsAsync(string key);
    /// <summary>Removes all keys whose name starts with <paramref name="prefix"/>.</summary>
    Task RemoveByPrefixAsync(string prefix);
}
