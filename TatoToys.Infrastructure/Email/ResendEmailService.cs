using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TatoToys.Domain.Interfaces;

namespace TatoToys.Infrastructure.Email;

public class ResendEmailService : IEmailService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _fromEmail;

    public ResendEmailService(HttpClient http, string apiKey, string fromEmail)
    {
        _http = http;
        _apiKey = apiKey;
        _fromEmail = fromEmail;
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink)
    {
        var payload = new
        {
            from = _fromEmail,
            to = new[] { toEmail },
            subject = "Reset your TatoToys password",
            html = $"""
                <div style="font-family: sans-serif; max-width: 480px; margin: 0 auto; padding: 32px 24px;">
                  <h2 style="color: #0f172a; margin-bottom: 8px;">Reset your password</h2>
                  <p style="color: #475569; margin-bottom: 24px;">
                    We received a request to reset your TatoToys account password.
                    Click the button below to choose a new one. This link expires in <strong>15 minutes</strong>.
                  </p>
                  <a href="{resetLink}"
                     style="display: inline-block; padding: 14px 28px; background: linear-gradient(135deg, #08b880, #00d4aa);
                            color: #fff; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 15px;">
                    Reset Password
                  </a>
                  <p style="color: #94a3b8; font-size: 13px; margin-top: 24px;">
                    If you didn't request a password reset, you can safely ignore this email.
                  </p>
                </div>
                """
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception($"Resend API error {response.StatusCode}: {body}");
        }
    }
}
