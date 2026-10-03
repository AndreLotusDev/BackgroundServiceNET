# 03 – Identity (Duende IdentityServer + ASP.NET Identity)

Users can register/log in to the web app. IdentityServer issues tokens for the API.

## Questions to resolve first

1. Duende license: confirm the Community Edition / dev-only terms fit this sample.
   → Answer: Dev/test only, no license key. Duende allows evaluation, development, testing and personal projects without a key; it only logs "no Duende license is configured" warnings and does not limit features. Production use would need Community Edition (< $1M revenue and < $3M capital) or a paid license.
2. IdentityServer hosted inside `SteamItems.Web` or as its own project?
   → Answer: Own project, `SteamItems.Identity` (https://localhost:5001). `SteamItems.Web` is an OIDC client (`mvc`) of it.
3. Self-registration, or only seeded users? If seeded, which test users?
   → Answer: Both. Seeded `alice` / `bob` (password `Pass123$`) from `SeedUsers` in `SteamItems.Identity/appsettings.Development.json`, plus a Register page.
4. Use the Duende UI templates (`dotnet new isui`) or write minimal login pages?
   → Answer: Minimal MVC pages (`AccountController`: Login, Register, Logout).

## Scope

- ASP.NET Identity with EF Core + SQLite (`identity.db`, owned by `SteamItems.Identity`; `web.db` stays for the Web app's own data in task 05).
- IdentityServer configured with an `api` scope and an `mvc` client (code + PKCE, cookie login for MVC pages).
- Clients/scopes in memory (`Config.cs`); grants and signing keys in the EF operational store.
- Migrations applied on startup in Development.

## Acceptance criteria

- [x] A user can log in and log out from the MVC site.
- [x] Pages that need login redirect to the login page when anonymous.
- [x] `/.well-known/openid-configuration` returns the discovery document.
- [x] `identity.db` is created on first run with Identity and IdentityServer tables.
- [x] Test user credentials live in a seed/config file, not hardcoded in controllers.

Verified by running both projects (`dotnet run --launch-profile https`) and through the AppHost:
anonymous `/Home/Profile` → 302 to `/connect/authorize`; login as `alice` returns to Profile with an access token that has the `api` scope;
logout ends the IdentityServer session too (Profile asks for credentials again); registering a new user signs them in and returns to the client.
`identity.db` contained the `AspNet*` tables and `PersistedGrants`, `Keys`, `DeviceCodes`, `ServerSideSessions`, `PushedAuthorizationRequests`.

## Notes

- `dotnet ef` is pinned in the local tool manifest (`dotnet-tools.json`, run `dotnet tool restore`).
  New migrations: `dotnet ef migrations add <Name> -p src/SteamItems.Identity -c ApplicationDbContext -o Data/Migrations/Identity` (or `-c PersistedGrantDbContext -o Data/Migrations/PersistedGrant`).
- The Web app talks to `Identity:Authority` (appsettings.Development.json; the AppHost overrides it with the identity endpoint). Both run their `https` profile because the OIDC cookies need HTTPS.
- The `mvc` client's base URL and secret are dev values in `Clients:Mvc` (Identity) and `Identity:ClientSecret` (Web).
