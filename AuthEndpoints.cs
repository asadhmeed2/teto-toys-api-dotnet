using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

// In-memory refresh token store (resets on restart; swap for Redis/DB in production)
public static class RefreshTokenStore
{
    private static readonly HashSet<string> _tokens = new();

    public static void Add(string token) => _tokens.Add(token);
    public static bool Contains(string token) => _tokens.Contains(token);
    public static void Remove(string token) => _tokens.Remove(token);
}

public static class AuthEndpoints
{
    private const string RefreshCookieName = "refresh_token";
    private const string Secret = "SuperSecretKeyForTetoToysTokenAuth2026";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", (LoginRequest request, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "invalid_request", error_description = "Email and password are required." });
            }

            if (request.Password.Length < 8)
            {
                return Results.BadRequest(new { error = "invalid_request", error_description = "Password must be at least 8 characters." });
            }

            if (request.Email == "admin@tetotoys.com" && request.Password == "password123")
            {
                // Short-lived access token (15 minutes)
                var accessPayload = new
                {
                    sub = request.Email,
                    email = request.Email,
                    exp = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds()
                };
                string accessToken = JwtHelper.GenerateToken(accessPayload, Secret);

                // Long-lived refresh token (7 days)
                var refreshPayload = new
                {
                    sub = request.Email,
                    email = request.Email,
                    type = "refresh",
                    exp = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds()
                };
                string refreshToken = JwtHelper.GenerateToken(refreshPayload, Secret);
                RefreshTokenStore.Add(refreshToken);

                SetRefreshCookie(context, refreshToken);

                return Results.Ok(new LoginResponse(accessToken, "Bearer", 900));
            }

            return Results.Json(new { error = "invalid_grant", error_description = "Invalid email or password." }, statusCode: 401);
        });

        group.MapPost("/refresh", (HttpContext context) =>
        {
            var refreshToken = context.Request.Cookies[RefreshCookieName];

            if (string.IsNullOrEmpty(refreshToken) || !RefreshTokenStore.Contains(refreshToken))
            {
                return Results.Json(new { error = "invalid_token", error_description = "Missing or invalid refresh token." }, statusCode: 401);
            }

            // Rotate: invalidate old token
            RefreshTokenStore.Remove(refreshToken);

            // Decode email from payload (base64url decode)
            string email;
            try
            {
                var parts = refreshToken.Split('.');
                var payloadJson = Base64UrlDecode(parts[1]);
                using var doc = JsonDocument.Parse(payloadJson);
                email = doc.RootElement.GetProperty("email").GetString()!;
            }
            catch
            {
                return Results.Json(new { error = "invalid_token", error_description = "Malformed refresh token." }, statusCode: 401);
            }

            var newAccessPayload = new
            {
                sub = email,
                email = email,
                exp = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds()
            };
            string newAccessToken = JwtHelper.GenerateToken(newAccessPayload, Secret);

            var newRefreshPayload = new
            {
                sub = email,
                email = email,
                type = "refresh",
                exp = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds()
            };
            string newRefreshToken = JwtHelper.GenerateToken(newRefreshPayload, Secret);
            RefreshTokenStore.Add(newRefreshToken);

            SetRefreshCookie(context, newRefreshToken);

            return Results.Ok(new LoginResponse(newAccessToken, "Bearer", 900));
        });

        group.MapPost("/logout", (HttpContext context) =>
        {
            var refreshToken = context.Request.Cookies[RefreshCookieName];
            if (!string.IsNullOrEmpty(refreshToken))
            {
                RefreshTokenStore.Remove(refreshToken);
            }

            context.Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = "/" });
            return Results.Ok(new { message = "Logged out successfully" });
        });
    }

    private static void SetRefreshCookie(HttpContext context, string token)
    {
        var isProduction = !context.RequestServices
            .GetRequiredService<IWebHostEnvironment>().IsDevelopment();

        context.Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = isProduction,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/",
        });
    }

    private static string Base64UrlDecode(string input)
    {
        var padding = (4 - input.Length % 4) % 4;
        var base64 = input.Replace('-', '+').Replace('_', '/') + new string('=', padding);
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}

public record LoginRequest(string Email, string Password);

public record LoginResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
    [property: System.Text.Json.Serialization.JsonPropertyName("token_type")] string TokenType,
    [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn
);

public static class JwtHelper
{
    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace("=", "")
            .Replace("+", "-")
            .Replace("/", "_");
    }

    public static string GenerateToken(object payload, string secret)
    {
        var header = new { alg = "HS256", typ = "JWT" };

        string headerJson = JsonSerializer.Serialize(header);
        string payloadJson = JsonSerializer.Serialize(payload);

        string headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        string payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        string signingInput = $"{headerB64}.{payloadB64}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
        string signatureB64 = Base64UrlEncode(signatureBytes);

        return $"{signingInput}.{signatureB64}";
    }
}
