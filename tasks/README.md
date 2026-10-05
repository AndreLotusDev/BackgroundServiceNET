# Tasks

Source: [docs/session-01-sample-project.md](../docs/session-01-sample-project.md)

Rule: a task does not start until its **Questions to resolve first** are answered. Write the answer under the question (`→ Answer: ...`).

Rule: from task 13 on, any frontend work (Web or Identity pages) uses the Fila components and styling from `fila_samples/` in the dark theme. See [13 – Fila UI, dark theme](13-fila-dark-theme.md) for the asset list and which Fila page to copy each component from.

| # | Task | Depends on |
|---|---|---|
| 01 | [Solution skeleton](01-solution-skeleton.md) | – |
| 02 | [LocalStack setup](02-localstack-setup.md) | 01 |
| 03 | [Identity (Duende + ASP.NET Identity)](03-identity.md) | 01 |
| 04 | [Swagger login](04-swagger-login.md) | 03 |
| 05 | [Steam items catalog + selection](05-steam-items-selection.md) | 03 |
| 06 | [Excel export](06-excel-export.md) | 05 |
| 07 | [Upload to S3](07-upload-to-s3.md) | 02, 06 |
| 08 | [Worker: SQS listener](08-worker-sqs-listener.md) | 02 |
| 09 | [Worker: process file row by row](09-worker-process-file.md) | 07, 08 |
| 10 | [Worker: errors and shutdown](10-worker-errors-shutdown.md) | 09 |
| 11 | [Job status back to the user](11-job-status.md) | 09 |
| 12 | [Show the processed rows](12-processed-items.md) | 11 |
| 13 | [Fila UI, dark theme](13-fila-dark-theme.md) | 12 |
| 14 | [Databases outside the projects + DB browser](14-worker-db-outside.md) | 12 |
