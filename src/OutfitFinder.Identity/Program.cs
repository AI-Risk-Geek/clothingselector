using Duende.IdentityServer.EntityFramework.DbContexts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Identity;
using OutfitFinder.Infrastructure.Data;
using OutfitFinder.Infrastructure.Safety;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

if (!string.IsNullOrEmpty(config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddApplicationInsightsTelemetry();

builder.Services.AddOutfitDatabase(config);
builder.Services.AddDataProtection().SetApplicationName("OutfitFinder.Identity").PersistKeysToDbContext<OutfitDbContext>();
builder.Services.AddRazorPages();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
{
    o.Password.RequiredLength = 10;
    o.Password.RequireDigit = false;
    o.Password.RequireLowercase = false;
    o.Password.RequireUppercase = false;
    o.Password.RequireNonAlphanumeric = false;
    o.Password.RequiredUniqueChars = 4;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    o.Lockout.AllowedForNewUsers = true;
    o.User.RequireUniqueEmail = false; // email is optional (decision 3)
    o.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._";
    o.ClaimsIdentity.UserIdClaimType = "sub";
    o.ClaimsIdentity.UserNameClaimType = "name";
    o.ClaimsIdentity.RoleClaimType = "role";
})
.AddEntityFrameworkStores<AppIdentityDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddIdentityServer(o =>
    {
        o.Events.RaiseErrorEvents = true;
        o.Events.RaiseFailureEvents = true;
        o.UserInteraction.LoginUrl = "/Account/Login";
        o.UserInteraction.LogoutUrl = "/Account/Logout";
        o.UserInteraction.ErrorUrl = "/Error";
        o.LicenseKey = config["Duende:LicenseKey"];
    })
    .AddInMemoryIdentityResources(Config.IdentityResources)
    .AddInMemoryApiScopes(Config.ApiScopes)
    .AddInMemoryApiResources(Config.ApiResources)
    .AddInMemoryClients(Config.Clients(config).ToList())
    .AddAspNetIdentity<ApplicationUser>()
    .AddOperationalStore(o =>
    {
        o.ConfigureDbContext = b => b.UseConfiguredProvider(config, "__EFMigrationsHistory_Grants", typeof(Program).Assembly.FullName);
        o.EnableTokenCleanup = true;
    });

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "of_identity";
    o.LoginPath = "/Account/Login";
});

builder.Services.AddHttpClient("turnstile");
builder.Services.AddSingleton<ICaptchaVerifier>(sp => new TurnstileVerifier(sp.GetRequiredService<IHttpClientFactory>().CreateClient("turnstile"),
    config["Turnstile:SecretKey"], sp.GetRequiredService<ILogger<TurnstileVerifier>>()));

if (!string.IsNullOrEmpty(config["Smtp:Host"])) builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
else if (builder.Environment.IsDevelopment()) builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
else throw new InvalidOperationException("Smtp:Host is required outside Development (password reset emails).");

var app = builder.Build();

if (config.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
{
    await DatabaseSetup.MigrateAsync(app.Services, config);
    using var scope = app.Services.CreateScope();
    var grants = scope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>();
    if ((config["Database:Provider"] ?? "MySql") == "Sqlite") await DatabaseSetup.CreateTablesIfMissing(grants, default);
    else await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync(grants.Database);
}
await AdminSeeder.SeedAsync(app.Services, config);

if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseExceptionHandler("/Error");
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseIdentityServer();
app.UseAuthorization();
app.MapRazorPages();
app.MapGet("/", (IConfiguration c) => Results.Redirect(c["Clients:Web:Url"] ?? "/Account/Login"));

app.Run();
