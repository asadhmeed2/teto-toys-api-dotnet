using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Per-IP request limits, counted in Redis so every instance shares one budget.
///
/// Fixed-window counter: INCR a key named for the current window, set its TTL on
/// first hit, reject once the count exceeds the limit. Cheap (one round trip) and
/// good enough to stop floods and credential stuffing. The trade-off versus a
/// sliding window is burstiness at window boundaries — a client can spend its full
/// budget at the end of one window and again at the start of the next.
/// </summary>
public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimit";

    public bool Enabled { get; set; } = true;

    /// <summary>Scopes counters per service so one API's traffic can't exhaust another's budget.</summary>
    public string ServiceName { get; set; } = "storefront";

    public int GlobalLimit { get; set; } = 100;
    public int GlobalWindowSeconds { get; set; } = 60;

    /// <summary>Login/refresh/register/reset are what actually get brute-forced.</summary>
    public int AuthLimit { get; set; } = 10;
    public int AuthWindowSeconds { get; set; } = 60;

    public string[] StrictPathPrefixes { get; set; } = ["/api/auth"];

    /// <summary>
    /// Only enable behind a proxy you control. X-Forwarded-For is client-settable,
    /// so trusting it on a directly-exposed service lets anyone forge their identity
    /// and sidestep the limit entirely.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }
}

public sealed class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitSettings _settings;
    private readonly IConnectionMultiplexer? _redis;

    public RateLimitingMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _next = next;
        _settings = configuration.GetSection(RateLimitSettings.SectionName).Get<RateLimitSettings>()
                    ?? new RateLimitSettings();
        _redis = serviceProvider.GetService<IConnectionMultiplexer>();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // CORS preflights are issued by the browser, not the caller — charging them
        // would halve every cross-origin client's effective budget.
        if (!_settings.Enabled || _redis == null || HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        var isStrict = _settings.StrictPathPrefixes
            .Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        var limit = isStrict ? _settings.AuthLimit : _settings.GlobalLimit;
        var window = Math.Max(1, isStrict ? _settings.AuthWindowSeconds : _settings.GlobalWindowSeconds);
        var scope = isStrict ? "auth" : "global";

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowIndex = now / window;
        var key = $"ratelimit:{_settings.ServiceName}:{scope}:{ResolveClientId(context)}:{windowIndex}";

        long count;
        try
        {
            var db = _redis.GetDatabase();
            count = await db.StringIncrementAsync(key);

            // Only the request that created the key sets the TTL, so the window
            // doesn't slide forward on every hit.
            if (count == 1)
            {
                await db.KeyExpireAsync(key, TimeSpan.FromSeconds(window));
            }
        }
        catch
        {
            // Fail open. A Redis outage must degrade to "unlimited", never to "down".
            await _next(context);
            return;
        }

        var resetSeconds = (int)Math.Max(1, ((windowIndex + 1) * window) - now);

        context.Response.Headers["X-RateLimit-Limit"] = limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, limit - count).ToString();
        context.Response.Headers["X-RateLimit-Reset"] = resetSeconds.ToString();

        if (count > limit)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = resetSeconds.ToString();
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "rate_limited",
                error_description = $"Too many requests. Please try again in {resetSeconds} seconds.",
            }));
            return;
        }

        await _next(context);
    }

    private string ResolveClientId(HttpContext context)
    {
        if (_settings.TrustForwardedHeaders)
        {
            var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                // Left-most entry is the original client.
                return forwarded.Split(',')[0].Trim();
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

public static class RateLimitingMiddlewareExtensions
{
    /// <summary>Register early — before auth and endpoints — so rejected traffic costs least.</summary>
    public static IApplicationBuilder UseRedisRateLimiting(this IApplicationBuilder app) =>
        app.UseMiddleware<RateLimitingMiddleware>();
}
