using DotNetEnv;
using TatoToys.Application;
using TatoToys.Infrastructure;

// Load .env file before building the host so env vars are available to IConfiguration.
// clobberExistingVars: false — real Docker/system env vars always win over the .env file.
Env.Load(options: new LoadOptions(setEnvVars: true, clobberExistingVars: false));

var builder = WebApplication.CreateBuilder(args);

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

app.UseCors("AllowAngular");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapAuthEndpoints();
app.MapPasswordResetEndpoints();
app.MapProductEndpoints();
app.MapFavoritesEndpoints();

app.Run();
