using Duende.IdentityServer;
using Duende.IdentityServer.Models;

namespace SteamItems.Identity;

public static class Config
{
    public const string ApiScope = "api";

    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResources.Email(),
    ];

    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new ApiScope(ApiScope, "SteamItems API"),
    ];

    public static IEnumerable<Client> Clients(IConfiguration configuration)
    {
        var mvc = configuration.GetSection("Clients:Mvc");
        var mvcBaseUrl = mvc["BaseUrl"]?.TrimEnd('/')
            ?? throw new InvalidOperationException("Clients:Mvc:BaseUrl is not configured.");
        var mvcSecret = mvc["Secret"]
            ?? throw new InvalidOperationException("Clients:Mvc:Secret is not configured.");

        return
        [
            // SteamItems.Web: server-side MVC app, signs users in with a cookie after the code flow.
            new Client
            {
                ClientId = "mvc",
                ClientName = "SteamItems Web",
                ClientSecrets = { new Secret(mvcSecret.Sha256()) },

                AllowedGrantTypes = GrantTypes.Code,
                RequirePkce = true,

                RedirectUris = { $"{mvcBaseUrl}/signin-oidc" },
                FrontChannelLogoutUri = $"{mvcBaseUrl}/signout-oidc",
                PostLogoutRedirectUris = { $"{mvcBaseUrl}/signout-callback-oidc" },

                AllowOfflineAccess = true,
                AllowedScopes =
                {
                    IdentityServerConstants.StandardScopes.OpenId,
                    IdentityServerConstants.StandardScopes.Profile,
                    IdentityServerConstants.StandardScopes.Email,
                    ApiScope,
                },
            },
        ];
    }
}
