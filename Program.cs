using DotNetEnv;
using TatoToys.Application;
using TatoToys.Infrastructure;

// Load .env file before building the host so env vars are available to IConfiguration.
// clobberExistingVars: false — real Docker/system env vars always win over the .env file.
Env.Load(options: new LoadOptions(setEnvVars: true, clobberExistingVars: false));

var builder = WebApplication.CreateBuilder(args);

// Don't advertise the stack ("Server: Kestrel").
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// Central error handling: unhandled exceptions and empty 4xx bodies (bad JSON, unknown
// route, wrong method) become RFC 7807 JSON instead of an empty response.
builder.Services.AddProblemDetails();

// Add services to the container.
builder.Services.AddOpenApi();

// CorsOrigin comes from env var (set by .env or Docker compose), falls back to appsettings.json
var allowedOrigin = builder.Configuration["CorsOrigin"] ?? "http://localhost:4200";

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(allowedOrigin)
              .AllowCredentials()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Clean Architecture: register Application and Infrastructure layers
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Run database table creation on startup (idempotent — safe to call every boot)
await app.Services.GetRequiredService<TatoToys.Infrastructure.Data.DatabaseInitializer>().InitializeAsync();

// First in the pipeline so even short-circuited responses (429s, redirects) carry them.
app.UseSecurityHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

// HSTS tells browsers to refuse plain HTTP for this host. Production only —
// on localhost it would pin the dev machine to HTTPS and break local work.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors("AllowAngular");

// Before auth and endpoints: rejected traffic should cost as little as possible.
// Must follow UseCors so 429 responses still carry CORS headers and the browser
// can read them instead of reporting an opaque network error.
app.UseRedisRateLimiting();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapAuthEndpoints();
app.MapPasswordResetEndpoints();
app.MapProductEndpoints();
app.MapFavoritesEndpoints();
app.MapContactEndpoints();
app.MapStoreHoursEndpoints();

app.Run();
