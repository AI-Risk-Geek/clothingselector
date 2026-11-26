using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using OutfitFinder.Infrastructure.Data;

namespace OutfitFinder.Identity;

/// <summary>Development only: writes emails (password reset links) to the log.</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> log) : IEmailSender
{
    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        log.LogWarning("DEV EMAIL to {Email}: {Subject}\n{Body}", email, subject, htmlMessage);
        return Task.CompletedTask;
    }
}

public sealed class SmtpEmailSender(IConfiguration config) : IEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        using var client = new SmtpClient(config["Smtp:Host"], config.GetValue("Smtp:Port", 587))
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(config["Smtp:User"], config["Smtp:Password"]),
        };
        using var msg = new MailMessage(config["Smtp:From"]!, email, subject, htmlMessage) { IsBodyHtml = true };
        await client.SendMailAsync(msg);
    }
}

/// <summary>The Admin role exists; the configured username (Admin:Username) is in it.</summary>
public static class AdminSeeder
{
    public static async Task SeedAsync(IServiceProvider sp, IConfiguration config)
    {
        using var scope = sp.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(Roles.Admin)) await roles.CreateAsync(new IdentityRole(Roles.Admin));
        await EnsureAdminAsync(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), config, config["Admin:Username"]);
    }

    public static async Task EnsureAdminAsync(UserManager<ApplicationUser> users, IConfiguration config, string? userName)
    {
        var adminName = config["Admin:Username"];
        if (string.IsNullOrWhiteSpace(adminName) || !string.Equals(adminName, userName, StringComparison.OrdinalIgnoreCase)) return;
        var user = await users.FindByNameAsync(adminName);
        if (user is not null && !await users.IsInRoleAsync(user, Roles.Admin)) await users.AddToRoleAsync(user, Roles.Admin);
    }
}
