# Quickstart: Validating Health Checks

## Automated

```
dotnet test
```

Relevant test: `CategoryProduct.API.Tests/HealthTests.cs`
(`Health_ReturnsHealthyWithDatabaseCheck`). It uses `ApiFactory` with the EF Core in-memory
provider.

## Manual (SQL Server available, API running)

1. Start the API (`dotnet run --project CategoryProduct.API`).
2. `GET /health` with no `Authorization` header -> `200`, JSON with `status: "Healthy"` and a
   `database` check.
3. Stop SQL Server (or break the connection string) and call again -> status is not `Healthy`
   (`503` expected by framework default). This step was not run when documenting.
4. Confirm the log output shows no Information-level request line for successful `/health` calls,
   while other routes (for example `GET /api/Category`) still log at Information.
