namespace TatoToys.Domain.Interfaces;

public interface IRedisCacheService
{
    Task SetRefreshTokenAsync(string token, TimeSpan ttl);
    Task<bool> ValidateRefreshTokenAsync(string token);
    Task InvalidateRefreshTokenAsync(string token);
}
