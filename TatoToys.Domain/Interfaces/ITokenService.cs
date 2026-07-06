namespace TatoToys.Domain.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(string userId, string secretKey, int expireMinutes);
    string GenerateRefreshToken(string userId, string secretKey, int expireMinutes);
    string? GetUserIdFromToken(string token);
    object? ValidateAndGetUserInfo(string token, string secretKey);
}

