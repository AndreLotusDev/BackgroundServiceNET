# 07 – Upload to S3

Web uploads the generated Excel to the LocalStack bucket.

## Questions to resolve first

1. Object key format. Suggested: `exports/{userId}/{exportId}.xlsx`.
2. What does the user see after upload: a "submitted" message, or a redirect to a status page (ties to task 11)?
3. Do we store an `Export` record in `web.db` (id, user, key, created at)?

## Scope

- `IFileStorage` interface + S3 implementation (`AWSSDK.S3`, `ServiceURL` from config, `ForcePathStyle = true`).
- Endpoint/action: export selected items → upload → confirm to the user.
- LocalStack endpoint, bucket and credentials in `appsettings.Development.json`.

## Acceptance criteria

- [ ] After submitting a selection, the file exists in the bucket with the agreed key.
- [ ] The file is visible in the storage UI.
- [ ] An SQS message is produced for the upload (verified with AWS CLI).
- [ ] If LocalStack is down, the user gets a clear error and the app does not crash.
- [ ] No AWS types leak outside the `IFileStorage` implementation.
