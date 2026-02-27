using Hangfire;
using Hangfire.Dashboard;
using Hangfire.Dashboard.BasicAuthorization;
using Microsoft.EntityFrameworkCore;
using Spydomo.Infrastructure.Extensions;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.Worker;
using Spydomo.Worker.Classes;
using System.Net;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

var brightDataSection = builder.Configuration.GetSection("BrightData");
var brightDataProxyUrl = brightDataSection["ProxyUrl"];
var brightDataProxyUser = brightDataSection["ProxyUsername"];
var brightDataProxyPassword = brightDataSection["ProxyPassword"];

var readabilityBaseUrlRaw = builder.Configuration["ReadabilityService:BaseUrl"];

if (string.IsNullOrWhiteSpace(readabilityBaseUrlRaw))
    throw new InvalidOperationException("Missing config: ReadabilityService:BaseUrl");

var readabilityBaseUrl = readabilityBaseUrlRaw.TrimEnd('/') + "/";

builder.Services.AddControllers();

// -------------------------
// 🗃️ Database Setup
// -------------------------
var connectionString = builder.Configuration.GetConnectionString("SpydomoDB")
    ?? throw new InvalidOperationException("Database connection string not found.");

void ConfigureDb(DbContextOptionsBuilder options) =>
    options.UseSqlServer(connectionString, sql =>
        sql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null));

builder.Services.AddPooledDbContextFactory<SpydomoContext>(ConfigureDb);

// -------------------------
// 🌍 External Data Services
// -------------------------


builder.Services.AddHttpClient("NodeScraper", client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
});


// Named clients used by BrightDataService
builder.Services.AddHttpClient("BrightDataClient", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddHttpClient("BrightDataProxyPrimary", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
})
.ConfigurePrimaryHttpMessageHandler(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();

    var proxyUrl = cfg["BrightData:ProxyUrl"];               // brd.superproxy.io:33335
    var user = cfg["BrightData:ProxyUsernamePrimary"];   // ...-zone-spydomo_primary
    var pass = cfg["BrightData:ProxyPasswordPrimary"];
    var disableSsl = cfg.GetValue<bool>("BrightData:DisableProxySslValidation");

    var proxyUri = new Uri(proxyUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
        ? proxyUrl
        : "http://" + proxyUrl);

    var handler = new HttpClientHandler
    {
        Proxy = new WebProxy(proxyUri)
        {
            Credentials = new NetworkCredential(user, pass)
        },
        UseProxy = true,
        AutomaticDecompression = DecompressionMethods.All
    };

    if (disableSsl)
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

    return handler;
});

builder.Services.AddHttpClient("BrightDataProxySerp", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
})
.ConfigurePrimaryHttpMessageHandler(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();

    var proxyUrl = cfg["BrightData:ProxyUrl"];
    var user = cfg["BrightData:ProxyUsernameSerp"];      // ...-zone-spydomo_serp
    var pass = cfg["BrightData:ProxyPasswordSerp"];
    var disableSsl = cfg.GetValue<bool>("BrightData:DisableProxySslValidation");

    var proxyUri = new Uri(proxyUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
        ? proxyUrl
        : "http://" + proxyUrl);

    var handler = new HttpClientHandler
    {
        Proxy = new WebProxy(proxyUri)
        {
            Credentials = new NetworkCredential(user, pass)
        },
        UseProxy = true,
        AutomaticDecompression = DecompressionMethods.All
    };

    if (disableSsl)
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

    return handler;
});

builder.Services.AddHttpClient("BrightDataProxySerp", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
})
.ConfigurePrimaryHttpMessageHandler(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();

    var proxyUrl = cfg["BrightData:ProxyUrl"];
    var user = cfg["BrightData:ProxyUsernameSerp"];      // ...-zone-spydomo_serp
    var pass = cfg["BrightData:ProxyPasswordSerp"];
    var disableSsl = cfg.GetValue<bool>("BrightData:DisableProxySslValidation");

    var proxyUri = new Uri(proxyUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
        ? proxyUrl
        : "http://" + proxyUrl);

    var handler = new HttpClientHandler
    {
        Proxy = new WebProxy(proxyUri)
        {
            Credentials = new NetworkCredential(user, pass)
        },
        UseProxy = true,
        AutomaticDecompression = DecompressionMethods.All
    };

    if (disableSsl)
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

    return handler;
});

// -------------------------
// ⚙️ Misc
// -------------------------
builder.Services.AddApplicationInsightsTelemetryWorkerService();

builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
});

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// shared core services
builder.Services.AddSpydomoShared();
builder.Services.AddNotifications(builder.Configuration);
builder.Services.AddSpydomoWorker();

// scheduler for recurring jobs
builder.Services.AddHostedService<RecurringJobsScheduler>();

builder.Services.AddHangfire(config =>
    config.UseSqlServerStorage(connectionString));

//
// Server #1: pipeline/hourly/maintenance (normal throughput)
//
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = "worker-main";
    options.Queues = new[] { "pipeline", "hourly", "maintenance", "default" };
    options.WorkerCount = 8; // tune: 4–12 depending on your CPU + I/O
});

//
// Server #2: company-data only (serial)
//
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = "worker-company-data";
    options.Queues = new[] { "company-data" };
    options.WorkerCount = 1;  // ✅ guarantees only one company-data job at a time
});

builder.Services.AddScoped<IWorkerAdminClient, NoOpWorkerAdminClient>();

var app = builder.Build();

app.UseRouting();

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api/admin"))
    {
        var expected = builder.Configuration["Admin:ApiKey"];
        var provided = ctx.Request.Headers["X-Admin-Key"].ToString();

        if (string.IsNullOrWhiteSpace(expected) || provided != expected)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("Unauthorized");
            return;
        }
    }

    await next();
});

// Enable Hangfire Dashboard (for monitoring jobs)
if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new LocalRequestsOnlyAuthorizationFilter() }
    });
}
else
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[]
        {
            new BasicAuthAuthorizationFilter(new BasicAuthAuthorizationFilterOptions
            {
                RequireSsl = true,
                SslRedirect = true,
                LoginCaseSensitive = false,
                Users = new[]
                {
                    new BasicAuthAuthorizationUser
                    {
                        Login = builder.Configuration["Hangfire:DashboardUser"],
                        PasswordClear = builder.Configuration["Hangfire:DashboardPassword"]
                    }
                }
            })
        }
    });
}

app.MapControllers();

app.MapGet("/", () => "Spydomo worker is running.");

app.Run();
