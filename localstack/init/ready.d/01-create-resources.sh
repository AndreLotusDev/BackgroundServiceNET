#!/bin/bash
# Creates the bucket, the queues and the S3 -> SQS notification.
# Runs every time LocalStack becomes ready; every step is idempotent.
set -euo pipefail

REGION="us-east-1"
ACCOUNT_ID="000000000000"
BUCKET="steam-items-uploads"
QUEUE="file-uploaded"
DLQ="file-uploaded-dlq"
MAX_RECEIVE_COUNT=5

QUEUE_ARN="arn:aws:sqs:${REGION}:${ACCOUNT_ID}:${QUEUE}"
DLQ_ARN="arn:aws:sqs:${REGION}:${ACCOUNT_ID}:${DLQ}"

echo "[init] bucket ${BUCKET}"
if ! awslocal s3api head-bucket --bucket "${BUCKET}" >/dev/null 2>&1; then
  awslocal s3api create-bucket --bucket "${BUCKET}" --region "${REGION}"
fi

echo "[init] queue ${DLQ}"
awslocal sqs create-queue --queue-name "${DLQ}" >/dev/null

echo "[init] queue ${QUEUE}"
QUEUE_URL=$(awslocal sqs create-queue --queue-name "${QUEUE}" --query QueueUrl --output text)

# Redrive + policy allowing the bucket to send messages.
# set-queue-attributes overwrites, so re-running is safe.
cat > /tmp/queue-attributes.json <<EOF
{
  "RedrivePolicy": "{\"deadLetterTargetArn\":\"${DLQ_ARN}\",\"maxReceiveCount\":\"${MAX_RECEIVE_COUNT}\"}",
  "Policy": "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Effect\":\"Allow\",\"Principal\":{\"Service\":\"s3.amazonaws.com\"},\"Action\":\"sqs:SendMessage\",\"Resource\":\"${QUEUE_ARN}\",\"Condition\":{\"ArnEquals\":{\"aws:SourceArn\":\"arn:aws:s3:::${BUCKET}\"}}}]}"
}
EOF
awslocal sqs set-queue-attributes --queue-url "${QUEUE_URL}" --attributes file:///tmp/queue-attributes.json

echo "[init] notification ${BUCKET} -> ${QUEUE}"
awslocal s3api put-bucket-notification-configuration \
  --bucket "${BUCKET}" \
  --notification-configuration "{\"QueueConfigurations\":[{\"QueueArn\":\"${QUEUE_ARN}\",\"Events\":[\"s3:ObjectCreated:*\"]}]}"

echo "[init] done. Queue URL: ${QUEUE_URL}"
