using TatoToys.Domain.Interfaces;

namespace TatoToys.Application.Services;

public class PasswordResetService : IPasswordResetService
{
    private static readonly TimeSpan ResetTokenTtl = TimeSpan.FromMinutes(15);

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRedisCacheService _redisService;
    private readonly IEmailService _emailService;
    private readonly string _frontendBaseUrl;

    public PasswordResetService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IRedisCacheService redisService,
        IEmailService emailService,
        string frontendBaseUrl)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _redisService = redisService;
        _emailService = emailService;
        _frontendBaseUrl = frontendBaseUrl;
    }

    public async Task<(bool Success, string? Error, string? ErrorDescription, int StatusCode)> ForgotPasswordAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return (false, "invalid_request", "Email is required.", 400);

        try
        {
            // Always return success to prevent user enumeration attacks
            var user = await _userRepository.GetByEmailAsync(email.Trim());
            if (user != null && user.IsActive)
            {
                var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                await _redisService.SetResetTokenAsync($"reset:{token}", user.UserId, ResetTokenTtl);

                var resetLink = $"{_frontendBaseUrl}/reset-password?token={token}";
                await _emailService.SendPasswordResetEmailAsync(user.Email, resetLink);
            }

            return (true, null, null, 200);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ForgotPassword error: {ex.Message}");
            return (false, "server_error", "An internal error occurred.", 500);
        }
    }

    public async Task<(bool Success, string? Error, string? ErrorDescription, int StatusCode)> ResetPasswordAsync(string token, string newPassword, string confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (false, "invalid_request", "Token is required.", 400);

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return (false, "invalid_request", "Password must be at least 8 characters.", 400);

        if (newPassword != confirmPassword)
            return (false, "invalid_request", "Passwords do not match.", 400);

        try
        {
            var userId = await _redisService.GetResetTokenUserIdAsync($"reset:{token}");
            if (string.IsNullOrEmpty(userId))
                return (false, "invalid_token", "Reset token is invalid or has expired.", 400);

            await _redisService.InvalidateResetTokenAsync($"reset:{token}");

            var passwordHash = _passwordHasher.HashPassword(newPassword);
            await _userRepository.UpdatePasswordAsync(userId, passwordHash);

            return (true, null, null, 200);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ResetPassword error: {ex.Message}");
            return (false, "server_error", "An internal error occurred.", 500);
        }
    }
}
