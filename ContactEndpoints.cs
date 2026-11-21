using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System.Net;
using System.Text.Json.Serialization;

public static class ContactEndpoints
{
    public static void MapContactEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/contact — save a contact form submission (public, no auth required)
        app.MapPost("/api/contact", async (ContactRequest body, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name) ||
                string.IsNullOrWhiteSpace(body.Email) ||
                string.IsNullOrWhiteSpace(body.Message))
            {
                return Results.BadRequest(new
                {
                    error = "validation_error",
                    error_description = "name, email, and message are required.",
                });
            }

            // ponytail: HTML-encode free-text fields so a submitted <script> tag is stored
            // as inert text, protecting any future consumer (admin UI, email digest, etc.)
            // that renders these values, regardless of whether that consumer remembers to encode.
            var name = WebUtility.HtmlEncode(body.Name.Trim());
            var email = WebUtility.HtmlEncode(body.Email.Trim());
            var subject = string.IsNullOrWhiteSpace(body.Subject) ? null : WebUtility.HtmlEncode(body.Subject.Trim());
            var message = WebUtility.HtmlEncode(body.Message.Trim());

            // Bounds match the columns (checked after encoding, which grows the text).
            if (name.Length > 120 || email.Length > 255 || (subject?.Length ?? 0) > 255 || message.Length > 5000)
                return Results.BadRequest(new { error = "validation_error", error_description = "name up to 120, email and subject up to 255, message up to 5000 characters." });

            var connectionString = GetConnectionString(context);
            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = @"
                INSERT INTO contact_messages (name, email, subject, message)
                VALUES (@name, @email, @subject, @message)";

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@email", email);
            cmd.Parameters.AddWithValue("@subject", (object?)subject ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@message", message);
            await cmd.ExecuteNonQueryAsync();

            return Results.Created("/api/contact", new
            {
                success = true,
                message = "Thank you for reaching out! We will get back to you within 1–2 business days.",
            });
        });
    }

    private static string GetConnectionString(HttpContext context)
    {
        var config = context.RequestServices.GetRequiredService<IConfiguration>();
        var rawConnectionString = config["MySQL:ConnectionString"] ?? config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(rawConnectionString)) return string.Empty;

        var connBuilder = new MySqlConnectionStringBuilder(rawConnectionString);
        if (!string.IsNullOrEmpty(config["MySQL:Server"])) connBuilder.Server = config["MySQL:Server"];
        if (!string.IsNullOrEmpty(config["MySQL:Port"]) && uint.TryParse(config["MySQL:Port"], out var port)) connBuilder.Port = port;
        if (!string.IsNullOrEmpty(config["MySQL:Database"])) connBuilder.Database = config["MySQL:Database"];
        if (!string.IsNullOrEmpty(config["MySQL:User"])) connBuilder.UserID = config["MySQL:User"];
        if (!string.IsNullOrEmpty(config["MySQL:Password"])) connBuilder.Password = config["MySQL:Password"];
        return connBuilder.ConnectionString;
    }
}

public record ContactRequest(
    [property: JsonPropertyName("name")]    string Name,
    [property: JsonPropertyName("email")]   string Email,
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("message")] string Message
);
