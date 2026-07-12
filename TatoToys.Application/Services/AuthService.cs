using System.Text.RegularExpressions;
using TatoToys.Application.DTOs;
using TatoToys.Domain.Interfaces;

namespace TatoToys.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IRedisCacheService _redisService;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IRedisCacheService redisService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _redisService = redisService;
    }

    public async Task<(bool Success, LoginResponse? Response, string? RefreshToken, string? Error, string? ErrorDescription, int StatusCode)> LoginAsync(LoginRequest request, string secret)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return (false, null, null, "invalid_request", "Email and password are required.", 400);

        if (request.Password.Length < 8)
            return (false, null, null, "invalid_request", "Password must be at least 8 characters.", 400);

        try
        {
            var user = await _userRepository.GetByEmailAsync(request.Email);
            if (user == null || !user.IsActive)
                return (false, null, null, "invalid_grant", "Invalid email or password.", 401);

            if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
                return (false, null, null, "invalid_grant", "Invalid email or password.", 401);

            await _userRepository.UpdateLastLoginAsync(user.UserId);

            string accessToken = _tokenService.GenerateAccessToken(user.UserId, secret, 15);
            string refreshToken = _tokenService.GenerateRefreshToken(user.UserId, user.FirstName, user.LastName, secret, 7 * 24 * 60);

            await _redisService.SetRefreshTokenAsync(refreshToken, TimeSpan.FromDays(7));

            return (true, new LoginResponse(accessToken, "Bearer", 900), refreshToken, null, null, 200);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Login error: {ex.Message}");
            return (false, null, null, "server_error", "An internal error occurred.", 500);
        }
    }

    public async Task<(bool Success, LoginResponse? Response, string? RefreshToken, string? Error, string? ErrorDescription, int StatusCode)> RefreshTokenAsync(string refreshToken, string secret)
    {
        if (string.IsNullOrEmpty(refreshToken) || !await _redisService.ValidateRefreshTokenAsync(refreshToken))
            return (false, null, null, "invalid_token", "Missing or invalid refresh token.", 401);

        string? userId = _tokenService.GetUserIdFromToken(refreshToken);
        if (string.IsNullOrEmpty(userId))
            return (false, null, null, "invalid_token", "Malformed refresh token.", 401);

        string newAccessToken = _tokenService.GenerateAccessToken(userId, secret, 15);

        return (true, new LoginResponse(newAccessToken, "Bearer", 900), null, null, null, 200);
    }

    public async Task<(bool Success, object? UserInfo, string? Error, string? ErrorDescription, int StatusCode)> GetCurrentUserAsync(string token, string secret)
    {
        var userInfo = _tokenService.ValidateAndGetUserInfo(token, secret);
        if (userInfo == null)
            return (false, null, "unauthorized", "Token is invalid or expired.", 401);

        var userId = userInfo.GetType().GetProperty("userId")?.GetValue(userInfo)?.ToString();
        var role = userInfo.GetType().GetProperty("role")?.GetValue(userInfo)?.ToString();
        var user = !string.IsNullOrEmpty(userId) ? await _userRepository.GetByIdAsync(userId) : null;

        return (true, new { userId, role, firstName = user?.FirstName, lastName = user?.LastName }, null, null, 200);
    }

    public async Task<(bool Success, object? Response, string? Error, string? ErrorDescription, int StatusCode)> RegisterAsync(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.ConfirmPassword))
            return (false, null, "invalid_request", "All fields are required.", 400);

        if (!Regex.IsMatch(request.Email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
            return (false, null, "invalid_request", "Please enter a valid email address.", 400);

        if (request.Password.Length < 8)
            return (false, null, "invalid_request", "Password must be at least 8 characters.", 400);

        if (request.Password != request.ConfirmPassword)
            return (false, null, "invalid_request", "Passwords do not match.", 400);

        if (!request.IsAdult)
            return (false, null, "invalid_request", "You must confirm that you are 18 years or older.", 400);

        if (!request.TermsAccepted)
            return (false, null, "invalid_request", "You must accept the Terms of Service and Privacy Policy.", 400);

        string passwordHash = _passwordHasher.HashPassword(request.Password);
        var userId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var termsVersion = "1.0";

        try
        {
            await _userRepository.CreateUserAsync(
                userId, request.Email.Trim(), passwordHash,
                request.FirstName.Trim(), request.LastName.Trim(),
                true, now, termsVersion, request.MarketingOptIn, now);
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("1062") || ex.Message.Contains("Duplicate"))
                return (false, null, "conflict", "An account with this email already exists.", 409);

            Console.Error.WriteLine($"Register error: {ex.Message}");
            return (false, null, "server_error", "An internal error occurred.", 500);
        }

        var userResponse = new
        {
            user_id = userId,
            email = request.Email.Trim(),
            first_name = request.FirstName.Trim(),
            last_name = request.LastName.Trim(),
            is_adult = true,
            terms_accepted_at = now,
            terms_version = termsVersion,
            marketing_opt_in = request.MarketingOptIn,
            created_at = now,
        };

        return (true, new { message = "Account created successfully.", user = userResponse }, null, null, 201);
    }

    public async Task LogoutAsync(string? refreshToken)
    {
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await _redisService.InvalidateRefreshTokenAsync(refreshToken);
        }
    }
}
