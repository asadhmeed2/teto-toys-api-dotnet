using System.Security.Cryptography;
using System.Text;
using TatoToys.Domain.Interfaces;

namespace TatoToys.Application.Services;

public class PasswordResetService : IPasswordResetService
{
    private static readonly TimeSpan ResetTokenTtl = TimeSpan.FromMinutes(15);
    // One reset email per account per 5 minutes, shared with node/flask: stops inbox flooding
    // and Resend quota burn from distributed requests the per-IP limiter can't see.
    private static readonly TimeSpan ResetCooldown = TimeSpan.FromMinutes(5);

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

    // Redis holds only the token's hash; the raw token lives in the email link.
    private static string ResetKey(string token) => $"reset:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)))}";

    public async Task<(bool Success, string? Error, string? ErrorDescription, int StatusCode)> ForgotPasswordAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return (false, "invalid_request", "Email is required.", 400);

        try
        {
            // Always return success to prevent user enumeration attacks
            var user = await _userRepository.GetByEmailAsync(email.Trim());
            if (user != null && user.IsActive
                && await _redisService.TryAcquireCooldownAsync($"reset_cooldown:{user.UserId}", ResetCooldown))
            {
                var token = RandomNumberGenerator.GetHexString(64, lowercase: true);
                await _redisService.SetResetTokenAsync(ResetKey(token), user.UserId, ResetTokenTtl);

                var resetLink = $"{_frontendBaseUrl}/reset-password?token={token}";
                // Log, don't fail: a send failure (bounce, quota, outage) must still return the generic 200, or 500-vs-200 tells an attacker which emails have accounts.
                try { await _emailService.SendPasswordResetEmailAsync(user.Email, resetLink); }
                catch (Exception ex) { Console.Error.WriteLine($"ForgotPassword email send failed: {ex.Message}"); }
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

        // bcrypt ignores bytes past 72; reject instead of silently truncating.
        if (Encoding.UTF8.GetByteCount(newPassword) > 72)
            return (false, "invalid_request", "Password must be at most 72 bytes.", 400);

        if (newPassword != confirmPassword)
            return (false, "invalid_request", "Passwords do not match.", 400);

        try
        {
            var userId = await _redisService.GetResetTokenUserIdAsync(ResetKey(token));
            if (string.IsNullOrEmpty(userId))
                return (false, "invalid_token", "Reset token is invalid or has expired.", 400);

            await _redisService.InvalidateResetTokenAsync(ResetKey(token));

            var passwordHash = _passwordHasher.HashPassword(newPassword);
            await _userRepository.UpdatePasswordAsync(userId, passwordHash);
            // Log out every existing session — an attacker's stolen session must not survive the reset.
            await _redisService.InvalidateUserRefreshTokensAsync(userId);

            return (true, null, null, 200);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ResetPassword error: {ex.Message}");
            return (false, "server_error", "An internal error occurred.", 500);
        }
    }
}
