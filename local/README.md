# Sequential publisher proof

Run `docker compose -f local/compose.yml up -d`, apply the already-authorised SQL migration with the migration runner, copy `local/local.settings.json.example` to the Functions app's local settings, and start the app with `func start --csharp`. Invoke the timer function once (or wait for the ten-second schedule). Inspect `dbo.OutboxMessages` before and after sending: a successful send has `ProcessedAt` set; broker failure leaves it `NULL`.

The Service Bus emulator listens on container port `5672`. Its host port defaults to `5672` and can be changed when another local process or container already owns that port, for example `$env:SERVICEBUS_HOST_PORT=5673; docker compose -f local/compose.yml up -d`. SQL Server listens on container port `1433`; its host port defaults to `1433` and can similarly be overridden, for example `$env:MSSQL_HOST_PORT=1434; docker compose -f local/compose.yml up -d`.

The emulator is suitable only for sequential send checks. It does not prove Azure RBAC, lease locking, multi-instance concurrency, or production Service Bus Standard behavior.

## Emulator configuration and startup validation

The emulator requires the fixed namespace name `sbemulatorns` and a non-null `UserConfig.Logging` block, as in [Microsoft's emulator configuration](https://learn.microsoft.com/en-us/azure/service-bus-messaging/test-locally-with-service-bus-emulator). This local configuration selects `Console` logging so startup diagnostics appear in Compose logs; the queue remains `orders` with a maximum delivery count of five.

Run the configuration regression tests from the repository root:

```powershell
dotnet test tests/CloudOrders.ArchitectureTests/CloudOrders.ArchitectureTests.csproj --configuration Release --filter FullyQualifiedName~LocalServiceBusConfigurationTests
```

For a broker-only startup check on alternate host ports (without starting Azurite), run:

```powershell
$env:SERVICEBUS_HOST_PORT = '5673'
$env:MSSQL_HOST_PORT = '1434'
docker compose -f local/compose.yml up -d mssql
docker compose -f local/compose.yml up -d --force-recreate --no-deps servicebus
docker compose -f local/compose.yml ps -a servicebus mssql
docker compose -f local/compose.yml logs --no-color servicebus 2>&1 |
    ForEach-Object { $_ -replace 'Password=[^;]*;', 'Password=[REDACTED];' }
```

Allow the emulator to initialize SQL before checking its logs. Require `Emulator Service is Successfully Up!`, no configuration validation/launcher failure, and a container that remains running on a later `ps -a` check. Compose syntax validation and a successful `up -d` exit alone do not prove emulator readiness. Restarting the emulator resets its local broker data; use only this disposable local stack. Raw emulator SQL logs can include the local SQL password, so redact connection strings before sharing or storing evidence. When using alternate host ports, update the local client connection strings to match; container-to-container ports remain unchanged.
