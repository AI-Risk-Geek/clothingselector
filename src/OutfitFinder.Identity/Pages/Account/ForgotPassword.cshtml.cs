using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OutfitFinder.Infrastructure.Data;

namespace OutfitFinder.Identity.Pages.Account;

public class ForgotPasswordModel(UserManager<ApplicationUser> users, IEmailSender email) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Enter your username.")] public string UserName { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public bool Sent { get; private set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await users.FindByNameAsync(UserName.Trim());
        // Same answer whether or not the account exists, so usernames can't be probed.
        if (user?.Email is { Length: > 0 } address)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var link = Url.Page("/Account/ResetPassword", null, new { userId = user.Id, token, returnUrl = ReturnUrl }, Request.Scheme)!;
            await email.SendEmailAsync(address, $"Reset your {Core.Brand.Name} password",
                $"<p>Someone asked to reset the password for <b>{HtmlEncoder.Default.Encode(user.UserName!)}</b>.</p><p><a href=\"{HtmlEncoder.Default.Encode(link)}\">Choose a new password</a></p><p>If this wasn't you, ignore this email.</p>");
        }
        Sent = true;
        return Page();
    }
}
