# 12 – Show the processed rows

The Worker stores every row of an uploaded workbook in `ProcessedItems`, but the user only sees the counts on `/Exports`. The user opens an export and sees its rows: what was imported and which rows failed, and why.

## Questions to resolve first

1. How does Web get the rows? Same pattern as task 11 (a new Worker endpoint, e.g. `GET /api/files/items?key=…`, that reads `worker.db`), or does the Worker push the rows to Web so Web keeps its own copy?
   → Answer: A new Worker endpoint, `GET /api/files/items`, that reads `worker.db`. Web calls it when the details page opens.
2. Rows are read per object key (like task 11) or per export id? A file the Worker rejected has no export id, and a re-uploaded key has more than one `ProcessedFile`: show only the latest one?
   → Answer: Web asks the Worker by object key, like task 11, and the Worker answers with its latest `ProcessedFile` for that key.
3. Paging: a file can have 20,000 rows. Page size, and is paging done by the Worker endpoint (`skip`/`take` or a row-number cursor) or by Web?
   → Answer: The Worker endpoint pages with `page`/`pageSize` (skip/take ordered by `RowNumber`). Default 50 rows, max 200. The response carries the total, so Web renders First / Previous / Next / Last.
4. Filter: show all rows, or have a filter (`All` / `Processed` / `Failed`)? Should failed rows come first?
   → Answer: Filter `All` / `Processed` / `Failed`, each with its count. Rows always in Excel row order.
5. What does a failed row show: the raw cell values (`RawAppId`, `RawPrice`, …) next to the error, or only the error?
   → Answer: Only the error. To still show the value that did not parse, the Worker's reader now writes it into the error, e.g. `Price must be a number zero or greater, found "free".` The DTO keeps the raw cells for API users; the page does not show them.
6. While the file is still `Processing`: show the rows stored so far, and do they update live (SignalR, like task 11) or on refresh?
   → Answer: No rows while `Pending` or `Processing`, only a message. The page listens to task 11's `exportUpdated` and reloads when the export's status changes.
7. API: add `GET /api/exports/{id}/items` to Web for Swagger users too, or only the MVC page?
   → Answer: Yes: `GET /api/exports/{id}/items` and also `GET /api/exports/{id}` for the header (closes that task 11 note).
8. Authorization: Web checks the export belongs to the logged-in user before asking the Worker. Is that enough, or does the Worker endpoint need protecting now (still open from task 11)?
   → Answer: Web's ownership check is enough for now. The Worker endpoints stay unauthenticated (still open, see Notes).

## Scope

- Shared DTO in `Contracts` for one row: row number, status, AppId, Name, Price, ReleaseDate, error, raw values for failed rows.
- Way for Web to get the rows of one file, per the answers above. Web still never reads `worker.db`.
- `/Exports/{id}` (details page): export header (file, status, counts) and a table of its rows, with paging and the filter.
- Link from each row on `/Exports` to its details page.

## Acceptance criteria

- [x] After a file is imported, the user opens it from `/Exports` and sees every row with the values from the Excel file.
- [x] Failed rows are marked, show the error and the value that did not parse (e.g. `"free"` in `Price`).
- [x] The `Failed` filter shows only the failed rows, and the count matches the failed count on `/Exports`.
- [x] A 20,000-row file opens without loading every row at once (paging works).
- [x] A user cannot see another user's export rows (404 or 403).
- [x] An export still `Pending`, or a file rejected as unreadable, shows a clear message instead of an empty table.
- [x] If the Worker is down, the details page shows the export header and a message that the rows are not available right now; it does not crash.
- [x] Web still has no access to `worker.db`.

