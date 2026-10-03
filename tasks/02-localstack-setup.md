# 02 – LocalStack setup

Run S3 + SQS locally with the bucket → queue notification already wired.

## Questions to resolve first

1. Does the current `localstack/localstack` image need `LOCALSTACK_AUTH_TOKEN`? If yes, are we OK creating an account, or do we pin an older image tag?
2. UI to browse files: LocalStack web app (needs account) or `cloudlena/s3manager` in the same compose file?
3. Names: bucket (`steam-items-uploads`?), queue (`file-uploaded`?), region (`us-east-1`?).
4. Do we need a dead-letter queue now, or later in task 10?

## Scope

- `docker-compose.yml` with LocalStack, `SERVICES=s3,sqs`, port `4566`.
- Init script in `localstack/init/ready.d/` that creates the bucket, the queue, the queue policy and the `s3:ObjectCreated:*` notification.
- Chosen UI available locally.

## Acceptance criteria

- [ ] `docker compose up` starts LocalStack with no manual steps.
- [ ] `aws --endpoint-url http://localhost:4566 s3 cp test.xlsx s3://<bucket>/` uploads the file.
- [ ] After that upload, `aws --endpoint-url http://localhost:4566 sqs receive-message --queue-url <url>` returns an S3 event with the object key.
- [ ] The uploaded file is visible in the chosen UI.
- [ ] Restarting the containers recreates everything (init script is idempotent).
