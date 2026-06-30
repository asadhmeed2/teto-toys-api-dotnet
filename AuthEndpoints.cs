using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

public static class AuthEndpoints
{
    private const string RefreshCookieName = "refresh_token";
    private const string Secret = "SuperSecretKeyForTetoToysTokenAuth2026";
    private static readonly TimeSpan RefreshTokenTtl = TimeSpan.FromDays(7);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (LoginRequest request, HttpContext context) =>
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

                // Store in Redis with 7-day TTL
                var db = context.RequestServices.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
                await db.StringSetAsync($"refresh:{refreshToken}", "1", RefreshTokenTtl);

                SetRefreshCookie(context, refreshToken);

                return Results.Ok(new LoginResponse(accessToken, "Bearer", 900));
            }

            return Results.Json(new { error = "invalid_grant", error_description = "Invalid email or password." }, statusCode: 401);
        });

        group.MapPost("/refresh", async (HttpContext context) =>
        {
            var refreshToken = context.Request.Cookies[RefreshCookieName];
            var db = context.RequestServices.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

            if (string.IsNullOrEmpty(refreshToken) || !await db.KeyExistsAsync($"refresh:{refreshToken}"))
            {
                return Results.Json(new { error = "invalid_token", error_description = "Missing or invalid refresh token." }, statusCode: 401);
            }

            // Rotate: invalidate old token
            await db.KeyDeleteAsync($"refresh:{refreshToken}");

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
            await db.StringSetAsync($"refresh:{newRefreshToken}", "1", RefreshTokenTtl);

            SetRefreshCookie(context, newRefreshToken);

            return Results.Ok(new LoginResponse(newAccessToken, "Bearer", 900));
        });

        group.MapPost("/logout", async (HttpContext context) =>
        {
            var refreshToken = context.Request.Cookies[RefreshCookieName];
            if (!string.IsNullOrEmpty(refreshToken))
            {
                var db = context.RequestServices.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
                await db.KeyDeleteAsync($"refresh:{refreshToken}");
            }

            context.Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = "/" });
            return Results.Ok(new { message = "Logged out successfully" });
        });

        // GET /me — validate access token and return the current user's info
        group.MapGet("/me", (HttpContext context) =>
        {
            var authHeader = context.Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new { error = "unauthorized", error_description = "Missing or invalid Authorization header." }, statusCode: 401);
            }

            var token = authHeader["Bearer ".Length..].Trim();
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(Secret);

                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = JwtHelper.Issuer,
                    ValidateAudience = true,
                    ValidAudience = JwtHelper.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                }, out var validatedToken);

                var jwt = (JwtSecurityToken)validatedToken;
                var email = jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value;
                var role = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value ?? "User";

                return Results.Ok(new { email, role });
            }
            catch
            {
                return Results.Json(new { error = "unauthorized", error_description = "Token is invalid or expired." }, statusCode: 401);
            }
        });

        group.MapPost("/register", async (RegisterRequest request, HttpContext context) =>
        {
            // --- Required field checks ---
            if (string.IsNullOrWhiteSpace(request.FirstName) ||
                string.IsNullOrWhiteSpace(request.LastName) ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                return Results.BadRequest(new { error = "invalid_request", error_description = "All fields are required." });
            }

            // --- Email format validation ---
            if (!Regex.IsMatch(request.Email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
            {
                return Results.BadRequest(new { error = "invalid_request", error_description = "Please enter a valid email address." });
            }

            // --- Password strength ---
            if (request.Password.Length < 8)
            {
                return Results.BadRequest(new { error = "invalid_request", error_description = "Password must be at least 8 characters." });
            }

            // --- Passwords match ---
            if (request.Password != request.ConfirmPassword)
            {
                return Results.BadRequest(new { error = "invalid_request", error_description = "Passwords do not match." });
            }

            // --- Compliance checks ---
            if (!request.IsAdult)
            {
                return Results.BadRequest(new { error = "invalid_request", error_description = "You must confirm that you are 18 years or older." });
            }

            if (!request.TermsAccepted)
            {
                return Results.BadRequest(new { error = "invalid_request", error_description = "You must accept the Terms of Service and Privacy Policy." });
            }

            // --- Hash password ---
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

            // --- Stub response (no DB yet) ---
            var userId = Guid.NewGuid().ToString();
            var now = DateTimeOffset.UtcNow;

            return Results.Json(new
            {
                message = "Account created successfully.",
                user = new
                {
                    user_id = userId,
                    email = request.Email.Trim(),
                    first_name = request.FirstName.Trim(),
                    last_name = request.LastName.Trim(),
                    is_adult = true,
                    terms_accepted_at = now,
                    terms_version = "1.0",
                    marketing_opt_in = request.MarketingOptIn,
                    created_at = now,
                }
            }, statusCode: 201);
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

public record RegisterRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("first_name")] string FirstName,
    [property: System.Text.Json.Serialization.JsonPropertyName("last_name")] string LastName,
    [property: System.Text.Json.Serialization.JsonPropertyName("email")] string Email,
    [property: System.Text.Json.Serialization.JsonPropertyName("password")] string Password,
    [property: System.Text.Json.Serialization.JsonPropertyName("confirm_password")] string ConfirmPassword,
    [property: System.Text.Json.Serialization.JsonPropertyName("is_adult")] bool IsAdult,
    [property: System.Text.Json.Serialization.JsonPropertyName("terms_accepted")] bool TermsAccepted,
    [property: System.Text.Json.Serialization.JsonPropertyName("marketing_opt_in")] bool MarketingOptIn
);

public static class JwtHelper
{
    public const string Issuer = "tatotoys-api";
    public const string Audience = "tatotoys-frontend";

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
