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
  SteamItems.Web/              ASP.NET Core MVC
  SteamItems.Worker/           Worker Service
```

Web and Worker do not reference each other. They only share `Contracts` and `ServiceDefaults`.

## Prerequisites

- .NET 10 SDK
- Docker (needed from task 02 on, for LocalStack)

## Run everything (one command)

```bash
dotnet run --project src/SteamItems.AppHost
```

This starts Web and Worker and opens the Aspire dashboard (its URL, including a login token, is printed in the console). Use the dashboard to see the Web URL and the logs of both services. Press `Ctrl+C` to stop everything.

## Run a single service

Web (default MVC home page at http://localhost:5017):

```bash
dotnet run --project src/SteamItems.Web --launch-profile http
```

Worker (logs a heartbeat every 5 seconds and stops cleanly on `Ctrl+C`):

```bash
dotnet run --project src/SteamItems.Worker
```

## Build

```bash
dotnet build SteamItems.sln
```
