# 09 – Worker: process file row by row

Download the file, read each row, store it in `worker.db`.

## Questions to resolve first

1. Idempotency key for files: S3 key, or key + ETag (same key uploaded twice)?
   → Answer: Key + ETag. `ProcessedFiles` is unique on (`Bucket`, `Key`, `ETag`), and the ETag comes from the S3 `GetObject` response. A redelivered event is skipped. A different file uploaded to the same key is a new file.
2. Idempotency for rows: unique on (file, row number)?
   → Answer: Yes, unique on (`FileId`, `RowNumber`), and each row is committed on its own. A file starts as `Processing` and ends as `Completed`. A redelivered `Processing` file (after a crash) resumes and skips rows already stored.
3. What is stored per row: the full item, plus status (`Processed` / `Failed`) and error message?
   → Answer: Row number, the typed item (`AppId`, `Name`, `Price`, `ReleaseDate`, nullable so a failed row can keep the values that did parse), `Status`, `Error` and `ProcessedAt`. Failed rows also keep the raw text of each cell. `ProcessedFiles` also stores `ExportId`/`UserId` from the workbook properties and the processed/failed counts (for task 11).

## Scope

- Download from S3 (`AWSSDK.S3`) to a stream, read with ClosedXML using the agreed layout.
- EF Core + SQLite `worker.db`: `ProcessedFiles`, `ProcessedItems`.
- Skip files already in `ProcessedFiles`.
- A bad row is saved as `Failed` with the reason; the rest of the file continues.

## Acceptance criteria

- [x] Uploading a file with N valid rows creates N `ProcessedItems` and one `ProcessedFiles` row.
- [x] Sending the same event twice does not create duplicates.
- [x] A file with one invalid row (e.g. text in `Price`) stores N-1 processed + 1 failed.
- [x] `worker.db` is separate from `web.db`; the Worker has no connection string for `web.db`.
- [x] Integration test against LocalStack (or a fake `IFileStorage`) covers the three cases above.

Verified with `dotnet test` (43 tests; 14 new in `tests/SteamItems.Worker.Tests`) and by running the Worker against LocalStack (`dotnet run --project src/SteamItems.Worker`, Development):
`FileImporterTests` (fake `IFileStorage`, real schema from the migrations on in-memory SQLite, a new `DbContext` per import) cover the three cases above, plus: the same key with new content is a second file, an interrupted `Processing` file resumes without touching stored rows, a non-xlsx file and a wrong header are marked `Failed` once and then skipped, and storage down throws `FileStorageException` and stores nothing. `ItemsWorkbookReaderTests` cover typed rows and properties, missing properties, blank rows, a row with every cell bad (all errors + raw text), and a missing `Items` sheet. `ListenerAndProcessorTests` now also check that a failed import leaves the message on the queue and the processor keeps going.
Running: on start the Worker applied `InitialWorker` and created `src/SteamItems.Worker/worker.db`. A 3-row workbook PUT to `manual/task09-valid.xlsx` logged `3 processed, 0 failed`. The same workbook with `"free"` in row 3's `Price` logged a `warn` for row 3 and `2 processed, 1 failed`. In the DB, the failed row has `Price` NULL, AppId/Name/ReleaseDate kept, the error, and the raw cells (`RawPrice = free`). Two hand-sent SQS messages with the same S3 event for the valid file both logged `Skipping … already Completed`, and the DB still has 2 files and 6 items. Ctrl+C logged `Listener stopped` and `File processors stopped`, with nothing on stderr.
Uploads through the Web app were not repeated. Task 07 already verified that they produce the same `ObjectCreated:Put` event, and the reader is tested against the same `ItemsWorkbook` layout the Web writer uses.

## Notes

- `Storage/`: a read-side `IFileStorage.DownloadAsync(bucket, key)` → `StoredFile(ETag, Stream)`, implemented by `S3FileStorage` (the only place in the Worker that uses `Amazon.S3`). The bucket comes from the event, so the `FileStorage` section has no `BucketName` (`Region` required, validated on start). The object is copied to a `MemoryStream`, because ClosedXML needs a seekable stream.
- `Import/ItemsWorkbookReader` reads with `ItemsWorkbook` column numbers and checks the header text. Row rules: `AppId` is a positive whole number, `Name` is non-blank text up to 200 characters, `Price` is a number ≥ 0, `ReleaseDate` is a real date. Every bad cell is reported on the row. Fully blank rows are skipped and row numbers stay Excel row numbers.
- `Import/FileImporter` (scoped, one DI scope per file from `FileProcessor`): download → look up (bucket, key, ETag) → skip if `Completed`/`Failed`, resume if `Processing`, else insert `Processing` → read → one `SaveChanges` per row → counts recomputed from the DB → `Completed`. A unique violation on a file or row means a duplicate delivery got there first, and is treated as already done.
- A file that is not a readable workbook with the agreed layout is stored as `Failed` with the reason, and its message is deleted, since retrying reads the same bytes. Storage errors (S3 down, object missing) propagate, so the message stays and is retried, then goes to the DLQ. Task 10 decides retry policy and cancellation (rows already check the stopping token between commits).
- `worker.db` migrations are applied on start in Development only (same as Web). Connection string `WorkerDb` is in `appsettings.json`. Production has no `FileStorage` section yet, so a non-Development run fails on start until it is configured.
- `ProcessedFiles` keeps `ExportId`/`UserId` from the workbook properties and the processed/failed counts for task 11.
