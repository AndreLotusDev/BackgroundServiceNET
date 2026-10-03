# 02 – LocalStack setup

Run S3 + SQS locally with the bucket → queue notification already wired.

## Questions to resolve first

1. Does the current `localstack/localstack` image need `LOCALSTACK_AUTH_TOKEN`? If yes, are we OK creating an account, or do we pin an older image tag?
   → Answer: Yes, `latest` needs a token since 2026-03-23. Pin `localstack/localstack:4.12` (no account).
2. UI to browse files: LocalStack web app (needs account) or `cloudlena/s3manager` in the same compose file?
   → Answer: `cloudlena/s3manager` at http://localhost:8080.
3. Names: bucket (`steam-items-uploads`?), queue (`file-uploaded`?), region (`us-east-1`?).
   → Answer: `steam-items-uploads`, `file-uploaded`, `us-east-1`.
4. Do we need a dead-letter queue now, or later in task 10?
   → Answer: Now. `file-uploaded-dlq`, `maxReceiveCount = 5`.

## Scope

- `docker-compose.yml` with LocalStack, `SERVICES=s3,sqs`, port `4566`.
- Init script in `localstack/init/ready.d/` that creates the bucket, the queue, the queue policy and the `s3:ObjectCreated:*` notification.
- Chosen UI available locally.

## Acceptance criteria

- [x] `docker compose up` starts LocalStack with no manual steps.
- [x] `aws --endpoint-url http://localhost:4566 s3 cp test.xlsx s3://<bucket>/` uploads the file.
- [x] After that upload, `aws --endpoint-url http://localhost:4566 sqs receive-message --queue-url <url>` returns an S3 event with the object key.
- [x] The uploaded file is visible in the chosen UI.
- [x] Restarting the containers recreates everything (init script is idempotent).

Verified with Podman (`podman compose`) and `awslocal` inside the container; the AWS CLI is not installed on the host yet.
