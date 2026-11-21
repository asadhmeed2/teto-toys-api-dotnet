namespace TatoToys.Domain.Interfaces;

public interface IRedisCacheService
{
    Task SetRefreshTokenAsync(string token, TimeSpan ttl, string userId);
    Task InvalidateUserRefreshTokensAsync(string userId);
    Task<bool> ValidateRefreshTokenAsync(string token);
    Task InvalidateRefreshTokenAsync(string token);

    Task SetResetTokenAsync(string key, string userId, TimeSpan ttl);
    Task<string?> GetResetTokenUserIdAsync(string key);
    Task InvalidateResetTokenAsync(string key);
    /// <summary>SET NX with TTL: true only for the first caller inside the window.</summary>
    Task<bool> TryAcquireCooldownAsync(string key, TimeSpan ttl);
}
