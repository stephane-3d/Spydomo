using Azure.Communication.Email;
using Clerk.BackendAPI;
using Clerk.Net.AspNetCore.Security;
using Clerk.Net.DependencyInjection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Spydomo.Infrastructure;
using Spydomo.Infrastructure.Clerk;
using Spydomo.Infrastructure.Clients;
using Spydomo.Infrastructure.Extensions;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using Spydomo.Web.Classes;
using Spydomo.Web.Components;
using System.Globalization;
using System.Text.Json.Serialization;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);


        // -------------------------
        // 🌐 Web + Blazor Services
        // -------------------------

        builder.Services.AddAntiforgery();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddRazorPages();
        builder.Services.AddServerSideBlazor();
        builder.Services.AddMudServices();

        builder.Services.AddHttpContextAccessor();

        builder.Services.AddHttpClient("Default", (sp, client) =>
        {
            var http = sp.GetRequiredService<IHttpContextAccessor>();
            var req = http.HttpContext?.Request;

            if (req is null)
            {
                // fallback (background contexts with no request)
                client.BaseAddress = new Uri(builder.Configuration["App:BaseUrl"] ?? "https://spydomo.com/");
                return;
            }

            client.BaseAddress = new Uri($"{req.Scheme}://{req.Host}/");
        });


        builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("Default"));

        builder.Services.AddScoped<IBrowserStorage, BrowserStorage>();
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<UserState>();

        builder.Services.AddScoped<DatasheetState>();
        builder.Services.AddScoped<DashboardState>();

        // shared core services
        builder.Services.AddSpydomoShared();
        builder.Services.AddNotifications(builder.Configuration);
        builder.Services.AddSpydomoWeb();

        builder.Services.AddSingleton(_ =>
            new EmailClient(builder.Configuration["AcsEmail:ConnectionString"]
                ?? throw new InvalidOperationException("Missing config: AcsEmail:ConnectionString")));

        builder.Services.AddHttpClient<ISlackNotifier, SlackNotifier>();

        builder.Services.AddSingleton<Stripe.IStripeClient>(_ =>
            new Stripe.StripeClient(builder.Configuration["Stripe:SecretKey"]));


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
        // ⚙️ Public facing services for the website
        // ------------------------- 

        builder.Services.AddScoped<MetaService>();

        builder.Services.Configure<ClerkOptions>(
            builder.Configuration.GetSection("Clerk"));

        builder.Services.AddClerkApiClient(config =>
        {
            config.SecretKey = builder.Configuration["Clerk:SecretKey"];
        });

        Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();

        builder.Services
            .AddAuthentication(ClerkAuthenticationDefaults.AuthenticationScheme)
            .AddClerkAuthentication(options =>
            {
                options.Authority = builder.Configuration["Clerk:Authority"];
                options.AuthorizedParty = builder.Configuration["Clerk:AppUrl"];
            });

        builder.Services.AddSingleton(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var secretKey = configuration["Clerk:SecretKey"];
            return new ClerkBackendApi(bearerAuth: secretKey);
        });

        builder.Services.AddScoped<ClerkBackend>();

        // -------------------------
        // ⚙️ Misc
        // -------------------------

        builder.Services.AddApplicationInsightsTelemetry();

        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

        builder.Services.AddLogging(logging =>
        {
            logging.AddConsole();
        });

        builder.Services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        // Worker Admin Client
        builder.Services.Configure<WorkerAdminOptions>(builder.Configuration.GetSection("Worker"));

        builder.Services.AddHttpClient<IWorkerAdminClient, WorkerAdminClient>((sp, client) =>
        {
            var opt = sp.GetRequiredService<IOptions<WorkerAdminOptions>>().Value;
            client.BaseAddress = new Uri(opt.BaseUrl); // "https://worker.spydomo.com"
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        var app = builder.Build();

        var culture = CultureInfo.GetCultureInfo("en-CA"); // or en-US
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        app.UseRequestLocalization(new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(culture),
            SupportedCultures = new[] { culture },
            SupportedUICultures = new[] { culture }
        });

        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });

        if (app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        // Redirect to spydomo.com
        app.Use(async (context, next) =>
        {
            var host = context.Request.Host.Host;
            if (host.Equals("spydomo.ai", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("www.spydomo.com", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("www.spydomo.ai", StringComparison.OrdinalIgnoreCase))
            {
                var newUrl = $"https://spydomo.com{context.Request.Path}{context.Request.QueryString}";
                context.Response.Redirect(newUrl, permanent: true);
                return;
            }

            await next();
        });

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        // Map signup and signin html files
        app.Use(async (ctx, next) =>
        {
            if (HttpMethods.IsGet(ctx.Request.Method))
            {
                var p = ctx.Request.Path.Value ?? "";

                if (string.Equals(p, "/app/signup", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p, "/app/signup/", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Request.Path = "/auth/sign-up.html";
                }
                else if (string.Equals(p, "/app/login", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(p, "/app/login/", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Request.Path = "/auth/sign-in.html";
                }
            }

            await next();
        });

        app.UseStaticFiles();
        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        // ✅ 401/403 redirect handler for browser navigations (HTML GET). Never redirect API/XHR.
        app.UseStatusCodePages(async ctx =>
        {
            var req = ctx.HttpContext.Request;
            var path = req.Path;

            var isGet = HttpMethods.IsGet(req.Method);

            // Treat missing Accept as HTML navigation (common for some navigations)
            var accept = req.Headers.Accept.ToString();
            var acceptsHtml = string.IsNullOrEmpty(accept) ||
                              accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);

            if (!isGet || !acceptsHtml)
                return;

            // Exclusions: don't redirect these (prevents loops / breaks infra requests)
            if (path.StartsWithSegments("/hangfire") ||
                path.StartsWithSegments("/auth") ||
                path.StartsWithSegments("/api") ||
                path.StartsWithSegments("/_blazor") ||
                path.StartsWithSegments("/_framework") ||
                path.Equals("/app/login", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/app/signup", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/404", StringComparison.OrdinalIgnoreCase) ||          
                path.Equals("/404/", StringComparison.OrdinalIgnoreCase))
                return;

            if (ctx.HttpContext.Response.HasStarted)
                return;

            var status = ctx.HttpContext.Response.StatusCode;

            if (status == StatusCodes.Status401Unauthorized)
            {
                ctx.HttpContext.Response.Redirect("/app/login");
            }
            else if (status == StatusCodes.Status403Forbidden)
            {
                ctx.HttpContext.Response.Redirect("/app/unauthorized");
            }
            else if (status == StatusCodes.Status404NotFound)
            {
                ctx.HttpContext.Response.Redirect("/404");
            }
        });

        app.UseWhen(
            c => c.Request.Path.StartsWithSegments("/app"),
            b => b.UseMiddleware<ClerkUserSyncMiddleware>()
        );

        app.UseAntiforgery();

        // 403 Forbidden handler page
        app.MapGet("/app/unauthorized", async context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync("""
                <!doctype html><html><head><title>403 – Forbidden</title>
                <meta name="robots" content="noindex">
                <style>body{font-family:system-ui,-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;padding:40px}
                .box{max-width:640px;margin:auto;padding:24px;border:1px solid #e5e7eb;border-radius:12px}
                h1{margin:0 0 8px 0}p{margin:6px 0}</style></head>
                <body><div class="box">
                <h1>403 — Forbidden</h1>
                <p>You don’t have permission to access this page.</p>
                <p><a href="/app/dashboard">Return to Market Pulse</a></p>
                </div></body></html>
                """);
        });

        // For  warmups and health checks
        app.MapGet("/healthz", () => Results.Ok("ok"));

        // SEO endpoints from Spydomo.Infrastructure.seo
        app.MapSpydomoSitemaps();

        // Map endpoints (use the modern style consistently)
        app.MapControllers();
        app.MapRazorPages();

        app.MapRazorComponents<App>()
           .AddInteractiveServerRenderMode();

        app.Run();

    }
}