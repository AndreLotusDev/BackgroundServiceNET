# 04 – Swagger login

Authorize in the Swagger UI, get a token, call a protected API endpoint.

## Questions to resolve first

1. Swashbuckle or Scalar? (Swashbuckle has the built-in OAuth2 PKCE flow in the UI.)
2. Which endpoints go in the API: only items + export, or also upload/status?

## Scope

- OpenAPI doc with an OAuth2 Authorization Code + PKCE security scheme.
- `swagger` client in IdentityServer with redirect URI `/swagger/oauth2-redirect.html`.
- API controllers protected with JWT bearer for the `api` scope.

## Acceptance criteria

- [ ] Swagger UI shows an **Authorize** button.
- [ ] Clicking it goes through the IdentityServer login and returns to Swagger with a token.
- [ ] A protected endpoint returns `401` without the token and `200` with it.
- [ ] MVC cookie login from task 03 still works.
