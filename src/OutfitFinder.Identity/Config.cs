using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using OutfitFinder.Core;
using OutfitFinder.Infrastructure;

namespace OutfitFinder.Identity;

public static class Config
{
    public const string ApiScope = "outfit.api";

    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResource("roles", "Your role", ["role"]),
    ];

    public static IEnumerable<ApiScope> ApiScopes => [new ApiScope(ApiScope, $"{Brand.Name} API", ["name", "role"])];

    public static IEnumerable<ApiResource> ApiResources =>
        [new ApiResource(ApiScope, $"{Brand.Name} API") { Scopes = { ApiScope }, UserClaims = { "name", "role" } }];

    public static IEnumerable<Client> Clients(IConfiguration config)
    {
        var web = InfrastructureSetup.Require(config, "Clients:Web:Url").TrimEnd('/');
        yield return new Client
        {
            ClientId = "web",
            ClientName = $"{Brand.Name} website",
            ClientSecrets = { new Secret(InfrastructureSetup.Require(config, "Clients:Web:Secret").Sha256()) },
            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,
            RedirectUris = { $"{web}/signin-oidc" },
            PostLogoutRedirectUris = { $"{web}/signout-callback-oidc" },
            FrontChannelLogoutUri = $"{web}/signout-oidc",
            AllowOfflineAccess = true,
            AllowedScopes = { IdentityServerConstants.StandardScopes.OpenId, IdentityServerConstants.StandardScopes.Profile, "roles", ApiScope },
            AlwaysIncludeUserClaimsInIdToken = true,
            AccessTokenLifetime = 3600,
            RequireConsent = false,
        };

        if (config["Clients:Extension:Id"] is { Length: > 0 } extId)
        {
            yield return new Client
            {
                ClientId = "extension",
                ClientName = $"{Brand.Name} Chrome extension",
                RequireClientSecret = false,
                AllowedGrantTypes = GrantTypes.Code,
                RequirePkce = true,
                RedirectUris = { $"https://{extId}.chromiumapp.org/" },
                PostLogoutRedirectUris = { $"https://{extId}.chromiumapp.org/" },
                AllowedCorsOrigins = { $"chrome-extension://{extId}" },
                AllowOfflineAccess = true,
                RefreshTokenUsage = TokenUsage.OneTimeOnly,
                RefreshTokenExpiration = TokenExpiration.Sliding,
                SlidingRefreshTokenLifetime = 30 * 24 * 3600,
                AllowedScopes = { IdentityServerConstants.StandardScopes.OpenId, IdentityServerConstants.StandardScopes.Profile, "roles", ApiScope },
                RequireConsent = false,
            };
        }
    }
}
