namespace TatoToys.Domain.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(string email, string secretKey, int expireMinutes);
    string GenerateRefreshToken(string email, string secretKey, int expireMinutes);
    string? GetEmailFromToken(string token);
    object? ValidateAndGetUserInfo(string token, string secretKey);
}

