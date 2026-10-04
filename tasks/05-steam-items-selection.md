# 05 – Steam items catalog + selection

Logged-in user sees a list of Steam game items and selects several.

## Questions to resolve first

1. Data source: static seed (decided for now) — how many items and which fields? Suggested: `AppId`, `Name`, `Price`, `ReleaseDate`.
   → Answer: 20 well-known games in `Data/SteamItemSeed.cs` with `AppId` (real Steam app id, primary key), `Name`, `Price` (sample USD values, `0` = free) and `ReleaseDate` (`DateOnly`). Seeded with EF `HasData`, so the rows are inserted by the `InitialWeb` migration.
2. Are selections saved per user in `web.db`, or only kept for the export request?
   → Answer: Saved per user in `web.db` (`UserSelections`, key `UserId` + `AppId`, where `UserId` is the `sub` claim). Each submit replaces the user's previous selection, and the page pre-checks the saved items. Task 06 exports the saved selection.
3. Paging/search needed, or a single list is enough for the sample?
   → Answer: A single list sorted by name; 20 items don't need paging.

## Scope

- `SteamItem` entity + seed data in `web.db`.
- MVC page listing items with checkboxes.
- API endpoint `GET /api/items`.

## Acceptance criteria

- [x] Seed runs once; restarting does not duplicate items.
- [x] Logged-in user sees the list; anonymous user is redirected to login.
- [x] User can select multiple items and submit; submitting zero items shows a validation message.
- [x] `GET /api/items` returns the same items through Swagger.

Verified by running both projects (`dotnet run --launch-profile https`):
`web.db` is created on first start with 20 `SteamItems` rows; after a restart it still has 20, and the saved selection is still there.
Anonymous `/Items` → 302 to `/connect/authorize`. After logging in as `alice`, the page lists 20 items with checkboxes.
Submitting none re-renders the page with "Select at least one item." Submitting `620`, `1245620` and an unknown `999` saves 2 rows (the unknown id is dropped) and shows "Saved 2 selected item(s)." with both boxes checked.
`GET /api/items` returns `401` without a token and the same 20 items with an `api` access token; Swagger lists `/api/items` next to `/api/me`.

## Notes

- `web.db` lives in `SteamItems.Web` (`ConnectionStrings:WebDb`); migrations are applied on startup in Development.
  New migrations: `dotnet ef migrations add <Name> -p src/SteamItems.Web -c WebDbContext -o Data/Migrations`.
- To change the catalog, edit `SteamItemSeed.Items` and add a migration; EF generates the insert/update/delete.
- SQLite stores `decimal` as TEXT, so sort/filter by price in memory, not in the query.
- MVC page: `ItemsController` (`/Items`, cookie login). API: `Api/ItemsController` (`/api/items`, `ApiScope` bearer policy).