Verified with `dotnet test` (94 tests; 21 new) and by running the AppHost (LocalStack, Identity, Web, Worker):
- Tests: `FileItemsQueryTests` (Worker, migrated in-memory SQLite) check the stored values in row order, a failed row's error and raw cells, the `Failed` filter and its counts, paging (incl. a page past the end), the latest file for a re-uploaded key, a rejected file and an unknown key. `ItemsWorkbookReaderTests` check that each error names the bad value, an empty cell, and a long value cut to 40 characters. `ExportItemsReaderTests` (Web, fake Worker) check the page request, another user's export (not found, Worker not asked), `Pending` / `Processing` / `Failed` without asking the Worker, Worker down, a key the Worker does not know, and a newer upload still processing. `HttpWorkerItemsClientTests` check the query string, the JSON contract, 404 → null, and 500 / refused connection → `WorkerUnavailableException`.
- An existing `Completed with errors` export opened from `/Exports`: header, `All 2 / Processed 1 / Failed 1`, and both rows with their values. Its failed row shows the old error text, without the value: it was imported before the reader change.
- Worker stopped: the same page showed the header and "The rows are not available right now". A new upload showed `Pending` and the "not picked up yet" message.
- A generated 20,000-row workbook (every 100th row `"free"` in `Price`) was PUT to that new export's key. The Worker imported it in about 1.5 min (`19800` processed, `200` failed), and `/Exports` showed `Completed with errors`, `19800`, `200`.
- Details page: `All 20000 / Processed 19800 / Failed 200`. Each page has 50 rows: "Page 1 of 400, 20000 rows", page 400 starts at row 19952, page 401 shows "No rows on this page" with a link back. `Failed` page 2 of 4 starts at row 5100, and every row shows `Price must be a number zero or greater, found "free".`
- `/Exports/{unknown id}` answers 404. The same lookup (`Id` and `UserId`) serves another user's id, covered by the tests; not checked with a second login, so the user's session was kept.
- Web references neither `worker.db` nor the Worker project. It only calls the HTTP endpoint and shares the DTO in `Contracts`.

## Notes

- **Contract** (`SteamItems.Contracts/Status/FileItems.cs`): `FileItemsApi` (path, `key`, `status`, `page`, `pageSize`, default 50, max 200), `FileItemsPage(Key, FileStatus, Filter, Page, PageSize, TotalCount, ProcessedCount, FailedCount, Items)` with a computed `PageCount`, `FileItemResponse(RowNumber, Status, AppId, Name, Price, ReleaseDate, Error, Raw)` and `FileItemStatus` (`Processed` / `Failed`, string).
- **Worker**: `Status/FileItemsQuery` reads the latest file per key and counts its rows from `ProcessedItems` (so the counts are right while still processing). `MapFileItems` answers 400 for a missing key, an unknown status or a bad page/page size, and 404 when the Worker has no file for the key. Row errors now end with `found "<value>"` or `found an empty cell`, with the value cut to 40 characters so the error stays under 500.
- **Web**: `Status/ExportItemsReader` loads the export by id **and** user id from `web.db`. It only asks the Worker (`HttpWorkerItemsClient`, same base URL and resilience handler as the status client) once the export is `Completed` / `Completed with errors`. The result is a state: `Available`, `Pending`, `Processing`, `Rejected`, `WorkerUnavailable` or `Missing`. If the Worker's latest file for the key is still processing (a re-upload), it shows `Processing`.
- **Page**: `GET /Exports/{id}` (`ExportsController.Details`, `status` and `page` in the query string); the file name on `/Exports` links to it. `wwwroot/js/export-details.js` is only loaded while `Pending` / `Processing` and reloads when the status changes, not on every count change.
- **API** (`Controllers/Api/ExportDetailsController`, `api` scope): `GET /api/exports/{id}` → `ExportResponse`. `GET /api/exports/{id}/items?status=&page=&pageSize=` → `FileItemsPage`, 404 not the caller's (or the Worker has no rows), 409 while `Pending` / `Processing` or when rejected, 503 when the Worker is down.
- Still open:
  - The Worker endpoints still have **no authentication** (as in task 11).
  - Rows imported before this task keep the old error text without the value.
  - A 404 from an older Worker build without the endpoint reads as "The Worker has no rows for this file".
  - The header's `Items` comes from `web.db` (what Web exported); if the object under the key is replaced, the rows can differ from it.
