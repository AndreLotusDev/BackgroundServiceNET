# 10 – Worker: errors and shutdown

Make the Worker behave correctly when things fail or the host stops.

## Questions to resolve first

1. Unhandled exception in `ExecuteAsync`: keep `StopHost` (default) or `Ignore`, or catch-and-continue inside the loop?
2. Retry policy for S3/SQS/SQLite failures: how many attempts, which backoff?
3. Add an SQS dead-letter queue (`maxReceiveCount`)? If yes, update the task 02 init script.
4. `HostOptions.ShutdownTimeout` value (default 30s).

## Scope

- Exception strategy per the answers above, with logging.
- Respect `stoppingToken`: finish the current row, stop reading new ones.
- Messages not processed on shutdown go back to the queue (not deleted).

## Acceptance criteria

- [ ] Stopping LocalStack while the Worker runs: errors are logged, Worker keeps running (or stops, per decision), and resumes when LocalStack is back.
- [ ] Ctrl+C during a large file: current row is committed, process exits within the shutdown timeout.
- [ ] After restart, the interrupted file is picked up again and finishes without duplicates.
- [ ] A message that always fails ends up in the DLQ (if DLQ chosen).
