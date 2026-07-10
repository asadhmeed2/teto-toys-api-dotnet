using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        // ponytail: GET /api/products (public endpoint for storefront)
        group.MapGet("/products", async (HttpContext context, int? page, int? pageSize, string? search) =>
        {
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var connectionString = GetConnectionString(config);

            int pageVal = page ?? 1;
            int pageSizeVal = pageSize ?? 10;
            if (pageVal < 1) pageVal = 1;
            if (pageSizeVal < 1 || pageSizeVal > 100) pageSizeVal = 10;

            int offset = (pageVal - 1) * pageSizeVal;

            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            // 1. Get total count
            var countSql = "SELECT COUNT(1) FROM products WHERE is_deleted = 0 AND is_displayed = 1";
            if (!string.IsNullOrEmpty(search))
            {
                countSql += " AND (title LIKE @search OR description LIKE @search)";
            }
            int totalCount = 0;
            await using (var countCmd = new MySqlCommand(countSql, conn))
            {
                if (!string.IsNullOrEmpty(search))
                {
                    countCmd.Parameters.AddWithValue("@search", $"%{search}%");
                }
                totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            }

            // 2. Get items
            var itemsSql = "SELECT product_id, title, subtitle, description, category, subcategory, price, image_urls FROM products WHERE is_deleted = 0 AND is_displayed = 1";
            if (!string.IsNullOrEmpty(search))
            {
                itemsSql += " AND (title LIKE @search OR description LIKE @search)";
            }
            itemsSql += " ORDER BY created_at DESC LIMIT @limit OFFSET @offset";

            var items = new List<object>();
            await using (var itemsCmd = new MySqlCommand(itemsSql, conn))
            {
                if (!string.IsNullOrEmpty(search))
                {
                    itemsCmd.Parameters.AddWithValue("@search", $"%{search}%");
                }
                itemsCmd.Parameters.AddWithValue("@limit", pageSizeVal);
                itemsCmd.Parameters.AddWithValue("@offset", offset);

                await using var reader = await itemsCmd.ExecuteReaderAsync();
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
            }

            var totalPages = (int)Math.Ceiling((double)totalCount / pageSizeVal);

            return Results.Ok(new
            {
                items,
                total_count = totalCount,
                page = pageVal,
                page_size = pageSizeVal,
                total_pages = totalPages
            });
        });

        // ponytail: GET /api/parts (public endpoint for storefront)
        group.MapGet("/parts", async (HttpContext context, int? page, int? pageSize, string? search) =>
        {
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var connectionString = GetConnectionString(config);

            int pageVal = page ?? 1;
            int pageSizeVal = pageSize ?? 10;
            if (pageVal < 1) pageVal = 1;
            if (pageSizeVal < 1 || pageSizeVal > 100) pageSizeVal = 10;

            int offset = (pageVal - 1) * pageSizeVal;

            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            // 1. Get total count
            var countSql = "SELECT COUNT(1) FROM parts";
            if (!string.IsNullOrEmpty(search))
            {
                countSql += " WHERE title LIKE @search OR description LIKE @search";
            }
            int totalCount = 0;
            await using (var countCmd = new MySqlCommand(countSql, conn))
            {
                if (!string.IsNullOrEmpty(search))
                {
                    countCmd.Parameters.AddWithValue("@search", $"%{search}%");
                }
                totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            }

            // 2. Get items
            var itemsSql = "SELECT part_id, title, description, price, image_urls FROM parts";
            if (!string.IsNullOrEmpty(search))
            {
                itemsSql += " WHERE title LIKE @search OR description LIKE @search";
            }
            itemsSql += " ORDER BY created_at DESC LIMIT @limit OFFSET @offset";

            var items = new List<object>();
            await using (var itemsCmd = new MySqlCommand(itemsSql, conn))
            {
                if (!string.IsNullOrEmpty(search))
                {
                    itemsCmd.Parameters.AddWithValue("@search", $"%{search}%");
                }
                itemsCmd.Parameters.AddWithValue("@limit", pageSizeVal);
                itemsCmd.Parameters.AddWithValue("@offset", offset);

                await using var reader = await itemsCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    items.Add(new
                    {
                        part_id = reader.GetGuid(reader.GetOrdinal("part_id")).ToString(),
                        title = reader.GetString(reader.GetOrdinal("title")),
                        description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                        price = reader.GetDecimal(reader.GetOrdinal("price")),
                        image_urls = reader.IsDBNull(reader.GetOrdinal("image_urls")) 
                            ? new List<string>() 
                            : System.Text.Json.JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("image_urls"))) ?? new List<string>()
                    });
                }
            }

            var totalPages = (int)Math.Ceiling((double)totalCount / pageSizeVal);

            return Results.Ok(new
            {
                items,
                total_count = totalCount,
                page = pageVal,
                page_size = pageSizeVal,
                total_pages = totalPages
            });
        });

        // ponytail: GET /api/categories (public endpoint for storefront lookup)
        group.MapGet("/categories", async (HttpContext context) =>
        {
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var connectionString = GetConnectionString(config);

            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            var itemsSql = "SELECT id, name, slug FROM categories ORDER BY name ASC";
            var items = new List<object>();

            await using (var cmd = new MySqlCommand(itemsSql, conn))
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    items.Add(new
                    {
                        id = reader.GetInt32(reader.GetOrdinal("id")),
                        name = reader.GetString(reader.GetOrdinal("name")),
                        slug = reader.GetString(reader.GetOrdinal("slug"))
                    });
                }
            }

            return Results.Ok(items);
        });
    }

    private static string GetConnectionString(IConfiguration config)
    {
        var rawConnectionString = config["MySQL:ConnectionString"] ?? config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(rawConnectionString))
        {
            return string.Empty;
        }

        var connBuilder = new MySqlConnectionStringBuilder(rawConnectionString);

        if (!string.IsNullOrEmpty(config["MySQL:Server"]))
            connBuilder.Server = config["MySQL:Server"];
        if (!string.IsNullOrEmpty(config["MySQL:Port"]) && uint.TryParse(config["MySQL:Port"], out var port))
            connBuilder.Port = port;
        if (!string.IsNullOrEmpty(config["MySQL:Database"]))
            connBuilder.Database = config["MySQL:Database"];
        if (!string.IsNullOrEmpty(config["MySQL:User"]))
            connBuilder.UserID = config["MySQL:User"];
        if (!string.IsNullOrEmpty(config["MySQL:Password"]))
            connBuilder.Password = config["MySQL:Password"];

        return connBuilder.ConnectionString;
    }
}
