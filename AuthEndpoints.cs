using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", (LoginRequest request) =>
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
                var payload = new
                {
                    sub = request.Email,
                    email = request.Email,
                    exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
                };

                string secret = "SuperSecretKeyForTetoToysTokenAuth2026";
                string token = JwtHelper.GenerateToken(payload, secret);

                return Results.Ok(new LoginResponse(token, "Bearer", 3600));
            }

            return Results.Json(new { error = "invalid_grant", error_description = "Invalid email or password." }, statusCode: 401);
        });

        group.MapPost("/logout", (HttpContext context) =>
        {
            var authHeader = context.Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new { error = "unauthorized", error_description = "Missing or invalid authorization header." }, statusCode: 401);
            }

            return Results.Ok(new { message = "Logged out successfully" });
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
