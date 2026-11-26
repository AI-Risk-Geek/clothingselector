using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OutfitFinder.Api;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Contracts;
using OutfitFinder.UI.Services;

namespace OutfitFinder.Tests.Integration;

/// <summary>
/// The real Api host on SQLite + local file blobs, with the explicit Demo AI provider and the development
/// moderation bypass (both refused outside Development). Each factory gets its own database and blob folder.
/// </summary>
public class ApiFactory : WebApplicationFactory<ApiMarker>
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "of-tests", Guid.NewGuid().ToString("N"));
    public string BlobRoot => Path.Combine(Folder, "blobs");
    public Func<IImageModerator?> Moderator { get; set; } = () => null;
    public string AiProvider { get; init; } = "Demo";
    public bool UseTestAuth { get; init; } = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Folder);
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:OutfitDb", $"Data Source={Path.Combine(Folder, "test.db")}");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Storage:Provider", "FileSystem");
        builder.UseSetting("Storage:Root", BlobRoot);
        builder.UseSetting("AI:Provider", AiProvider);
        builder.UseSetting("Safety:Mode", "DevelopmentBypass");
        builder.UseSetting("Identity:Authority", "https://identity.invalid");
        builder.UseSetting("Api:UrlSigningKey", "test-signing-key");
        builder.UseSetting("Limits:RequireCaptchaForAnonymousUpload", "false");
        builder.UseSetting("Limits:DailyBudgetUsd", "0");
        builder.UseSetting("Limits:SearchesPerVisitorPerDay", "50");
        builder.UseSetting("Worker:PollMilliseconds", "100");
        builder.UseSetting("Worker:StuckAfterSeconds", "60");
        builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning).AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.Error));
        if (Environment.GetEnvironmentVariable("OF_TEST_LOG") == "1") builder.ConfigureLogging(l => l.AddConsole());
        builder.ConfigureTestServices(services =>
        {
            if (Moderator() is { } m) services.AddSingleton(m);
            if (UseTestAuth)
            {
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                });
            }
        });
    }

    public HttpClient Visitor(Guid? visitorId = null)
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Add(Headers.VisitorId, (visitorId ?? Guid.NewGuid()).ToString());
        return c;
    }

    public HttpClient User(string userId, string? role = null, Guid? visitorId = null)
    {
        var c = Visitor(visitorId);
        c.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        if (role is not null) c.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        return c;
    }

    public static MultipartFormDataContent Photo(byte[]? bytes = null)
    {
        bytes ??= File.ReadAllBytes(SamplePath);
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "photo", "photo.jpg");
        return form;
    }

    public static string SamplePath
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OutfitFinder.sln"))) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "src", "OutfitFinder.Api", "Samples", "sample-outfit.jpg");
        }
    }

    public static async Task<SearchDto> WaitForFinish(HttpClient client, Guid id, int seconds = 30)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            var s = await client.GetFromJsonAsync<SearchDto>($"api/searches/{id}", OutfitApiClient.Json);
            if (s!.IsFinished) return s;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Search {id} did not finish in {seconds}s");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(Folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Signs a request in as X-Test-User (with optional X-Test-Role). No header: anonymous.</summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
    : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    public const string SchemeName = "Test", UserHeader = "X-Test-User", RoleHeader = "X-Test-Role";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new("sub", user!), new("name", user!) };
        if (Request.Headers.TryGetValue(RoleHeader, out var role)) claims.Add(new Claim("role", role!));
        var id = new ClaimsIdentity(claims, SchemeName, "name", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), SchemeName)));
    }
}
