# 12 – Show the processed rows

The Worker stores every row of an uploaded workbook in `ProcessedItems`, but the user only sees the counts on `/Exports`. The user opens an export and sees its rows: what was imported and which rows failed, and why.

## Questions to resolve first

1. How does Web get the rows? Same pattern as task 11 (a new Worker endpoint, e.g. `GET /api/files/items?key=…`, that reads `worker.db`), or does the Worker push the rows to Web so Web keeps its own copy?
2. Rows are read per object key (like task 11) or per export id? A file the Worker rejected has no export id, and a re-uploaded key has more than one `ProcessedFile`: show only the latest one?
3. Paging: a file can have 20,000 rows. Page size, and is paging done by the Worker endpoint (`skip`/`take` or a row-number cursor) or by Web?
4. Filter: show all rows, or have a filter (`All` / `Processed` / `Failed`)? Should failed rows come first?
5. What does a failed row show: the raw cell values (`RawAppId`, `RawPrice`, …) next to the error, or only the error?
6. While the file is still `Processing`: show the rows stored so far, and do they update live (SignalR, like task 11) or on refresh?
7. API: add `GET /api/exports/{id}/items` to Web for Swagger users too, or only the MVC page?
8. Authorization: Web checks the export belongs to the logged-in user before asking the Worker. Is that enough, or does the Worker endpoint need protecting now (still open from task 11)?

## Scope

- Shared DTO in `Contracts` for one row: row number, status, AppId, Name, Price, ReleaseDate, error, raw values for failed rows.
- Way for Web to get the rows of one file, per the answers above. Web still never reads `worker.db`.
- `/Exports/{id}` (details page): export header (file, status, counts) and a table of its rows, with paging and the filter.
- Link from each row on `/Exports` to its details page.

## Acceptance criteria

- [ ] After a file is imported, the user opens it from `/Exports` and sees every row with the values from the Excel file.
- [ ] Failed rows are marked, show the error and the value that did not parse (e.g. `"free"` in `Price`).
- [ ] The `Failed` filter shows only the failed rows, and the count matches the failed count on `/Exports`.
- [ ] A 20,000-row file opens without loading every row at once (paging works).
- [ ] A user cannot see another user's export rows (404 or 403).
- [ ] An export still `Pending`, or a file rejected as unreadable, shows a clear message instead of an empty table.
- [ ] If the Worker is down, the details page shows the export header and a message that the rows are not available right now; it does not crash.
- [ ] Web still has no access to `worker.db`.
