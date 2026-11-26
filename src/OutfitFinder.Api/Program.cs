using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using OutfitFinder.Api.Endpoints;
using OutfitFinder.Api.Services;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Infrastructure;
using OutfitFinder.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

if (!string.IsNullOrEmpty(config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddApplicationInsightsTelemetry();

builder.Services.AddOutfitInfrastructure(config, builder.Environment);
builder.Services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<AppIdentityDbContext>();

builder.Services.AddScoped<SearchIntake>();
builder.Services.AddScoped<SearchPipeline>();
builder.Services.AddScoped<ItemAdjuster>();
builder.Services.AddScoped<UsageGuard>();
builder.Services.AddScoped<DataDeleter>();
builder.Services.AddSingleton<UrlSigner>();
builder.Services.AddSingleton<RetentionWorker>();
if (config.GetValue("Worker:Enabled", true))
{
    builder.Services.AddHostedService<SearchWorker>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<RetentionWorker>());
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.Authority = InfrastructureSetup.Require(config, "Identity:Authority");
    o.Audience = "outfit.api";
    o.MapInboundClaims = false;
    o.TokenValidationParameters.NameClaimType = "name";
    o.TokenValidationParameters.RoleClaimType = "role";
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AdminEndpoints.AdminPolicy, p => p.RequireAuthenticatedUser().RequireRole(Roles.Admin));

var extensionId = config["Extension:Id"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    var origins = new List<string>();
    if (!string.IsNullOrEmpty(extensionId)) origins.Add($"chrome-extension://{extensionId}");
    origins.AddRange(config.GetSection("Cors:Origins").Get<string[]>() ?? []);
    p.WithOrigins(origins.ToArray()).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Location");
}));
builder.Services.AddOutputCache();
builder.Services.AddRequestTimeouts();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 11 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 11 * 1024 * 1024);

var app = builder.Build();

if (config.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
    await DatabaseSetup.MigrateAsync(app.Services, config);

// Fail loudly at startup if the AI provider can't be resolved (searches will also fail with this message).
using (var scope = app.Services.CreateScope())
{
    try { (await scope.ServiceProvider.GetRequiredService<IAiSettingsProvider>().GetAsync(default)).EnsureConfigured(); }
    catch (AiNotConfiguredException e) { app.Logger.LogCritical("AI PROVIDER NOT CONFIGURED: {Message} Searches will fail until this is fixed.", e.Message); }
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();
app.UseRequestTimeouts();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/config", async (IAiSettingsProvider ai, IConfiguration cfg, CancellationToken ct) =>
{
    string provider;
    try { provider = (await ai.GetAsync(ct)).Provider.ToString(); } catch (AiNotConfiguredException) { provider = "NotConfigured"; }
    return Results.Ok(new Dictionary<string, string>
    {
        ["aiProvider"] = provider,
        ["turnstileSiteKey"] = cfg["Turnstile:SiteKey"] ?? "",
        ["captchaForAnonymous"] = cfg.GetValue("Limits:RequireCaptchaForAnonymousUpload", true).ToString(),
        ["retentionDays"] = cfg.GetValue("Retention:Days", 90).ToString(),
    });
});
app.MapSearchEndpoints();
app.MapPatternEndpoints();
app.MapMeEndpoints();
app.MapAdminEndpoints();

app.Run();

