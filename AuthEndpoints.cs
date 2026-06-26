using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Tokens;

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
                string accessToken = JwtHelper.GenerateToken(request.Email, Secret, expireMinutes: 15);

                // Long-lived refresh token (7 days)
                string refreshToken = JwtHelper.GenerateToken(request.Email, Secret, expireMinutes: 7 * 24 * 60, tokenType: "refresh");
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

            // Decode email from the JWT using JwtSecurityTokenHandler
            string email;
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(refreshToken);
                email = jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value;
            }
            catch
            {
                return Results.Json(new { error = "invalid_token", error_description = "Malformed refresh token." }, statusCode: 401);
            }

            string newAccessToken = JwtHelper.GenerateToken(email, Secret, expireMinutes: 15);
            string newRefreshToken = JwtHelper.GenerateToken(email, Secret, expireMinutes: 7 * 24 * 60, tokenType: "refresh");
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
}

public record LoginRequest(string Email, string Password);

public record LoginResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
    [property: System.Text.Json.Serialization.JsonPropertyName("token_type")] string TokenType,
    [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn
);

public static class JwtHelper
{
    private const string Issuer = "tatotoys-api";
    private const string Audience = "tatotoys-frontend";

    public static string GenerateToken(string email, string secretKey, int expireMinutes, string tokenType = "access")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(secretKey);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, email),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Role, "User"),
        };

        if (tokenType == "refresh")
        {
            claims.Add(new Claim("token_type", "refresh"));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(expireMinutes),
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature
            )
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
