# Session 01 – Sample project definition

Date: 2026-10-02

## Goal

Build a sample solution with **two decoupled services** that only talk to each other through a storage system plus a notification:

1. **ASP.NET Core MVC web app** – the user logs in, picks Steam game items, exports them to Excel, and uploads the Excel file to storage.
2. **Worker Service** – gets notified when a new file lands in storage, downloads it, and processes it row by row into its own database.

## Requirements (as stated)

- ASP.NET Core MVC
- Duende IdentityServer for authentication
- Database: SQLite
- Swagger with a working login flow (authorize in Swagger UI, get a token, call the API)
- User can select multiple Steam game items in the UI
- Export the selected items to an Excel file
- Send the Excel file to a storage system
- Storage notifies the Worker Service
- Worker picks up the file and processes **item by item**, storing in a **different database**
- Storage should be a **local emulator of a cloud provider, with a UI** to browse files → **decided: LocalStack** (see [Storage decision](#storage-decision-localstack))

## Proposed architecture

```
 ┌──────────────────────────┐        upload .xlsx        ┌───────────────────────┐
 │  Web (ASP.NET Core MVC)  │ ─────────────────────────▶ │  S3 bucket             │
 │  - Duende IdentityServer │                            │  (LocalStack)          │
 │  - MVC pages + API       │                            └──────────┬────────────┘
 │  - Swagger (OAuth2 PKCE) │                                       │ s3:ObjectCreated:* event
 │  - SQLite: web.db        │                                       ▼
 └──────────────────────────┘                            ┌───────────────────────┐
                                                         │  SQS queue             │
                                                         │  (LocalStack)          │
                                                         └──────────┬────────────┘
                                                                    │
                                                                    ▼
                                                         ┌───────────────────────┐
                                                         │  Worker Service        │
                                                         │  - BackgroundService   │
                                                         │  - reads .xlsx rows    │
                                                         │  - SQLite: worker.db   │
                                                         └───────────────────────┘
```

The two services share **no code path and no database**. The contract between them is:
- the bucket/container name,
- the Excel layout (columns), and
- the event message shape.

A small `Contracts` class library can hold the Excel column definitions and the event DTO if we want compile-time safety.

## Suggested solution layout

```
src/
  SteamItems.Web/          ASP.NET Core MVC + Duende IdentityServer + API + Swagger
  SteamItems.Worker/       Worker Service (BackgroundService)
  SteamItems.Contracts/    Shared DTOs: ExcelRow, FileUploadedEvent
  SteamItems.AppHost/      (optional) .NET Aspire orchestration
docker-compose.yml         LocalStack (S3 + SQS)
localstack/init/           init script: create bucket, queue and S3 → SQS notification
```

## Technical decisions / notes

| Topic | Choice | Notes |
|---|---|---|
| Identity | Duende IdentityServer + ASP.NET Identity (EF Core, SQLite) | Duende is free for development/testing; check the Community Edition terms before any production use. |
| Swagger login | Swashbuckle (or Scalar) with OAuth2 **Authorization Code + PKCE** | Register a `swagger` client in IdentityServer with redirect URI `/swagger/oauth2-redirect.html`. Newer .NET templates ship `Microsoft.AspNetCore.OpenApi` instead of Swashbuckle, so add it explicitly. |
| Web DB | SQLite `web.db` | Users, Steam items catalog, user selections. |
| Worker DB | SQLite `worker.db` | Processed items + a `ProcessedFiles` table for idempotency. |
| Steam items | Seed a static list first | Later: Steam Web API (needs an API key) or the public store endpoints. |
| Excel | ClosedXML (MIT) | Used both for writing (Web) and reading (Worker). |
| Storage | **LocalStack** – S3 | Bucket for the uploaded `.xlsx` files. Accessed with `AWSSDK.S3` pointing at `http://localhost:4566` (`ForcePathStyle = true`). |
| Notification | **LocalStack** – S3 event → SQS | Bucket notification on `s3:ObjectCreated:*` sends a message to an SQS queue. Worker long-polls the queue with `AWSSDK.SQS`. |
| Storage access | `IFileStorage` abstraction | Lets us swap emulator/provider without touching business code. |
| Worker → processing | `BackgroundService` + `Channel<T>` | Listener writes events into a channel; a processor reads them. Matches the notes in `Notes.md`. |

## Worker behaviour to cover (ties to `Todo.mnd`)

- Idempotency: the same file event may arrive twice; skip files/rows already processed.
- Per-row error handling: one bad row should not kill the whole file (log it, store as failed).
- Exceptions in `ExecuteAsync`: by default since .NET 6 an unhandled exception stops the host (`BackgroundServiceExceptionBehavior.StopHost`). Decide whether to catch-and-continue.
- Graceful shutdown: respect `stoppingToken`, finish the current row, configure `HostOptions.ShutdownTimeout`.
- Fire-and-forget flow: user uploads, later sees "job finished" (worker writes a status the web app can read, or sends a message back).

## Open questions for next session

1. ~~Which storage emulator to use~~ → **LocalStack** (decided, see below).
2. Use .NET Aspire for local orchestration, or plain `docker-compose` + multiple startup projects?
3. Does the user get notified when the worker finishes? If yes: a status queue/topic back to the web app, or polling a status endpoint.

## Storage emulator options discussed

| Emulator | Mimics | Built-in UI | Native "file created" notification | Notes |
|---|---|---|---|---|
| **LocalStack** | AWS S3, SQS, SNS, ... | Web app at app.localstack.cloud (needs account) | Yes: S3 event → SQS/SNS | Closest to real AWS. Check current licensing/auth token requirements. |
| **MinIO** | S3 API | Yes (web console) | Yes: bucket notifications → webhook, AMQP (RabbitMQ), Kafka, NATS, Redis, Postgres | Community edition console features were reduced and distribution changed in 2025; verify the image/tag you use. |
| **Azurite** | Azure Blob, Queue, Table | Azure Storage Explorer (desktop app) | No Event Grid; app posts a message to Azure Queue after upload | First-class in .NET Aspire (`AddAzureStorage().RunAsEmulator()`). |
| **Moto (server mode)** | AWS S3, SQS, SNS, ... | Basic dashboard at `/moto-api/` | Yes: S3 → SQS | Lightweight, Python, fully open source. |
| **fake-gcs-server** | Google Cloud Storage | None | Can publish to the Pub/Sub emulator | Pair with the gcloud Pub/Sub emulator. |
| **SeaweedFS / Garage** | S3 API | SeaweedFS filer UI / Garage none | Limited | More "real storage" than emulators. |

Generic UIs for any S3-compatible endpoint: `cloudlena/s3manager` (web, Docker), Cyberduck, S3 Browser (Windows), AWS CLI with `--endpoint-url`.

## Storage decision: LocalStack

We are going to use **LocalStack** as the local cloud emulator.

Why:
- Closest to real AWS, so the same code works against a real S3/SQS later by changing only the endpoint/credentials.
- Native "file created" notification: S3 bucket notifications → SQS, no custom webhook needed.
- Has a UI to browse buckets and queues (LocalStack web app at app.localstack.cloud, connected to the local instance).

How it fits:
- `docker-compose.yml` runs the `localstack/localstack` image exposing port `4566`, with `SERVICES=s3,sqs`.
- An init script (mounted in `/etc/localstack/init/ready.d/`) creates the bucket, the queue, and the `s3:ObjectCreated:*` → SQS notification.
- Web app uploads with `AWSSDK.S3`; Worker reads the queue with `AWSSDK.SQS`, then downloads the file from S3.
- Local credentials are dummy values (e.g. `test` / `test`, region `us-east-1`).

To check before starting:
- Current LocalStack licensing / auth token requirement for the image we pull (`LOCALSTACK_AUTH_TOKEN`).
- If the web app UI needs an account we don't want, fall back to `cloudlena/s3manager` or the AWS CLI with `--endpoint-url http://localhost:4566`.
