# Application walkthrough (screenshots)

Screenshots of the running sample, taken with `dotnet run --project src/SteamItems.AppHost` (Aspire + Podman). Images are in [screenshots/](screenshots/), palette-compressed PNGs.

Flow: pick Steam items in **Web**, export to Excel, upload it to **S3 (LocalStack)**, the S3 event lands on **SQS**, the **Worker** processes it row by row into `worker.db`, and **Web** shows the job status.

## 1. Web app (Fila dark theme)

| | |
|---|---|
| **Home** (anonymous) | **Login** (Identity, OIDC) |
| ![Home](screenshots/01-home.png) | ![Login](screenshots/02-login.png) |

Test users: `alice` / `bob`, password `Pass123$`.

### Steam items

Select items and click **Save selection**. From there: **Download Excel** or **Upload Excel**.

![Items selection](screenshots/03-items-selection.png)

![Items saved](screenshots/04-items-saved.png)

### Exports and job status

After **Upload Excel** the export is `Pending`; the Worker picks it up and the row turns `Completed` live over SignalR.

![Exports pending](screenshots/05-exports-pending.png)

![Exports completed](screenshots/06-exports-completed.png)

The detail page lists every processed row, with its status and error (if any):

![Export details](screenshots/07-export-details.png)

### Profile and Swagger

| Profile (requires login) | Swagger UI (`/swagger`, API login via Identity) |
|---|---|
| ![Profile](screenshots/08-profile.png) | ![Swagger](screenshots/09-swagger.png) |

## 2. Local stack

Everything except the .NET services runs in containers, started by the AppHost (or `docker compose up -d`, see [README](../README.md)).

### Aspire dashboard

Resources started by the AppHost: LocalStack, s3manager, sqlite-web, Identity, Web and Worker.

![Aspire dashboard](screenshots/13-aspire-resources.png)

| Service | URL |
|---|---|
| Web | https://localhost:7281 |
| Identity | https://localhost:5001 |
| Worker (status API) | http://localhost:5290 |
| LocalStack (S3 + SQS) | http://localhost:4566 |
| s3manager | http://localhost:8080 |
| sqlite-web | http://localhost:8081 |

> The screenshots of s3manager were taken on the port Podman published for the container, because `8080` was already taken on the machine. Under a free `8080` the URL above works as listed.

### S3 browser (s3manager)

Bucket `steam-items-uploads`, created by the LocalStack init script:

![s3manager buckets](screenshots/10-s3manager-buckets.png)

The uploaded workbook, stored under `exports/{userId}/{exportId}.xlsx`:

![s3manager objects](screenshots/11-s3manager-objects.png)

## 3. Database web UI (sqlite-web)

Read-only browser for `data/worker/worker.db` and `data/web/web.db`. Switch database from the header dropdown.

![sqlite-web worker.db](screenshots/12-sqlite-web-worker-db.png)

**worker.db**: `ProcessedFiles` (one row per uploaded file) and `ProcessedItems` (one row per Excel row):

![ProcessedFiles](screenshots/14-db-worker-processedfiles.png)

![ProcessedItems](screenshots/15-db-worker-processeditems.png)

**web.db**: `Exports` (the job status that Web polls from the Worker), plus `SteamItems` and `UserSelections`:

![Exports table](screenshots/16-db-web-exports.png)
