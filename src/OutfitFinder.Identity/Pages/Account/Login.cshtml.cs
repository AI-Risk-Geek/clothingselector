using System.ComponentModel.DataAnnotations;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OutfitFinder.Infrastructure.Data;

namespace OutfitFinder.Identity.Pages.Account;

public class LoginModel(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, IIdentityServerInteractionService interaction, IConfiguration config) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? Error { get; set; }
    public string? Info { get; set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Enter your username.")] public string UserName { get; set; } = "";
        [Required(ErrorMessage = "Enter your password."), DataType(DataType.Password)] public string Password { get; set; } = "";
        public bool Remember { get; set; } = true;
    }

    public void OnGet(string? reset)
    {
        if (reset == "ok") Info = "Your password was changed. Sign in with the new one.";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var result = await signIn.PasswordSignInAsync(Input.UserName.Trim(), Input.Password, Input.Remember, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            Error = "Too many attempts. This account is locked for 15 minutes.";
            return Page();
        }
        if (!result.Succeeded)
        {
            Error = "That username and password don't match.";
            return Page();
        }
        await AdminSeeder.EnsureAdminAsync(users, config, Input.UserName.Trim());
        return RedirectAfterSignIn(interaction, ReturnUrl, config);
    }

    internal IActionResult RedirectAfterSignIn(IIdentityServerInteractionService ids, string? returnUrl, IConfiguration cfg)
    {
        if (returnUrl is not null && ids.IsValidReturnUrl(returnUrl)) return Redirect(returnUrl);
        if (returnUrl is not null && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return Redirect(cfg["Clients:Web:Url"] ?? "/");
    }
}
