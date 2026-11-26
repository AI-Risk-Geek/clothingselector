using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OutfitFinder.Infrastructure.Data;

namespace OutfitFinder.Identity.Pages.Account;

/// <summary>Signs out straight away (no confirmation prompt) and returns to the client that asked.</summary>
public class LogoutModel(SignInManager<ApplicationUser> signIn, IIdentityServerInteractionService interaction, IConfiguration config) : PageModel
{
    public string ReturnTo { get; private set; } = "/";
    public string? SignOutIframeUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? logoutId)
    {
        var context = await interaction.GetLogoutContextAsync(logoutId, HttpContext.RequestAborted);
        if (User.Identity?.IsAuthenticated == true) await signIn.SignOutAsync();
        ReturnTo = context?.PostLogoutRedirectUri ?? config["Clients:Web:Url"] ?? "/";
        SignOutIframeUrl = context?.SignOutIFrameUrl;
        if (context?.PostLogoutRedirectUri is not null && SignOutIframeUrl is null) return Redirect(context.PostLogoutRedirectUri);
        return Page();
    }
}
