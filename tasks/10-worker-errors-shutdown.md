# 10 – Worker: errors and shutdown

Make the Worker behave correctly when things fail or the host stops.

## Questions to resolve first

1. Unhandled exception in `ExecuteAsync`: keep `StopHost` (default) or `Ignore`, or catch-and-continue inside the loop?
   → Answer: Both. The loops catch the failures they expect (queue, S3, SQLite, a bad message) and keep going. `BackgroundServiceExceptionBehavior` stays `StopHost`, now set in config: an exception that still escapes `ExecuteAsync` is a bug, and the process stops instead of running half-alive.
2. Retry policy for S3/SQS/SQLite failures: how many attempts, which backoff?
   → Answer: Polly via `Microsoft.Extensions.Resilience`, four named pipelines (`Resilience/WorkerPipelines.cs`), all exponential with jitter:
   - `queue-receive`: retries forever, 1 s → 30 s max, until SQS answers or the Worker stops.
   - `queue-acknowledge` (delete / release / visibility): 3 retries from 500 ms.
   - `storage` (S3 download): 3 retries from 1 s, transient errors only (network, timeout, 5xx, 408, 429). Behind it, a circuit breaker opens for 15 s when at least half of at least 4 downloads in 30 s fail.
   - `database` (SQLite `SaveChanges`): 3 retries from 200 ms when SQLite reports `BUSY`/`LOCKED`.
   A file that still fails is left on the queue: SQS redelivers it after the visibility timeout.
3. Add an SQS dead-letter queue (`maxReceiveCount`)? If yes, update the task 02 init script.
   → Answer: Already there since task 02: `file-uploaded-dlq`, `maxReceiveCount = 5`. Init script unchanged.
4. `HostOptions.ShutdownTimeout` value (default 30s).
   → Answer: 60 s, in `appsettings.json` (`HostOptions:ShutdownTimeout`).

## Scope

- Exception strategy per the answers above, with logging.
- Respect `stoppingToken`: finish the current row, stop reading new ones.
- Messages not processed on shutdown go back to the queue (not deleted).

## Acceptance criteria

- [x] Stopping LocalStack while the Worker runs: errors are logged, Worker keeps running (or stops, per decision), and resumes when LocalStack is back.
- [x] Ctrl+C during a large file: current row is committed, process exits within the shutdown timeout.
- [x] After restart, the interrupted file is picked up again and finishes without duplicates.
- [x] A message that always fails ends up in the DLQ (if DLQ chosen).

Verified with `dotnet test` (56 tests; 13 new in `tests/SteamItems.Worker.Tests`) and by running the Worker (Development) against LocalStack with a 20,000-row workbook:
- Tests: the listener keeps retrying while the queue is down and gets the next message; on shutdown it lets a running receive finish and passes on what it returned. The heartbeat keeps a message hidden while it is processed. On shutdown, the file in progress and the waiting messages are released, not deleted. Cancelling while a row is being saved still commits that row, and a second run finishes the file with no duplicates. A short storage outage is retried, a missing object is not, and the `storage` circuit opens after repeated failures.
- Ctrl+C 12 s into `manual/task10-large3.xlsx`: `Stopping file 5 before row 6624`, `Released message … back to the queue`, process gone 8.4 s after Ctrl+C. Rows 2–6623 were stored and the message was visible on the queue at once. Restarted: `Resuming … file 5`, then `Imported …: 20000 processed`. `ProcessedItems` has 20,000 rows and 20,000 distinct row numbers. The message was received once more and deleted.
- An event for a key that does not exist (`manual/task10-missing.xlsx`): logged as `fail` on each receive, with no in-process retries because a 404 is not transient. It went to `file-uploaded-dlq` after 5 receives.
- `podman stop localstack` while running: `Could not receive messages (attempt 1…6), retrying in …` warnings with growing delays (0.8 s, 1.3 s, 1.1 s, 3.6 s, 5 s, … 28 s), and the process kept running. `podman start localstack`: the next attempt succeeded, the new `s3:TestEvent` was ignored, and a new upload was imported (20,000 rows).

## Notes

- **Shutdown order.** The listener stops first and completes the channel. Its running long poll is *not* cancelled, because SQS keeps a cancelled long poll open on its side. Found while testing: a message released on shutdown went to that abandoned poll and stayed invisible for 30 s, and the receive counted towards the DLQ. The poll now runs to its end (≤ 20 s, capped at 25 s after shutdown starts), and whatever it returns goes to the channel. Then the processors stop: the file in progress stops between rows (the row being saved is committed with `CancellationToken.None`), and that message plus everything left in the channel is released (`ChangeMessageVisibility` 0). A Ctrl+C can therefore take up to ~20 s; the timeout is 60 s.
- **Visibility heartbeat** (`Worker:VisibilityHeartbeat` 10 s, `Worker:VisibilityExtension` 30 s). Not in the original answers; added after a test run showed the need. A 20,000-row file takes over a minute. Without the heartbeat, SQS handed the message out again every 30 s mid-import. After 5 receives it moved a file that was *successfully* imported to the DLQ, and the final delete did not fail. Now the processor extends the visibility every 10 s while it works on a message. If the Worker dies, the message is back within 30 s.
- `IFileUploadedQueue.ChangeVisibilityAsync(receiptHandle, timeout)` handles both the heartbeat and the release (`TimeSpan.Zero`).
- `FileStorageException.IsTransient`: `S3FileStorage` sets it to false for 4xx answers (except 408/429), so a missing object or access denied is not retried in-process and goes to the DLQ through SQS.
- Implementations (`SqsFileUploadedQueue`, `S3FileStorage`) still make one attempt (plus the AWS SDK's own 2 retries). Callers pick the pipeline. Pipeline logs are custom (`OnRetry`, circuit opened/closed), and Polly's built-in log category is set to `None` to avoid duplicate lines.
- Tests use `TestPipelines`: the same retry rules with no delays and no circuit breaker. `WorkerPipelinesTests` checks the real registrations.
- Still open: messages *waiting* in the unbounded channel get no heartbeat (only the one being processed does). With `ProcessorCount = 1` and a burst of large files, a waiting message can still be redelivered (the importer skips it as a duplicate) and its receives count towards the DLQ. Bound the channel or raise the queue's visibility timeout if this shows up.
