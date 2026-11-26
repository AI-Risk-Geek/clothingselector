using System.ComponentModel.DataAnnotations;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Entities;
using OutfitFinder.Infrastructure.Data;
using OutfitFinder.UI.Legal;

namespace OutfitFinder.Identity.Pages.Account;

public class RegisterModel(
    UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, IIdentityServerInteractionService interaction,
    OutfitDbContext db, ICaptchaVerifier captcha, IConfiguration config) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? TurnstileSiteKey => config["Turnstile:SiteKey"];

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Choose a username."), StringLength(32, MinimumLength = 3, ErrorMessage = "Usernames are 3 to 32 characters.")]
        [RegularExpression("^[A-Za-z0-9._-]+$", ErrorMessage = "Use letters, numbers, dots, dashes or underscores.")]
        public string UserName { get; set; } = "";

        [EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Choose a password."), StringLength(128, MinimumLength = 10, ErrorMessage = "Use at least 10 characters."), DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Compare(nameof(Password), ErrorMessage = "The passwords don't match."), DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = "";

        [Range(typeof(bool), "true", "true", ErrorMessage = "Please confirm you are 16 or older and agree to the Terms and Privacy Policy.")]
        public bool Agree { get; set; }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync([FromForm(Name = "cf-turnstile-response")] string? turnstile)
    {
        if (!ModelState.IsValid) return Page();
        if (!string.IsNullOrEmpty(TurnstileSiteKey) && !await captcha.VerifyAsync(turnstile, HttpContext.Connection.RemoteIpAddress?.ToString(), HttpContext.RequestAborted))
        {
            ModelState.AddModelError("", "Please complete the 'I am human' check.");
            return Page();
        }

        var user = new ApplicationUser { UserName = Input.UserName.Trim(), Email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email.Trim() };
        var result = await users.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError(e.Code.Contains("UserName") ? "Input.UserName" : e.Code.Contains("Password") ? "Input.Password" : "",
                    e.Code == "DuplicateUserName" ? "That username is taken." : e.Description);
            return Page();
        }

        // Consent (16+, Terms, Privacy) recorded with the document versions shown.
        foreach (var doc in LegalDocuments.Current)
            db.Consents.Add(new ConsentRecord { Id = Guid.NewGuid(), UserId = user.Id, Document = doc.Document, Version = doc.Version, AcceptedUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        await AdminSeeder.EnsureAdminAsync(users, config, user.UserName);
        await signIn.SignInAsync(user, isPersistent: true);

        if (ReturnUrl is not null && interaction.IsValidReturnUrl(ReturnUrl)) return Redirect(ReturnUrl);
        if (ReturnUrl is not null && Url.IsLocalUrl(ReturnUrl)) return LocalRedirect(ReturnUrl);
        return Redirect(config["Clients:Web:Url"] ?? "/");
    }
}
