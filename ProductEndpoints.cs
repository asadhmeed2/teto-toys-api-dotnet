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
        group.MapGet("/products", async (HttpContext context, int? page, int? pageSize, string? search, string? category, string? lang) =>
        {
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var connectionString = GetConnectionString(config);

            int pageVal = page ?? 1;
            int pageSizeVal = pageSize ?? 10;
            if (pageVal < 1) pageVal = 1;
            if (pageSizeVal < 1 || pageSizeVal > 100) pageSizeVal = 10;
            string langVal = string.IsNullOrEmpty(lang) ? "en" : lang;

            int offset = (pageVal - 1) * pageSizeVal;

            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            bool filterByCategory = !string.IsNullOrEmpty(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase) && int.TryParse(category, out _);

            // 1. Get total count (same translation joins as the items query — the
            // search filter matches translated text, so the count must resolve it too)
            var countSql = @"
                SELECT COUNT(1) FROM products p
                LEFT JOIN product_translations req ON req.product_id = p.product_id AND req.language_code = @language
                LEFT JOIN product_translations fb ON fb.product_id = p.product_id AND fb.language_code = 'en'
                WHERE p.is_deleted = 0 AND p.is_displayed = 1";
            if (filterByCategory)
            {
                countSql += " AND p.category = @categoryId";
            }
            if (!string.IsNullOrEmpty(search))
            {
                countSql += " AND (COALESCE(req.title, fb.title) LIKE @search OR COALESCE(req.description, fb.description) LIKE @search)";
            }
            int totalCount = 0;
            await using (var countCmd = new MySqlCommand(countSql, conn))
            {
                countCmd.Parameters.AddWithValue("@language", langVal);
                if (filterByCategory)
                {
                    countCmd.Parameters.AddWithValue("@categoryId", int.Parse(category!));
                }
                if (!string.IsNullOrEmpty(search))
                {
                    countCmd.Parameters.AddWithValue("@search", $"%{search}%");
                }
                totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            }

            // 2. Get items (LEFT JOIN product_parts so each product carries its associated part_ids;
            // double LEFT JOIN product_translations resolves requested-language text with an 'en' fallback)
            var itemsSql = @"
                SELECT p.product_id,
                       COALESCE(req.title, fb.title) AS title,
                       COALESCE(req.subtitle, fb.subtitle) AS subtitle,
                       COALESCE(req.description, fb.description) AS description,
                       p.category, p.subcategory, p.price, p.image_urls,
                       GROUP_CONCAT(pp.part_id) AS part_ids
                FROM products p
                LEFT JOIN product_parts pp ON pp.product_id = p.product_id
                LEFT JOIN product_translations req ON req.product_id = p.product_id AND req.language_code = @language
                LEFT JOIN product_translations fb ON fb.product_id = p.product_id AND fb.language_code = 'en'
                WHERE p.is_deleted = 0 AND p.is_displayed = 1";
            if (filterByCategory)
            {
                itemsSql += " AND p.category = @categoryId";
            }
            if (!string.IsNullOrEmpty(search))
            {
                itemsSql += " AND (COALESCE(req.title, fb.title) LIKE @search OR COALESCE(req.description, fb.description) LIKE @search)";
            }
            itemsSql += " GROUP BY p.product_id ORDER BY p.created_at DESC LIMIT @limit OFFSET @offset";

            var items = new List<object>();
            await using (var itemsCmd = new MySqlCommand(itemsSql, conn))
            {
                itemsCmd.Parameters.AddWithValue("@language", langVal);
                if (filterByCategory)
                {
                    itemsCmd.Parameters.AddWithValue("@categoryId", int.Parse(category!));
                }
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
                            : System.Text.Json.JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("image_urls"))) ?? new List<string>(),
                        part_ids = reader.IsDBNull(reader.GetOrdinal("part_ids"))
                            ? new List<string>()
                            : new List<string>(reader.GetString(reader.GetOrdinal("part_ids")).Split(','))
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
        group.MapGet("/parts", async (HttpContext context, int? page, int? pageSize, string? search, string? lang) =>
        {
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var connectionString = GetConnectionString(config);

            int pageVal = page ?? 1;
            int pageSizeVal = pageSize ?? 10;
            if (pageVal < 1) pageVal = 1;
            if (pageSizeVal < 1 || pageSizeVal > 100) pageSizeVal = 10;
            string langVal = string.IsNullOrEmpty(lang) ? "en" : lang;

            int offset = (pageVal - 1) * pageSizeVal;

            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            // 1. Get total count (same translation joins as the items query)
            var countSql = @"
                SELECT COUNT(1) FROM parts pa
                LEFT JOIN part_translations req ON req.part_id = pa.part_id AND req.language_code = @language
                LEFT JOIN part_translations fb ON fb.part_id = pa.part_id AND fb.language_code = 'en'";
            if (!string.IsNullOrEmpty(search))
            {
                countSql += " WHERE (COALESCE(req.title, fb.title) LIKE @search OR COALESCE(req.description, fb.description) LIKE @search)";
            }
            int totalCount = 0;
            await using (var countCmd = new MySqlCommand(countSql, conn))
            {
                countCmd.Parameters.AddWithValue("@language", langVal);
                if (!string.IsNullOrEmpty(search))
                {
                    countCmd.Parameters.AddWithValue("@search", $"%{search}%");
                }
                totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            }

            // 2. Get items (double LEFT JOIN part_translations resolves requested-language text with an 'en' fallback)
            var itemsSql = @"
                SELECT pa.part_id,
                       COALESCE(req.title, fb.title) AS title,
                       COALESCE(req.description, fb.description) AS description,
                       pa.price, pa.image_urls
                FROM parts pa
                LEFT JOIN part_translations req ON req.part_id = pa.part_id AND req.language_code = @language
                LEFT JOIN part_translations fb ON fb.part_id = pa.part_id AND fb.language_code = 'en'";
            if (!string.IsNullOrEmpty(search))
            {
                itemsSql += " WHERE (COALESCE(req.title, fb.title) LIKE @search OR COALESCE(req.description, fb.description) LIKE @search)";
            }
            itemsSql += " ORDER BY pa.created_at DESC LIMIT @limit OFFSET @offset";

            var items = new List<object>();
            await using (var itemsCmd = new MySqlCommand(itemsSql, conn))
            {
                itemsCmd.Parameters.AddWithValue("@language", langVal);
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
        group.MapGet("/categories", async (HttpContext context, string? lang) =>
        {
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var connectionString = GetConnectionString(config);
            string langVal = string.IsNullOrEmpty(lang) ? "en" : lang;

            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            var itemsSql = @"
                SELECT c.id, COALESCE(req.name, fb.name) AS name, c.slug
                FROM categories c
                LEFT JOIN category_translations req ON req.category_id = c.id AND req.language_code = @language
                LEFT JOIN category_translations fb ON fb.category_id = c.id AND fb.language_code = 'en'
                WHERE c.number_of_active_products > 0
                ORDER BY name ASC";
            var items = new List<object>();

            await using (var cmd = new MySqlCommand(itemsSql, conn))
            {
                cmd.Parameters.AddWithValue("@language", langVal);
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
