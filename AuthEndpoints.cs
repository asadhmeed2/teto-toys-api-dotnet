using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using TatoToys.Application.DTOs;
using TatoToys.Application.Services;

public static class AuthEndpoints
{
    private const string RefreshCookieName = "refresh_token";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (LoginRequest request, HttpContext context) =>
        {
            var (secret, errorResult) = GetSecret(context);
            if (errorResult != null) return errorResult;

            var authService = context.RequestServices.GetRequiredService<IAuthService>();
            var result = await authService.LoginAsync(request, secret!);

            if (!result.Success)
            {
                return Results.Json(new { error = result.Error, error_description = result.ErrorDescription }, statusCode: result.StatusCode);
            }

            SetRefreshCookie(context, result.RefreshToken!);

            return Results.Ok(result.Response);
        });

        group.MapPost("/refresh", async (HttpContext context) =>
        {
            var (secret, errorResult) = GetSecret(context);
            if (errorResult != null) return errorResult;

            var refreshToken = context.Request.Cookies[RefreshCookieName];
            
            var authService = context.RequestServices.GetRequiredService<IAuthService>();
            var result = await authService.RefreshTokenAsync(refreshToken ?? "", secret!);

            if (!result.Success)
            {
                return Results.Json(new { error = result.Error, error_description = result.ErrorDescription }, statusCode: result.StatusCode);
            }

            SetRefreshCookie(context, result.RefreshToken!);

            return Results.Ok(result.Response);
        });

        group.MapPost("/logout", async (HttpContext context) =>
        {
            var refreshToken = context.Request.Cookies[RefreshCookieName];
            
            var authService = context.RequestServices.GetRequiredService<IAuthService>();
            await authService.LogoutAsync(refreshToken);

            context.Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = "/" });
            return Results.Ok(new { message = "Logged out successfully" });
        });

        group.MapGet("/me", async (HttpContext context) =>
        {
            var (secret, errorResult) = GetSecret(context);
            if (errorResult != null) return errorResult;

            var authHeader = context.Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new { error = "unauthorized", error_description = "Missing or invalid Authorization header." }, statusCode: 401);
            }

            var token = authHeader["Bearer ".Length..].Trim();

            var authService = context.RequestServices.GetRequiredService<IAuthService>();
            var result = await authService.GetCurrentUserAsync(token, secret!);

            if (!result.Success)
            {
                return Results.Json(new { error = result.Error, error_description = result.ErrorDescription }, statusCode: result.StatusCode);
            }

            return Results.Ok(result.UserInfo);
        });

        group.MapPost("/register", async (RegisterRequest request, HttpContext context) =>
        {
            var authService = context.RequestServices.GetRequiredService<IAuthService>();
            var result = await authService.RegisterAsync(request);

            if (!result.Success)
            {
                return Results.Json(new { error = result.Error, error_description = result.ErrorDescription }, statusCode: result.StatusCode);
            }

            return Results.Json(result.Response, statusCode: result.StatusCode);
        });
    }

    private static (string? Secret, IResult? ErrorResult) GetSecret(HttpContext context)
    {
        var config = context.RequestServices.GetRequiredService<IConfiguration>();
        var secret = config["JWT:Secret"];
        if (string.IsNullOrEmpty(secret))
        {
            Console.Error.WriteLine("JWT Secret is not configured.");
            return (null, Results.Json(new { error = "server_error", error_description = "An internal error occurred." }, statusCode: 500));
        }
        return (secret, null);
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
