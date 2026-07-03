using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TatoToys.Application.Services;

public static class PasswordResetEndpoints
{
    public static void MapPasswordResetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // POST /api/auth/forgot-password
        group.MapPost("/forgot-password", async (ForgotPasswordRequest request, HttpContext context) =>
        {
            var service = context.RequestServices.GetRequiredService<IPasswordResetService>();
            var result = await service.ForgotPasswordAsync(request.Email ?? "");

            if (!result.Success)
                return Results.Json(new { error = result.Error, error_description = result.ErrorDescription }, statusCode: result.StatusCode);

            // Always return 200 to prevent user enumeration
            return Results.Ok(new { message = "If an account with that email exists, a password reset link has been sent." });
        });

        // POST /api/auth/reset-password
        group.MapPost("/reset-password", async (ResetPasswordRequest request, HttpContext context) =>
        {
            var service = context.RequestServices.GetRequiredService<IPasswordResetService>();
            var result = await service.ResetPasswordAsync(
                request.Token ?? "",
                request.NewPassword ?? "",
                request.ConfirmPassword ?? "");

            if (!result.Success)
                return Results.Json(new { error = result.Error, error_description = result.ErrorDescription }, statusCode: result.StatusCode);

            return Results.Ok(new { message = "Password has been reset successfully." });
        });
    }
}

public record ForgotPasswordRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("email")] string? Email
);

public record ResetPasswordRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("token")] string? Token,
    [property: System.Text.Json.Serialization.JsonPropertyName("new_password")] string? NewPassword,
    [property: System.Text.Json.Serialization.JsonPropertyName("confirm_password")] string? ConfirmPassword
);
