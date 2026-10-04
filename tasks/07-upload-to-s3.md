# 07 – Upload to S3

Web uploads the generated Excel to the LocalStack bucket.

## Questions to resolve first

1. Object key format. Suggested: `exports/{userId}/{exportId}.xlsx`.
   → Answer: `exports/{userId}/{exportId}.xlsx` (`userId` = Identity `sub`, `exportId` = the UUID v7 in the workbook properties). The same values are also stored as S3 object metadata: `export-id`, `user-id`, `created-at`.
2. What does the user see after upload: a "submitted" message, or a redirect to a status page (ties to task 11)?
   → Answer: Redirect to a new `/Exports` page listing the user's exports (created at, file, item count, key, status `Uploaded`) with a "Submitted …" banner. Task 11 adds the Worker's outcome to this page.
3. Do we store an `Export` record in `web.db` (id, user, key, created at)?
   → Answer: Yes. `Exports` table (`ExportRecord`: id, user, object key, file name, item count, created at, status), migration `AddExports`. The row is written only after the upload succeeds.

Extra decision: the API gets the same action, `POST /api/items/export` (bearer, `ApiScope`) → `202` with the export, `404` without a saved selection, `503` when storage is down.

## Scope

- `IFileStorage` interface + S3 implementation (`AWSSDK.S3`, `ServiceURL` from config, `ForcePathStyle = true`).
- Endpoint/action: export selected items → upload → confirm to the user.
- LocalStack endpoint, bucket and credentials in `appsettings.Development.json`.

## Acceptance criteria

- [x] After submitting a selection, the file exists in the bucket with the agreed key.
- [x] The file is visible in the storage UI.
- [x] An SQS message is produced for the upload (verified with AWS CLI).
- [x] If LocalStack is down, the user gets a clear error and the app does not crash.
- [x] No AWS types leak outside the `IFileStorage` implementation.

Verified with `dotnet test` (11 tests; 3 new in `ExportSubmitterTests`: key/metadata/record, no selection, failed upload records nothing) and by running LocalStack (`podman compose up -d`), Identity and Web:
logged in as `alice`, **Upload Excel** on `/Items` redirected to `/Exports` with the new row. `awslocal s3 ls` showed `exports/<sub>/<exportId>.xlsx`, `s3api head-object` showed the xlsx content type and the three metadata entries, and `sqs receive-message` on `file-uploaded` returned an `ObjectCreated:Put` event with that key (AWS CLI run through `awslocal` in the container, not installed on the host). The object is listed in s3manager (http://localhost:8080).
With the `localstack` container stopped, the upload returned to `/Items` with "The file could not be uploaded because file storage is unavailable…", the log has one `warn` (connection refused wrapped in `FileStorageException`) and no unhandled exception, and no `Exports` row was added.
`Amazon.*` is only referenced in `Storage/S3FileStorage.cs`.
API: `POST /api/items/export` returns `401` without a token and Swagger lists `202/404/503`; it was not called with a real token (same `ExportSubmitter` path as the MVC action).

## Notes

- `Storage/IFileStorage.cs` (`IFileStorage`, `FileStorageException`), `Storage/S3FileStorage.cs` (options + S3 client), `AddS3FileStorage` registers both and validates the `FileStorage` section on start (`BucketName`, `Region` required). Without `ServiceUrl`/keys it uses the real AWS region and the default credential chain.
- Client timeout 10 s, 2 retries, so a dead endpoint fails fast.
- Upload uses the **saved** selection (same as Download), via a separate form, not the checkboxes currently on screen.
- Production `appsettings.json` has no `FileStorage` section yet, so a non-Development run fails on start until it is configured.
