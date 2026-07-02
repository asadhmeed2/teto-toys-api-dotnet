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

    public async Task SetRefreshTokenAsync(string token, TimeSpan ttl)
    {
        var db = _multiplexer.GetDatabase();
        await db.StringSetAsync($"refresh:{token}", "1", ttl);
    }

    public async Task<bool> ValidateRefreshTokenAsync(string token)
    {
        var db = _multiplexer.GetDatabase();
        return await db.KeyExistsAsync($"refresh:{token}");
    }

    public async Task InvalidateRefreshTokenAsync(string token)
    {
        var db = _multiplexer.GetDatabase();
        await db.KeyDeleteAsync($"refresh:{token}");
    }
}
