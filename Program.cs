using DotNetEnv;
using StackExchange.Redis;
using TatoToys.Api.Services;

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

// Redis — single static ConnectionMultiplexer for the application lifetime.
// Reads from env vars (Redis__Host etc.) which override appsettings.json values.
var redisHost = builder.Configuration["Redis:Host"] ?? "127.0.0.1";
var redisPort = builder.Configuration["Redis:Port"] ?? "6379";
var redisPassword = builder.Configuration["Redis:Password"];

var redisConfig = new ConfigurationOptions
{
    EndPoints = { $"{redisHost}:{redisPort}" },
    Password = string.IsNullOrEmpty(redisPassword) ? null : redisPassword,
    ConnectTimeout = 5000,
    SyncTimeout = 3000,
    AbortOnConnectFail = false,
};

var multiplexer = ConnectionMultiplexer.Connect(redisConfig);

builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);

// MySQL — DatabaseService registered as scoped (one connection per request).
var rawConnectionString = builder.Configuration["MySQL:ConnectionString"]
    ?? builder.Configuration.GetConnectionString("DefaultConnection");

var connBuilder = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(rawConnectionString);

if (!string.IsNullOrEmpty(builder.Configuration["MySQL:Server"]))
{
    connBuilder.Server = builder.Configuration["MySQL:Server"];
}
if (!string.IsNullOrEmpty(builder.Configuration["MySQL:Port"]))
{
    if (uint.TryParse(builder.Configuration["MySQL:Port"], out var port))
    {
        connBuilder.Port = port;
    }
}
if (!string.IsNullOrEmpty(builder.Configuration["MySQL:Database"]))
{
    connBuilder.Database = builder.Configuration["MySQL:Database"];
}
if (!string.IsNullOrEmpty(builder.Configuration["MySQL:User"]))
{
    connBuilder.UserID = builder.Configuration["MySQL:User"];
}
if (!string.IsNullOrEmpty(builder.Configuration["MySQL:Password"]))
{
    connBuilder.Password = builder.Configuration["MySQL:Password"];
}

var mysqlConnectionString = connBuilder.ConnectionString;

builder.Services.AddScoped(_ => new DatabaseService(mysqlConnectionString));

var app = builder.Build();

if (multiplexer.IsConnected)
{
    app.Logger.LogInformation("✅ Successfully connected to Redis at startup!");
}
else
{
    app.Logger.LogError("❌ Failed to connect to Redis at startup.");
}

// Test MySQL connection at startup
try
{
    using var scope = app.Services.CreateScope();
    var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
    await dbService.TestConnectionAsync();
    app.Logger.LogInformation("✅ Successfully connected to MySQL at startup!");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "❌ MySQL connection failed at startup.");
}

app.UseCors("AllowAngular");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapAuthEndpoints();

app.Run();
