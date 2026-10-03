# 09 – Worker: process file row by row

Download the file, read each row, store it in `worker.db`.

## Questions to resolve first

1. Idempotency key for files: S3 key, or key + ETag (same key uploaded twice)?
2. Idempotency for rows: unique on (file, row number)?
3. What is stored per row: the full item, plus status (`Processed` / `Failed`) and error message?

## Scope

- Download from S3 (`AWSSDK.S3`) to a stream, read with ClosedXML using the agreed layout.
- EF Core + SQLite `worker.db`: `ProcessedFiles`, `ProcessedItems`.
- Skip files already in `ProcessedFiles`.
- A bad row is saved as `Failed` with the reason; the rest of the file continues.

## Acceptance criteria

- [ ] Uploading a file with N valid rows creates N `ProcessedItems` and one `ProcessedFiles` row.
- [ ] Sending the same event twice does not create duplicates.
- [ ] A file with one invalid row (e.g. text in `Price`) stores N-1 processed + 1 failed.
- [ ] `worker.db` is separate from `web.db`; the Worker has no connection string for `web.db`.
- [ ] Integration test against LocalStack (or a fake `IFileStorage`) covers the three cases above.
