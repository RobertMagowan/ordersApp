# Sequential publisher proof

Run `docker compose -f local/compose.yml up -d`, apply the already-authorised SQL migration with the migration runner, copy `local/local.settings.json.example` to the Functions app's local settings, and start the app with `func start --csharp`. Invoke the timer function once (or wait for the ten-second schedule). Inspect `dbo.OutboxMessages` before and after sending: a successful send has `ProcessedAt` set; broker failure leaves it `NULL`.

The Service Bus emulator listens on container port `5672`. Its host port defaults to `5672` and can be changed when another local process or container already owns that port, for example `$env:SERVICEBUS_HOST_PORT=5673; docker compose -f local/compose.yml up -d`. SQL Server listens on container port `1433`; its host port defaults to `1433` and can similarly be overridden, for example `$env:MSSQL_HOST_PORT=1434; docker compose -f local/compose.yml up -d`.

The emulator is suitable only for sequential send checks. It does not prove Azure RBAC, lease locking, multi-instance concurrency, or production Service Bus Standard behavior.
