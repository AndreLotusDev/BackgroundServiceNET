# 08 – Worker: SQS listener

Worker receives "file uploaded" events from SQS and hands them to a processor through a `Channel<T>`.

## Questions to resolve first

1. When is the SQS message deleted: right after it is written to the channel, or only after the file is processed? (Affects at-least-once behaviour.)
2. Channel bounded or unbounded? If bounded, what capacity?
3. How many processors read the channel: one, or configurable?

## Scope

- `SqsListener : BackgroundService` — long polling (`WaitTimeSeconds = 20`), parses the S3 event into `FileUploadedEvent` (bucket, key).
- `Channel<FileUploadedEvent>` registered as singleton.
- `FileProcessor : BackgroundService` reading the channel (for now: log the key).
- Ignore the `s3:TestEvent` message S3 sends when the notification is created.

## Acceptance criteria

- [ ] Uploading a file (via Web or AWS CLI) makes the Worker log the bucket and key within a few seconds.
- [ ] Malformed messages are logged and do not stop the listener.
- [ ] Listener and processor both stop on Ctrl+C without exceptions in the log.
- [ ] Unit test for the S3 event → `FileUploadedEvent` parsing.
