using StackExchange.Redis;
using TatoToys.Domain.Interfaces;

namespace TatoToys.Infrastructure.Caching;

public class RedisCacheService : IRedisCacheService
{
    private readonly IConnectionMultiplexer _multiplexer;

    public RedisCacheService(IConnectionMultiplexer multiplexer)
    {
        _multiplexer = multiplexer;
    }

    public async Task SetRefreshTokenAsync(string token, TimeSpan ttl, string userId)
    {
        var db = _multiplexer.GetDatabase();
        await db.StringSetAsync($"refresh:{token}", "1", ttl);
        // Per-user index so a password reset can revoke every session. Key shared with node/flask.
        await db.SetAddAsync($"user_refresh:{userId}", token);
        await db.KeyExpireAsync($"user_refresh:{userId}", ttl);
    }

    public async Task InvalidateUserRefreshTokensAsync(string userId)
    {
        var db = _multiplexer.GetDatabase();
        var tokens = await db.SetMembersAsync($"user_refresh:{userId}");
        var keys = tokens.Select(t => (RedisKey)$"refresh:{t}").Append($"user_refresh:{userId}").ToArray();
        await db.KeyDeleteAsync(keys);
    }

    public async Task<bool> ValidateRefreshTokenAsync(string token)
    {
        var db = _multiplexer.GetDatabase();
        return await db.KeyExistsAsync($"refresh:{token}");
    }

    public async Task<string?> GetRefreshTokenAsync(string Id)
    {
        var db = _multiplexer.GetDatabase();
        var value = await db.StringGetAsync($"refresh:{Id}");
        return value.HasValue ? value.ToString() : null;
    }

    public async Task InvalidateRefreshTokenAsync(string token)
    {
        var db = _multiplexer.GetDatabase();
        await db.KeyDeleteAsync($"refresh:{token}");
    }

    public async Task SetResetTokenAsync(string key, string userId, TimeSpan ttl)
    {
        var db = _multiplexer.GetDatabase();
        await db.StringSetAsync(key, userId, ttl);
    }

    public async Task<string?> GetResetTokenUserIdAsync(string key)
    {
        var db = _multiplexer.GetDatabase();
        var value = await db.StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task InvalidateResetTokenAsync(string key)
    {
        var db = _multiplexer.GetDatabase();
        await db.KeyDeleteAsync(key);
    }
}
