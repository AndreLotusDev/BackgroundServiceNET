# 03 – Identity (Duende IdentityServer + ASP.NET Identity)

Users can register/log in to the web app. IdentityServer issues tokens for the API.

## Questions to resolve first

1. Duende license: confirm the Community Edition / dev-only terms fit this sample.
2. IdentityServer hosted inside `SteamItems.Web` or as its own project?
3. Self-registration, or only seeded users? If seeded, which test users?
4. Use the Duende UI templates (`dotnet new isui`) or write minimal login pages?

## Scope

- ASP.NET Identity with EF Core + SQLite (`web.db`).
- IdentityServer configured with an `api` scope and an `mvc` client (cookie login for MVC pages).
- Migrations applied on startup in Development.

## Acceptance criteria

- [ ] A user can log in and log out from the MVC site.
- [ ] Pages that need login redirect to the login page when anonymous.
- [ ] `/.well-known/openid-configuration` returns the discovery document.
- [ ] `web.db` is created on first run with Identity and IdentityServer tables.
- [ ] Test user credentials live in a seed/config file, not hardcoded in controllers.
