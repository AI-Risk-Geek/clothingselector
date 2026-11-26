using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OutfitFinder.Infrastructure.Data;

namespace OutfitFinder.Identity.Pages.Account;

public class ResetPasswordModel(UserManager<ApplicationUser> users) : PageModel
{
    [BindProperty(SupportsGet = true)] public string UserId { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    [BindProperty, Required(ErrorMessage = "Choose a password."), StringLength(128, MinimumLength = 10, ErrorMessage = "Use at least 10 characters."), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [BindProperty, Compare(nameof(Password), ErrorMessage = "The passwords don't match."), DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";

    /// <summary>True when the link is missing, expired or already used: the page then offers a new link instead of a form that can only fail.</summary>
    public bool LinkInvalid { get; private set; }

    public async Task OnGetAsync() => LinkInvalid = await FindUserWithValidTokenAsync() is null;

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await FindUserWithValidTokenAsync();
        if (user is null) { LinkInvalid = true; return Page(); }
        if (!ModelState.IsValid) return Page();
        var result = await users.ResetPasswordAsync(user, Token, Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(nameof(Password), e.Description);
            return Page();
        }
        await users.ResetAccessFailedCountAsync(user);
        return RedirectToPage("Login", new { reset = "ok", returnUrl = ReturnUrl });
    }

    private async Task<ApplicationUser?> FindUserWithValidTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(UserId) || string.IsNullOrWhiteSpace(Token)) return null;
        var user = await users.FindByIdAsync(UserId);
        if (user is null) return null;
        var ok = await users.VerifyUserTokenAsync(user, users.Options.Tokens.PasswordResetTokenProvider, UserManager<ApplicationUser>.ResetPasswordTokenPurpose, Token);
        return ok ? user : null;
    }
}
