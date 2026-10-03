# 11 – Job status back to the user

User uploads, then later sees that the job finished.

## Questions to resolve first

1. Do we do this at all in the sample? (Open question 3 in the doc.)
2. Mechanism: Worker publishes to a second SQS queue that Web consumes, or Web polls a Worker status endpoint? (Second queue keeps the services decoupled.)
3. How the user sees it: status page with refresh, or live update (SignalR)?

## Scope

- Status message: export id/key, status, processed count, failed count.
- Web stores the status on its `Export` record and shows it.

## Acceptance criteria

- [ ] After the Worker finishes a file, the user sees `Completed` with counts on the status page.
- [ ] A file with failed rows shows `Completed with errors` and the failed count.
- [ ] Web still has no access to `worker.db`.
- [ ] If the Worker is down, the status stays `Pending` and updates once it is back.
