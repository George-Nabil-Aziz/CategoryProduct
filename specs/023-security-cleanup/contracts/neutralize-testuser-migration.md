# Contract: migration NeutralizeSeededTestUser

## Creation

```
dotnet ef migrations add NeutralizeSeededTestUser --project CategoryProduct.API
```

Generates `<timestamp>_NeutralizeSeededTestUser.cs` and `.Designer.cs` (the timestamp is later than `20261122120100_AddCartAndOrders`). Expected generated `Up`/`Down`: empty. The snapshot must come out unchanged (verify with `git diff`). Existing migrations are not edited.

## Up (the only addition)

One `migrationBuilder.Sql` call:

```sql
UPDATE rt SET RevokedAtUtc = SYSUTCDATETIME(), ConcurrencyStamp = NEWID()
FROM RefreshTokens rt INNER JOIN Users u ON u.Id = rt.UserId
WHERE u.UserName = 'testuser'
  AND u.PasswordHash = 'AQAAAAIAAYagAAAAENKkunRHnzjDucj641tHfwyMEdkA6lL7wKrwYWVP4LKRA2L9/9W536fomTkCKhKHSw=='
  AND rt.RevokedAtUtc IS NULL;

UPDATE Users SET PasswordHash = '/w==', Role = 'User'
WHERE UserName = 'testuser'
  AND PasswordHash = 'AQAAAAIAAYagAAAAENKkunRHnzjDucj641tHfwyMEdkA6lL7wKrwYWVP4LKRA2L9/9W536fomTkCKhKHSw==';
```

The hash literal is the one in `SeedTestUserForAuth` (copy it from that file; do not retype). Order matters: tokens first.

## Down

Empty, with a comment: restoring the known password would re-create the vulnerability.

## Guarantees

| Case | Result |
|---|---|
| seeded hash, any role | marker, `User`, tokens revoked |
| different hash | untouched |
| no `testuser` | no-op |
| user has orders/cart/movements | succeeds (update only) |
| run twice | second run matches nothing |
| `PasswordHasher<User>.VerifyHashedPassword(user, "/w==", anything)` | `Failed` (login 401) |

## Tests

| Where | Case |
|---|---|
| `SqlServerTests/SeededTestUserNeutralizationSqlServerTests` | migrate to `20261122120100_AddCartAndOrders` via `IMigrator`; assert seeded state (Admin, seeded hash); insert a refresh token (unrevoked) and an order for the user; `MigrateAsync()`; assert marker, `User`, token revoked, order intact |
| same | after migrating to the previous migration set a different hash and role Admin; migrate; row and token unchanged |
| same | delete `testuser` before migrating; migrate succeeds |
| same | login via `SqlServerApiFactory` client for `testuser` returns 401 |
| existing `MigrationTests` | full migrate and no pending model changes still pass |
| `API.Tests/SeededTestUserMarkerTests` | marker returns `Failed` and does not throw; the seeded hash constant still verifies as `Failed` for a wrong password |
