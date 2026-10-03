# 01 – Solution skeleton

Create the solution and empty projects so everything after this has a place to go.

## Questions to resolve first

1. .NET Aspire or plain `docker-compose` + multiple startup projects?
   → Answer: .NET Aspire (`SteamItems.AppHost` + `SteamItems.ServiceDefaults`).
2. Keep a `SteamItems.Contracts` library, or let each service own its copy of the Excel layout?
   → Answer: Keep `SteamItems.Contracts`.
3. Target .NET version (8 LTS or 10 LTS)?
   → Answer: .NET 10 LTS.

## Scope

- `SteamItems.sln` with `src/SteamItems.Web` (MVC), `src/SteamItems.Worker` (Worker Service), and `src/SteamItems.Contracts` / `src/SteamItems.AppHost` depending on the answers above.
- `.gitignore` for .NET, ignoring `*.db`.
- One command to run everything locally.

## Acceptance criteria

- [x] `dotnet build` succeeds with no warnings treated as errors.
- [x] Web starts and shows the default MVC home page.
- [ ] Worker starts and logs a heartbeat line, then stops cleanly on Ctrl+C.
- [x] The README in the repo root explains how to run both.
- [x] Web and Worker have no project reference to each other.
