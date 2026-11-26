using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OutfitFinder.Identity;
using OutfitFinder.Infrastructure.Data;

namespace OutfitFinder.Tests.Integration;

public class IdentityFactory : WebApplicationFactory<IdentityMarker>
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "of-tests", "id-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_folder);
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:OutfitDb", $"Data Source={Path.Combine(_folder, "id.db")}");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Clients:Web:Url", "https://web.test");
        builder.UseSetting("Clients:Web:Secret", "test-secret");
        builder.UseSetting("Admin:Username", "boss");
        builder.UseSetting("Turnstile:SiteKey", "");
        builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning));
    }

    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
}

public partial class IdentityTests : IClassFixture<IdentityFactory>
{
    private readonly IdentityFactory _f;
    public IdentityTests(IdentityFactory f) => _f = f;

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")] private static partial Regex Token();

    private static async Task<HttpResponseMessage> PostForm(HttpClient c, string path, Dictionary<string, string> fields)
    {
        var page = await c.GetStringAsync(path);
        fields["__RequestVerificationToken"] = Token().Match(page).Groups[1].Value;
        return await c.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private static Dictionary<string, string> Register(string user, string password, bool agree = true) => new()
    {
        ["Input.UserName"] = user, ["Input.Password"] = password, ["Input.ConfirmPassword"] = password,
        ["Input.Agree"] = agree ? "true" : "false",
    };

    [Fact]
    public async Task Discovery_document_is_served()
    {
        var res = await _f.Browser().GetAsync("/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("outfit.api", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_sign_out_sign_in()
    {
        var c = _f.Browser();
        var user = "u" + Guid.NewGuid().ToString("N")[..10];
        var reg = await PostForm(c, "/Account/Register", Register(user, "correct horse battery"));
        Assert.Equal(HttpStatusCode.Redirect, reg.StatusCode);
        Assert.StartsWith("https://web.test", reg.Headers.Location!.ToString());

        // Consent for Terms + Privacy recorded with versions.
        await using (var scope = _f.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var id = (await users.FindByNameAsync(user))!.Id;
            var consents = await scope.ServiceProvider.GetRequiredService<OutfitDbContext>().Consents.Where(x => x.UserId == id).ToListAsync();
            Assert.Equal(["privacy", "terms"], consents.Select(x => x.Document).Order());
        }

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/Account/Logout")).StatusCode);

        var bad = await PostForm(c, "/Account/Login", new() { ["Input.UserName"] = user, ["Input.Password"] = "wrong password" });
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("don&#x27;t match", await bad.Content.ReadAsStringAsync());

        var ok = await PostForm(c, "/Account/Login", new() { ["Input.UserName"] = user, ["Input.Password"] = "correct horse battery" });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
    }

    [Fact]
    public async Task Register_requires_consent_and_a_10_character_password()
    {
        var c = _f.Browser();
        var noAgree = await PostForm(c, "/Account/Register", Register("u" + Guid.NewGuid().ToString("N")[..8], "long enough pass", agree: false));
        Assert.Contains("16 or older", await noAgree.Content.ReadAsStringAsync());
        var shortPw = await PostForm(c, "/Account/Register", Register("u" + Guid.NewGuid().ToString("N")[..8], "short"));
        Assert.Contains("at least 10", await shortPw.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Account_locks_after_5_failed_attempts()
    {
        var c = _f.Browser();
        var user = "lock" + Guid.NewGuid().ToString("N")[..8];
        await PostForm(c, "/Account/Register", Register(user, "the right password"));
        await c.GetAsync("/Account/Logout");
        HttpResponseMessage last = null!;
        for (var i = 0; i < 5; i++)
            last = await PostForm(c, "/Account/Login", new() { ["Input.UserName"] = user, ["Input.Password"] = "nope nope nope" });
        Assert.Contains("locked", await last.Content.ReadAsStringAsync());
        // Even the right password is refused while locked.
        var right = await PostForm(c, "/Account/Login", new() { ["Input.UserName"] = user, ["Input.Password"] = "the right password" });
        Assert.Contains("locked", await right.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Configured_username_gets_the_admin_role()
    {
        var c = _f.Browser();
        await PostForm(c, "/Account/Register", Register("boss", "boss password 123"));
        await using var scope = _f.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.IsInRoleAsync((await users.FindByNameAsync("boss"))!, Roles.Admin));
    }

    [Fact]
    public async Task Reset_page_without_a_valid_link_offers_a_new_link_instead_of_the_form()
    {
        var c = _f.Browser();
        foreach (var path in new[] { "/Account/ResetPassword", "/Account/ResetPassword?userId=nobody&token=abc" })
        {
            var html = await c.GetStringAsync(path);
            Assert.Contains("This link doesn't work", html);
            Assert.DoesNotContain("Save new password", html);
        }

        await PostForm(c, "/Account/Register", Register("resetter", "long-enough-password"));
        await using var scope = _f.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByNameAsync("resetter"))!;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var ok = await c.GetStringAsync($"/Account/ResetPassword?userId={user.Id}&token={Uri.EscapeDataString(token)}");
        Assert.Contains("Save new password", ok);
    }
}
