
using MySql.Data.MySqlClient;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.Json.Serialization;

public static class StoreHoursEndpoints
{
    // Shared with the admin API (which deletes this key on save) and the
    // node/flask storefront backends. Keep the string identical across services.
    private const string CacheKey = "store_hours:all";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private const string DefaultTimeZone = "Asia/Jerusalem";

    public static void MapStoreHoursEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        // GET /api/store-hours — public: weekly schedule + whether the shop is open right now.
        group.MapGet("/store-hours", async (HttpContext context) =>
        {
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var multiplexer = context.RequestServices.GetService<IConnectionMultiplexer>();

            List<StoreHourRow>? days = null;

            // 1. Try the cache. Redis being down must not take the storefront with it.
            try
            {
                if (multiplexer != null)
                {
                    var cached = await multiplexer.GetDatabase().StringGetAsync(CacheKey);
                    if (cached.HasValue)
                    {
                        // Cast explicitly: RedisValue converts implicitly to both string and
                        // ReadOnlySpan<byte>, which leaves the Deserialize overload ambiguous.
                        var json = (string?)cached;
                        if (!string.IsNullOrEmpty(json))
                            days = JsonSerializer.Deserialize<List<StoreHourRow>>(json);
                    }
                }
            }
            catch
            {
                // fall through to the database
            }

            // 2. Cache miss — read through to MySQL and repopulate.
            if (days == null)
            {
                days = await LoadFromDatabaseAsync(GetConnectionString(config));

                try
                {
                    if (multiplexer != null)
                    {
                        await multiplexer.GetDatabase().StringSetAsync(
                            CacheKey, JsonSerializer.Serialize(days), CacheTtl);
                    }
                }
                catch
                {
                    // caching is best-effort
                }
            }

            // 3. is_open_now is always computed fresh — never cached, or a stale "true"
            //    could outlive closing time by up to the full TTL.
            var timeZoneId = config["Store:TimeZone"] ?? DefaultTimeZone;
            var isOpenNow = ComputeIsOpenNow(days, timeZoneId, out var localNow);

            return Results.Ok(new
            {
                timezone = timeZoneId,
                server_time = localNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                is_open_now = isOpenNow,
                days = days.ConvertAll(d => new
                {
                    day_of_week = d.DayOfWeek,
                    open_time = d.OpenTime,
                    close_time = d.CloseTime,
                    is_closed = d.IsClosed,
                }),
            });
        });
    }

    private static async Task<List<StoreHourRow>> LoadFromDatabaseAsync(string connectionString)
    {
        var rows = new List<StoreHourRow>();

        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT day_of_week, open_time, close_time, is_closed FROM store_hours ORDER BY day_of_week ASC", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var open = reader.GetFieldValue<TimeSpan>(reader.GetOrdinal("open_time"));
            var close = reader.GetFieldValue<TimeSpan>(reader.GetOrdinal("close_time"));

            rows.Add(new StoreHourRow
            {
                DayOfWeek = reader.GetInt32(reader.GetOrdinal("day_of_week")),
                OpenTime = FormatTime(open),
                CloseTime = FormatTime(close),
                IsClosed = reader.GetBoolean(reader.GetOrdinal("is_closed")),
            });
        }

        return rows;
    }

    private static bool ComputeIsOpenNow(List<StoreHourRow> days, string timeZoneId, out DateTime localNow)
    {
        localNow = ResolveNow(timeZoneId);

        // .NET DayOfWeek already maps Sunday = 0 .. Saturday = 6.
        var today = (int)localNow.DayOfWeek;
        var row = days.Find(d => d.DayOfWeek == today);

        if (row == null || row.IsClosed) return false;
        if (!TryParseTime(row.OpenTime, out var open)) return false;
        if (!TryParseTime(row.CloseTime, out var close)) return false;

        var now = localNow.TimeOfDay;
        return now >= open && now < close;
    }

    private static DateTime ResolveNow(string timeZoneId)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch (TimeZoneNotFoundException)
        {
            // Container without tzdata — fall back to server local rather than 500.
            return DateTime.Now;
        }
        catch (InvalidTimeZoneException)
        {
            return DateTime.Now;
        }
    }

    private static string FormatTime(TimeSpan value) =>
        $"{(int)value.TotalHours:D2}:{value.Minutes:D2}";

    private static bool TryParseTime(string? raw, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return TimeSpan.TryParseExact(raw.Trim(), new[] { @"hh\:mm", @"hh\:mm\:ss" },
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static string GetConnectionString(IConfiguration config)
    {
        var raw = config["MySQL:ConnectionString"] ?? config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var builder = new MySqlConnectionStringBuilder(raw);
        if (!string.IsNullOrEmpty(config["MySQL:Server"])) builder.Server = config["MySQL:Server"];
        if (!string.IsNullOrEmpty(config["MySQL:Port"]) && uint.TryParse(config["MySQL:Port"], out var port)) builder.Port = port;
        if (!string.IsNullOrEmpty(config["MySQL:Database"])) builder.Database = config["MySQL:Database"];
        if (!string.IsNullOrEmpty(config["MySQL:User"])) builder.UserID = config["MySQL:User"];
        if (!string.IsNullOrEmpty(config["MySQL:Password"])) builder.Password = config["MySQL:Password"];
        return builder.ConnectionString;
    }

    // Cached shape. The node and flask backends read and write this SAME Redis key,
    // so the JSON field names must stay snake_case to match what they emit —
    // otherwise whichever service populates the cache breaks the other two.
    private class StoreHourRow
    {
        [JsonPropertyName("day_of_week")]
        public int DayOfWeek { get; set; }

        [JsonPropertyName("open_time")]
        public string OpenTime { get; set; } = "00:00";

        [JsonPropertyName("close_time")]
        public string CloseTime { get; set; } = "00:00";

        [JsonPropertyName("is_closed")]
        public bool IsClosed { get; set; }
    }
}
