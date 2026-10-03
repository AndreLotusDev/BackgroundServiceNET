# SteamItems – Background Service sample

Two decoupled .NET 10 services:

- **SteamItems.Web**: ASP.NET Core MVC app. The user picks Steam items, exports them to Excel and uploads the file.
- **SteamItems.Worker**: Worker Service (`BackgroundService`) that gets notified about uploaded files and processes them row by row.

Design notes: [docs/session-01-sample-project.md](docs/session-01-sample-project.md). Work items: [tasks/](tasks/README.md).

## Solution layout

```
SteamItems.sln
src/
  SteamItems.AppHost/          .NET Aspire orchestration (starts everything)
  SteamItems.ServiceDefaults/  Aspire defaults: OpenTelemetry, health checks, resilience
  SteamItems.Contracts/        Shared contract between Web and Worker (Excel layout, event DTO)
  SteamItems.Identity/         Duende IdentityServer + ASP.NET Identity (login, register, tokens)
  SteamItems.Web/              ASP.NET Core MVC (OIDC client of SteamItems.Identity)
  SteamItems.Worker/           Worker Service
```

Web and Worker do not reference each other. They only share `Contracts` and `ServiceDefaults`. Web talks to Identity over OIDC only.

## Prerequisites

- .NET 10 SDK
- Docker (needed from task 02 on, for LocalStack)

## Local AWS (LocalStack)

```bash
docker compose up -d
```

(`podman compose up -d` works too.) This starts:

- **LocalStack 4.12** on http://localhost:4566 with S3 and SQS. On startup, [localstack/init/ready.d](localstack/init/ready.d/01-create-resources.sh) creates:
  - bucket `steam-items-uploads`
  - queue `file-uploaded` (dead-letter queue `file-uploaded-dlq` after 5 receives)
  - an `s3:ObjectCreated:*` notification from the bucket to the queue
- **s3manager** on http://localhost:8080 to browse the bucket.

LocalStack keeps state only in memory: `docker compose down` wipes it, and the next `up` recreates the resources. Credentials are `test` / `test`, region `us-east-1`.

Try it with the AWS CLI:

```bash
aws --endpoint-url http://localhost:4566 s3 cp test.xlsx s3://steam-items-uploads/
aws --endpoint-url http://localhost:4566 sqs receive-message --queue-url http://localhost:4566/000000000000/file-uploaded
```

The first message on the queue is an `s3:TestEvent`, which S3 sends when the notification is created.

## Run everything (one command)

```bash
dotnet run --project src/SteamItems.AppHost
```

This starts Identity, Web and Worker and opens the Aspire dashboard (its URL, including a login token, is printed in the console). Use the dashboard to see the Web URL and the logs of both services. Press `Ctrl+C` to stop everything.

## Run a single service

Identity (https://localhost:5001, discovery at `/.well-known/openid-configuration`). In Development it applies migrations to `identity.db` and seeds the test users on startup:

```bash
dotnet run --project src/SteamItems.Identity --launch-profile https
```

Web (https://localhost:7281). Needs Identity running; login only works over HTTPS:

```bash
dotnet run --project src/SteamItems.Web --launch-profile https
```

Worker (logs a heartbeat every 5 seconds and stops cleanly on `Ctrl+C`):

```bash
dotnet run --project src/SteamItems.Worker
```

## Login

Click **Log in** (or open **Profile**, which requires login). Test users come from `SeedUsers` in [src/SteamItems.Identity/appsettings.Development.json](src/SteamItems.Identity/appsettings.Development.json): `alice` / `bob`, password `Pass123$`. New users can sign up from the **Register** link on the login page.

IdentityServer runs without a Duende license key (allowed for development/testing), so it logs license warnings at startup.

## Build

```bash
dotnet build SteamItems.sln
```
