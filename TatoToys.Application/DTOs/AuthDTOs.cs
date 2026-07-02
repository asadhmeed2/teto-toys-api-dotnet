using System.Text.Json.Serialization;

namespace TatoToys.Application.DTOs;

public record LoginRequest(string Email, string Password);

public record LoginResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn
);

public record RegisterRequest(
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("confirm_password")] string ConfirmPassword,
    [property: JsonPropertyName("is_adult")] bool IsAdult,
    [property: JsonPropertyName("terms_accepted")] bool TermsAccepted,
    [property: JsonPropertyName("marketing_opt_in")] bool MarketingOptIn
);
