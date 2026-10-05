# 14 – Databases outside the projects + DB browser

Today `worker.db` is created in the Worker's working directory (`src/SteamItems.Worker/worker.db`), next to the source code and the build output. Nothing outside the Worker process can look at it while the app runs. The developer runs the AppHost and gets the Worker's database in a data folder outside the project, plus a web page (like s3manager for S3) to browse its tables. Same for Web's `web.db`.

## Questions to resolve first

1. Where does the data live: a folder in the repo (bind mount, gitignored), or a named container volume?
   → Answer: repo folder `data/worker/` (gitignored). The Worker runs on the host, not in a container, so a named volume would not be reachable by it; a host folder is the only thing both the Worker and a container can open.
2. Who decides the path: the Worker's `appsettings`, or the AppHost?
   → Answer: the AppHost. It creates `data/worker/` and passes `ConnectionStrings__WorkerDb=Data Source=<absolute path>/worker.db` to the Worker. A standalone `dotnet run --project src/SteamItems.Worker` keeps the old `Data Source=worker.db` from `appsettings.Development.json`.
3. Which DB browser? Build our own page (Fila, task 13 rule) or an off-the-shelf container like `s3manager`?
   → Answer: off-the-shelf `ghcr.io/coleifer/sqlite-web`, started by the AppHost like `s3manager`. It is a developer tool, not a page of Web or Identity, so the Fila rule does not apply.
4. Read-only or read-write browser?
   → Answer: read-only (`--read-only`). The Worker owns the data; editing rows by hand under a running processor is how imports get into states the code never produces.
5. SQLite locks do not cross the host ↔ container VM boundary (Windows/macOS + Docker/Podman). In WAL mode even a read-only reader writes to `worker.db-shm`, which the Worker also memory-maps: that can corrupt the WAL index. What journal mode?
   → Answer: in Development, after migrating, the Worker switches `worker.db` to `journal_mode=DELETE` (persisted in the file). A read-only reader then never writes any file the Worker uses. Cost: readers and the writer block each other briefly; the existing SQLite busy retry in `WorkerPipelines` covers it, and `ProcessorCount` is 1. The browser can still hit a half-written transaction and show an error: refresh.
6. Port?
   → Answer: fixed `http://localhost:8081` (8080 is `s3manager`).
7. Web's `web.db` and Identity's `identity.db` too?
   → Answer: `web.db` yes: `data/web/web.db`, same journal-mode switch in Web. One `sqlite-web` container opens both files (database switcher in its header). `identity.db` not in this task.

## Scope

- `.gitignore`: `data/`.
- AppHost: create `data/worker/` and `data/web/`, pass the connection strings to Worker and Web, add a `sqlite-web` container bind-mounting both folders under `/data`, read-only, port 8081, waits for Worker and Web (healthy = migrations applied, so the files exist).
- Worker and Web: after `MigrateAsync` in Development, `PRAGMA journal_mode=DELETE`.
- README: where the data is and how to open the browser.

## Acceptance criteria

- [ ] `dotnet run --project src/SteamItems.AppHost` creates `data/worker/worker.db` and `data/web/web.db`; nothing new is written to `src/SteamItems.Worker/` or `src/SteamItems.Web/`.
- [ ] The Aspire dashboard shows a `sqlite-web` resource; http://localhost:8081 lists `ProcessedFiles` and `ProcessedItems`, and switching to `web.db` lists Web's tables (`Exports`, …).
- [ ] After uploading an export, its rows appear in `ProcessedItems` in the browser (refresh).
- [ ] The browser cannot insert, edit or delete rows.
- [ ] Both files are in `delete` journal mode (no `-wal` file next to them while the services run).
- [ ] Stopping and restarting the AppHost keeps the data (it is a host folder, not container state).
- [ ] Standalone `dotnet run` of the Worker or Web still works with its own `worker.db` / `web.db`.
