# 05 – Steam items catalog + selection

Logged-in user sees a list of Steam game items and selects several.

## Questions to resolve first

1. Data source: static seed (decided for now) — how many items and which fields? Suggested: `AppId`, `Name`, `Price`, `ReleaseDate`.
2. Are selections saved per user in `web.db`, or only kept for the export request?
3. Paging/search needed, or a single list is enough for the sample?

## Scope

- `SteamItem` entity + seed data in `web.db`.
- MVC page listing items with checkboxes.
- API endpoint `GET /api/items`.

## Acceptance criteria

- [ ] Seed runs once; restarting does not duplicate items.
- [ ] Logged-in user sees the list; anonymous user is redirected to login.
- [ ] User can select multiple items and submit; submitting zero items shows a validation message.
- [ ] `GET /api/items` returns the same items through Swagger.
