# 08 – Worker: SQS listener

Worker receives "file uploaded" events from SQS and hands them to a processor through a `Channel<T>`.

## Questions to resolve first

1. When is the SQS message deleted: right after it is written to the channel, or only after the file is processed? (Affects at-least-once behaviour.)
   → Answer: Only after the file is processed (at-least-once). The channel item carries the SQS receipt handle and the processor deletes the message when it is done. A crash, an error or Ctrl+C leaves the message in SQS; it comes back after the visibility timeout and goes to the DLQ after 5 receives. Duplicates are expected and handled by task 09's idempotency.
2. Channel bounded or unbounded? If bounded, what capacity?
   → Answer: Unbounded.
3. How many processors read the channel: one, or configurable?
   → Answer: Configurable, `Worker:ProcessorCount`, default 1.

## Scope

- `SqsListener : BackgroundService` — long polling (`WaitTimeSeconds = 20`), parses the S3 event into `FileUploadedEvent` (bucket, key).
- `Channel<FileUploadedEvent>` registered as singleton.
- `FileProcessor : BackgroundService` reading the channel (for now: log the key).
- Ignore the `s3:TestEvent` message S3 sends when the notification is created.

## Acceptance criteria

- [x] Uploading a file (via Web or AWS CLI) makes the Worker log the bucket and key within a few seconds.
- [x] Malformed messages are logged and do not stop the listener.
- [x] Listener and processor both stop on Ctrl+C without exceptions in the log.
- [x] Unit test for the S3 event → `FileUploadedEvent` parsing.

Verified with `dotnet test` (29 tests; 18 new in `tests/SteamItems.Worker.Tests`) and by running the Worker against LocalStack (`dotnet run` in `src/SteamItems.Worker`):
`S3EventParserTests` cover a Put event, URL-decoded keys (`+`, `%2B`, UTF-8), several records (non-`ObjectCreated` skipped), the `s3:TestEvent`, and invalid bodies (not JSON, no `Records`, record without bucket/key). `ListenerAndProcessorTests` (fake queue) check that the listener queues uploads, deletes only the test event, keeps going after a malformed message and stops cleanly, and that 3 processors delete all 5 messages after processing.
Running: the leftover `s3:TestEvent` was logged as ignored and deleted. `awslocal s3 cp` to `manual/task 08+test.xlsx` was logged within a second as `Processor 1 got file manual/task 08+test.xlsx in bucket steam-items-uploads`. `sqs send-message` with body `not an s3 event` logged a `warn` with the reason and body, and the listener kept polling. A console Ctrl+C (`GenerateConsoleCtrlEvent`) logged `Application is shutting down...`, `Listener stopped`, `File processors stopped`, with no exception or `fail` in the log.
Uploads through the Web app were not repeated here; task 07 already verified that they produce the same `ObjectCreated:Put` message.

## Notes

- `Messaging/`: `S3EventParser` (body → `Created` / `Ignored` / `Invalid`), `IFileUploadedQueue` + `SqsFileUploadedQueue` (the only place that uses `Amazon.*`, same options pattern as Web's `S3FileStorage`, `Queue` section validated on start), `AddFileUploadedMessaging` registers the queue and the `Channel`.
- The channel holds `FileUploadedMessage` (message id, receipt handle, `FileUploadedEvent[]`), not bare `FileUploadedEvent`, because the processor deletes the message.
- `s3:TestEvent` and messages with no `ObjectCreated` record are deleted by the listener. Malformed messages are **not** deleted: they come back after the visibility timeout and go to `file-uploaded-dlq` after 5 receives.
- A failed receive (LocalStack down) is logged as `warn` and retried after 5 s. Task 10 decides the real retry/backoff policy.
- `FileProcessor` (registered first) runs `ProcessorCount` loops. Hosted services stop in reverse order, so the listener stops polling first and completes the channel. Items still in the channel are not deleted and SQS delivers them again.
- Risk of the unbounded channel: the listener keeps receiving while processors are busy. A message that waits longer than the visibility timeout (30 s default) becomes visible again and is received a second time. Task 09's idempotency covers this, and task 10 can raise the visibility timeout or bound the channel if it shows up.
- Production `appsettings.json` has no `Queue` section yet (same as Web's `FileStorage`), so a non-Development run fails on start until it is configured.
