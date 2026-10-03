# 04 – Swagger login

Authorize in the Swagger UI, get a token, call a protected API endpoint.

## Questions to resolve first

1. Swashbuckle or Scalar? (Swashbuckle has the built-in OAuth2 PKCE flow in the UI.)
   → Answer: Swashbuckle (`Swashbuckle.AspNetCore` 10.x, Microsoft.OpenApi 2.x). Swagger UI at `/swagger`.
2. Which endpoints go in the API: only items + export, or also upload/status?
   → Answer: Neither exists yet, so this task adds one protected `GET /api/me` (echoes `sub` and scopes from the token) to prove the flow.
   Items (05), export (06), upload (07) and status (11) are added by their own tasks under `/api` with the same `ApiScope` policy.

## Scope

- OpenAPI doc with an OAuth2 Authorization Code + PKCE security scheme.
- `swagger` client in IdentityServer with redirect URI `/swagger/oauth2-redirect.html`.
- API controllers protected with JWT bearer for the `api` scope.

## Acceptance criteria

- [x] Swagger UI shows an **Authorize** button.
- [x] Clicking it goes through the IdentityServer login and returns to Swagger with a token.
- [x] A protected endpoint returns `401` without the token and `200` with it.
- [x] MVC cookie login from task 03 still works.

Verified by running both projects (`dotnet run --launch-profile https`):
Swagger UI shows **Authorize** with client id `swagger` prefilled and the `api` scope ticked; it sends a PKCE request to IdentityServer, and logging in as `alice` returns to `/swagger/oauth2-redirect.html` with a code.
The same PKCE flow scripted with curl: the CORS preflight on `/connect/token` from `https://localhost:7281` is allowed, the code exchange returns an access token, and `GET /api/me` returns `401` without a token or with a bad one and `200` with the token (`{"subject":"...","scopes":["api"]}`).
MVC: anonymous `/Home/Profile` still redirects to the IdentityServer login and returns `200` after login. The MVC cookie alone gets `401` on `/api/me`, because the API accepts bearer tokens only.

## Notes

- `swagger` is a public client (no secret) in `Config.cs`; its base URL is `Clients:Swagger:BaseUrl` (Identity). The Web app reads the client id from `Identity:SwaggerClientId`.
- IdentityServer defines only an `api` scope (no ApiResource), so access tokens have no `aud`. The JWT bearer handler skips audience validation, and the `ApiScope` policy (`AuthPolicies.cs`) requires `scope=api` instead.
- Only routes starting with `api/` go into the OpenAPI document; MVC pages are left out.
