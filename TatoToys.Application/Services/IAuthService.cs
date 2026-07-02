using TatoToys.Application.DTOs;

namespace TatoToys.Application.Services;

public interface IAuthService
{
    Task<(bool Success, LoginResponse? Response, string? RefreshToken, string? Error, string? ErrorDescription, int StatusCode)> LoginAsync(LoginRequest request, string secret);
    Task<(bool Success, LoginResponse? Response, string? RefreshToken, string? Error, string? ErrorDescription, int StatusCode)> RefreshTokenAsync(string refreshToken, string secret);
    Task<(bool Success, object? UserInfo, string? Error, string? ErrorDescription, int StatusCode)> GetCurrentUserAsync(string token, string secret);
    Task<(bool Success, object? Response, string? Error, string? ErrorDescription, int StatusCode)> RegisterAsync(RegisterRequest request);
    Task LogoutAsync(string? refreshToken);
}
