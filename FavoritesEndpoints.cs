using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;

public static class FavoritesEndpoints
{
    public static void MapFavoritesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/favorites");

        // GET /api/favorites — list the authenticated user's favourite products
        group.MapGet("/", async (HttpContext context) =>
        {
            var userId = ExtractUserId(context);
            if (userId == null)
                return Results.Json(new { error = "unauthorized", error_description = "Missing or invalid Authorization header." }, statusCode: 401);

            var connectionString = GetConnectionString(context);
            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = @"
                SELECT p.product_id, p.title, p.subtitle, p.description,
                       p.category, p.subcategory, p.price, p.image_urls
                FROM favorites_products f
                JOIN products p ON p.product_id = f.product_id
                WHERE f.user_id = @userId
                  AND p.is_deleted = 0
                  AND p.is_displayed = 1
                ORDER BY f.created_at DESC";

            var items = new List<object>();
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@userId", userId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(new
                {
                    product_id = reader.GetGuid(reader.GetOrdinal("product_id")).ToString(),
                    title = reader.GetString(reader.GetOrdinal("title")),
                    subtitle = reader.IsDBNull(reader.GetOrdinal("subtitle")) ? null : reader.GetString(reader.GetOrdinal("subtitle")),
                    description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                    category = reader.GetInt32(reader.GetOrdinal("category")),
                    subcategory = reader.IsDBNull(reader.GetOrdinal("subcategory")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("subcategory")),
                    price = reader.GetDecimal(reader.GetOrdinal("price")),
                    image_urls = reader.IsDBNull(reader.GetOrdinal("image_urls"))
                        ? new List<string>()
                        : System.Text.Json.JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("image_urls"))) ?? new List<string>()
                });
            }

            return Results.Ok(new { items });
        });

        // GET /api/favorites/ids — return only the list of product IDs the user has favourited
        group.MapGet("/ids", async (HttpContext context) =>
        {
            var userId = ExtractUserId(context);
            if (userId == null)
                return Results.Json(new { error = "unauthorized", error_description = "Missing or invalid Authorization header." }, statusCode: 401);

            var connectionString = GetConnectionString(context);
            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = "SELECT product_id FROM favorites_products WHERE user_id = @userId";
            var ids = new List<string>();
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@userId", userId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                ids.Add(reader.GetGuid(0).ToString());

            return Results.Ok(new { ids });
        });

        // POST /api/favorites/{productId} — add a product to favourites
        group.MapPost("/{productId}", async (string productId, HttpContext context) =>
        {
            var userId = ExtractUserId(context);
            if (userId == null)
                return Results.Json(new { error = "unauthorized", error_description = "Missing or invalid Authorization header." }, statusCode: 401);

            var connectionString = GetConnectionString(context);
            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            // Check product exists and is visible
            const string checkSql = "SELECT COUNT(1) FROM products WHERE product_id = @productId AND is_deleted = 0 AND is_displayed = 1";
            await using (var checkCmd = new MySqlCommand(checkSql, conn))
            {
                checkCmd.Parameters.AddWithValue("@productId", productId);
                var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                if (count == 0)
                    return Results.NotFound(new { error = "not_found", error_description = "Product not found." });
            }

            // INSERT IGNORE so duplicates are silently ignored
            const string insertSql = "INSERT IGNORE INTO favorites_products (user_id, product_id) VALUES (@userId, @productId)";
            await using var cmd = new MySqlCommand(insertSql, conn);
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@productId", productId);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok(new { product_id = productId, is_favorite = true });
        });

        // DELETE /api/favorites/{productId} — remove a product from favourites
        group.MapDelete("/{productId}", async (string productId, HttpContext context) =>
        {
            var userId = ExtractUserId(context);
            if (userId == null)
                return Results.Json(new { error = "unauthorized", error_description = "Missing or invalid Authorization header." }, statusCode: 401);

            var connectionString = GetConnectionString(context);
            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            const string deleteSql = "DELETE FROM favorites_products WHERE user_id = @userId AND product_id = @productId";
            await using var cmd = new MySqlCommand(deleteSql, conn);
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@productId", productId);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok(new { product_id = productId, is_favorite = false });
        });
    }

    private static string? ExtractUserId(HttpContext context)
    {
        var authHeader = context.Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = authHeader["Bearer ".Length..].Trim();
        var config = context.RequestServices.GetRequiredService<IConfiguration>();
        var secret = config["JWT:Secret"];
        if (string.IsNullOrEmpty(secret)) return null;

        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(secret);
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = "tatotoys-api",
                ValidateAudience = true,
                ValidAudience = "tatotoys-frontend",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            }, out var validatedToken);

            var jwt = (JwtSecurityToken)validatedToken;
            return jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value;
        }
        catch
        {
            return null;
        }
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
