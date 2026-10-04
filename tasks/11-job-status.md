# 11 – Job status back to the user

User uploads, then later sees that the job finished.

## Questions to resolve first

1. Do we do this at all in the sample? (Open question 3 in the doc.)
   → Answer: Yes.
2. Mechanism: Worker publishes to a second SQS queue that Web consumes, or Web polls a Worker status endpoint? (Second queue keeps the services decoupled.)
   → Answer: Web polls a Worker status endpoint. The Worker gets a small HTTP endpoint, `GET /api/files/status?key=…&key=…`, that reads `worker.db`. A `BackgroundService` in Web polls it for exports that are not finished yet and stores the result on the `Exports` row. Files are matched by object key, not export id: a file the Worker rejects as unreadable has no export id.
3. How the user sees it: status page with refresh, or live update (SignalR)?
   → Answer: Live update with SignalR. `/Exports` renders the stored status; when the poller changes an export, Web pushes it to that user's connections and the row updates in place.

## Scope

- Status message: export id/key, status, processed count, failed count.
- Web stores the status on its `Export` record and shows it.

## Acceptance criteria

- [x] After the Worker finishes a file, the user sees `Completed` with counts on the status page.
- [x] A file with failed rows shows `Completed with errors` and the failed count.
- [x] Web still has no access to `worker.db`.
- [x] If the Worker is down, the status stays `Pending` and updates once it is back.

Verified with `dotnet test` (73 tests; 17 new) and by running LocalStack, Identity, Web and Worker (Development):
- Tests: `FileStatusQueryTests` (Worker, migrated in-memory SQLite) check stored counts for a completed file, rows so far for a file still processing, the reason for a rejected file, unknown keys left out, and the latest file for a re-uploaded key. `ExportStatusUpdaterTests` (Web, fake Worker and notifier) check `Completed`, `Completed with errors`, `Failed`, progress pushed only when it changes, an unseen export staying `Pending`, Worker down then back, finished exports not asked about again, and batches of 50 keys. `HttpWorkerStatusClientTests` check the query string (keys escaped), the JSON contract, and that a 500, a refused connection or a non-JSON body become `WorkerUnavailableException`.
- Web started with the Worker stopped. `AddExportOutcome` was applied and the existing `Uploaded` export showed as `Pending`. The poller logged one `warn` (connection refused to `localhost:5290`) and no more after that. Logged in as `alice`, **Upload Excel** landed on `/Exports` with the new row `Pending`.
- The Worker was started without reloading the page. It imported the file (`2 processed, 0 failed`), Web logged `Updated the status of 1 export(s)`, and the row turned `Completed`, `2`, `0` with the completion time, still on the same page load (checked with a flag set on `window` before).
- `Completed with errors`: the other `Pending` export had never reached the Worker (its message was consumed during task 07's manual check). A copy of a workbook with `"free"` in row 3's `Price` was PUT to that export's key. The Worker logged `row 3 failed` and `1 processed, 1 failed`, and the row turned into a yellow `Completed with errors` badge, `1`, `1`, without a reload. The workbook inside carried the *other* export's id, and the match was still right because it goes by object key.
- After a reload both rows show the same status, read from `web.db`. `GET /api/files/status` with no key returns `400`, and an unknown key is left out of the result.
- Web references neither `worker.db` nor the Worker project. It only calls the HTTP endpoint and shares the DTO in `Contracts`.

## Notes

- **Contract** (`SteamItems.Contracts/Status/FileStatus.cs`): `FileStatusApi` (path, `key` parameter, max 50 keys) and `FileStatusResponse(Key, Status, ProcessedCount, FailedCount, Error, StartedAt, CompletedAt)`. `FileProcessingStatus` is `Processing` / `Completed` / `Failed`, serialized as a string.
- **Worker**: now a `WebApplication` (`FrameworkReference Microsoft.AspNetCore.App`, the explicit `Microsoft.Extensions.Hosting` package removed as redundant). It listens on `http://localhost:5290` (launch profile). `Status/FileStatusQuery` returns the latest file per key. For a file still `Processing`, the counts come from `ProcessedItems`, because `ProcessedFiles` counts are only written at the end. `MapDefaultEndpoints` (health checks) is mapped too. `Microsoft.AspNetCore` logs at `Warning`, so a poll every 5 s does not add request lines.
- **Web**: `Status/ExportStatusPoller` (`BackgroundService`, `PeriodicTimer`, `WorkerStatus:PollInterval` 5 s) creates a scope per round and runs `ExportStatusUpdater`. The updater loads the `Pending`/`Processing` exports, asks the Worker in batches of 50, saves each batch and then pushes each changed export. When every export is finished, no request is made. `HttpWorkerStatusClient` is a typed `HttpClient`, so the service defaults' standard resilience handler wraps it. The poller logs the Worker going away and coming back once each, and catches any other error so the web app keeps running (`StopHost` is the default).
- **Statuses**: `Uploaded` was renamed to `Pending`. The migration `AddExportOutcome` updates existing rows and adds `ProcessedCount`, `FailedCount`, `Error`, `CompletedAt` and an index on `Status`. Mapping: `Processing` → `Processing` (counts so far), `Completed` with 0 failed → `Completed`, `Completed` with failed rows → `CompletedWithErrors` ("Completed with errors"), `Failed` → `Failed` with the reason. `POST /api/items/export` now answers `"Status": "Pending"`.
- **SignalR**: `ExportsHub` at `/hubs/exports` (`[Authorize]`, cookie). It only sends from server to client: `exportUpdated` with `ExportStatusMessage`, which carries the badge text and class so the script stays small. `SubjectUserIdProvider` maps SignalR users to the `sub` claim (claims are not remapped), and `Clients.User(export.UserId)` sends to every tab of the owner. `wwwroot/js/exports.js` updates the row in place and reloads the page after a reconnect, since updates sent while disconnected are lost. The browser client is vendored at `wwwroot/lib/microsoft-signalr` (`@microsoft/signalr` 10.0.11).
- Found while testing: the table was `id="exports"`, which makes `window.exports` exist. The SignalR UMD bundle then took its CommonJS branch and attached itself to the table element, so `window.signalR` was undefined. The table is now `id="export-list"`, with a comment saying why.
- AppHost: Web gets `WorkerStatus__BaseUrl` from the Worker's `http` endpoint, with no `WaitFor(worker)`, because Web works without the Worker.
- Still open:
  - The Worker endpoint has **no authentication**. It only returns status and counts for keys the caller already knows, but it is not protected in any way. Before a real deployment, add a client-credentials client in Identity (or at least bind it to an internal network).
  - An export whose message never reaches the Worker (consumed elsewhere, or moved to the DLQ before any import, e.g. a missing object) stays `Pending` for ever. The Worker has nothing to report for it, and Web keeps asking for it every round. A timeout on Web's side (e.g. `Pending` for more than N hours → `Unknown`) would close that loop.
  - Web has no `GET` for a single export's status in the API yet. Swagger users only see `Pending` in the `POST` answer.
  - Production `appsettings.json` has no `WorkerStatus:BaseUrl`, so a non-Development Web fails on start until it is configured (same as `FileStorage`).
