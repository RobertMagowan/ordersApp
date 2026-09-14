# Sequential publisher proof

Run `docker compose -f local/compose.yml up -d`, apply the already-authorised SQL migration with the migration runner, and invoke the timer function once. Inspect `dbo.OutboxMessages` before and after sending: a successful send has `ProcessedAt` set; broker failure leaves it `NULL`. The emulator is suitable only for sequential send checks. It does not prove Azure RBAC, lease locking, multi-instance concurrency, or production Service Bus Standard behavior.
