# Contract: BootstrapAdmin

## Signature

```csharp
namespace CategoryProduct.API.Data;

public enum BootstrapAdminOutcome { NotConfigured, Created, SkippedAdminExists, SkippedUserNameTaken, Rejected, Failed }

public static class BootstrapAdmin
{
    public static Task<BootstrapAdminOutcome> EnsureIfConfiguredAsync(
        IServiceProvider services, IConfiguration configuration, ILogger logger);
}
```

Call site (`CategoryProduct.API/Program.cs`), directly after the migrator and before `app.Run()`:

```csharp
await DatabaseMigrator.MigrateIfEnabledAsync(app.Services, app.Configuration, app.Logger);
await BootstrapAdmin.EnsureIfConfiguredAsync(app.Services, app.Configuration, app.Logger);
```

## Algorithm

```text
name = Bootstrap:AdminUserName ; pwd = Bootstrap:AdminPassword
both blank        -> (FR-011) if Users readable and no Admin: Warning "No Admin user exists..." ; NotConfigured
exactly one blank -> Warning "Both ... are required" ; NotConfigured
name = name.Trim()
rules: name 3-50 chars and ^[A-Za-z0-9._-]+$ ; pwd length >= 8
fail              -> Error "Bootstrap admin rejected: <rule>" (no password) ; Rejected
scope -> ApplicationDbContext
any Role == "Admin"                       -> Information ; SkippedAdminExists
any UserName.ToLower() == name.ToLower()  -> Warning "name taken, not promoted" ; SkippedUserNameTaken
add User { UserName=name, Role="Admin", PasswordHash = new PasswordHasher<User>().HashPassword(user, pwd) }
SaveChangesAsync
  DbUpdateException -> Information (created by another instance) ; SkippedUserNameTaken
  other Exception   -> Error (exception without password) ; Failed
success           -> Information "Bootstrap admin '{UserName}' created." ; Created
```

Never throws. The password is not passed to any logger call, scope or exception message.

## Rules parity with registration

Same as `RegisterRequestDto` (verified in code): username 3-50, `^[A-Za-z0-9._-]+$`, trimmed; password `MinLength(8)`. `Validator.TryValidateObject` is not usable on the record (attributes are on constructor parameters), so the checks are explicit and a parity theory compares both paths.

## Tests (`CategoryProduct.API.Tests/BootstrapAdminTests.cs`)

| Case | Expect |
|---|---|
| valid settings, no Admin (helper, own InMemory db) | `Created`; one Admin; `VerifyHashedPassword` succeeds; hash != password; Information log with name, none contains the password |
| valid settings, Admin exists | `SkippedAdminExists`; user count unchanged |
| no settings / one setting / whitespace | `NotConfigured`; one-of-two logs Warning; no user added |
| password 7 chars | `Rejected`; no user; Error log without the password |
| username `ab`, `bad name`, 51 chars | `Rejected` |
| non-admin with same name different case, no Admin | `SkippedUserNameTaken`; role and hash unchanged |
| duplicate insert race (pre-insert via second context between check and save is impractical): second call after first | `SkippedAdminExists` (idempotent) |
| host: `NoAdminFactory` (`SeedAdmin => false`, settings set) | `POST api/account/login` 200, role `Admin` |
| host: default `ApiFactory` plus settings | no extra user; settings user cannot log in |
| `MigrateFactory` style (`Database:MigrateOnStartup=true`, InMemory) plus settings, no admin | still creates the Admin (migrator skipped for non-relational) |
| parity theory (name, password, valid?) | register 201/400 matches helper `Created`/`Rejected` |
| exception path (disposed provider or throwing context) | `Failed`, no throw |
