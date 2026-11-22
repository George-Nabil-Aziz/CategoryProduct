# Data Model: Email Notifications

## Schema impact

One column and one index on `Users`; created only by `dotnet ef migrations add AddUserEmail --project CategoryProduct.API` (never hand-written).

| Table.Column | Type | Null | Notes |
|---|---|---|---|
| `Users.Email` | `nvarchar(254)` | yes | stored trimmed and lower-case; NULL means "no email" |

| Index | Definition |
|---|---|
| `IX_Users_Email` | unique, filter `[Email] IS NOT NULL` (many NULLs allowed, duplicates rejected) |

`OnModelCreating` addition:

```csharp
modelBuilder.Entity<User>(b =>
{
    b.Property(u => u.Email).HasMaxLength(254);
    b.HasIndex(u => u.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
});
```

Existing rows get NULL. No data migration. `db.Database.HasPendingModelChanges()` must be false after the migration. No other table changes: the queue is in memory, pickup files are on disk, nothing about notifications is stored in the database.

## Non-persistent types

| Type | Where | Notes |
|---|---|---|
| `EmailMessage(To, Subject, Body)` | `CategoryProduct.API/Services/EmailMessage.cs` | record; `Validate()` returns an error string or null |
| `IEmailSender` | `Services/IEmailSender.cs` | `Task SendAsync(EmailMessage, CancellationToken)` |
| `EmailOptions`, `SmtpOptions` | `Services/EmailOptions.cs` | section `Email` |
| `EmailQueue` | `Services/EmailQueue.cs` | singleton; bounded channel (1000); `TryEnqueue`, `WaitForIdleAsync`, reader for the worker |
| `EmailDispatchWorker` | `Services/EmailDispatchWorker.cs` | `BackgroundService`; reads the queue, calls the sender |
| `NotificationService` | `Services/NotificationService.cs` | scoped; `OrderPlacedAsync`, `OrderStatusChangedAsync`, `LowStockAsync` |
| `EmailAddressRules` | `Services/EmailAddressRules.cs` | static; `TryNormalize` |
| `UpdateEmailRequestDto(string? Email)`, `ProfileResponseDto(int Id, string UserName, string Role, string? Email)` | `Dtos/ProfileDtos.cs` | new |

## Changed DTOs

| DTO | Change |
|---|---|
| `RegisterRequestDto` | `+ string? Email = null` (trailing, optional) |
| `UserResponseDto` | `+ string? Email = null` (trailing, optional) |
| MVC `UserItem` | `+ string? Email = null` |
| MVC `RegisterViewModel` | `+ string? Email` (optional, `[EmailAddress]`, `[StringLength(254)]`, display "Email (optional)") |
| MVC `ProfileViewModel` (new) | `string? Email` |

## Production code touched

| File | Behavior impact |
|---|---|
| `Models/User.cs`, `Data/ApplicationDbContext.cs`, migration | new nullable column + filtered unique index |
| `Controllers/AccountController.cs` | register accepts email; new `me`, `email` endpoints |
| `Controllers/UsersController.cs` | responses carry `email` (Admin only) |
| `Controllers/OrdersController.cs`, `ProductController.cs` | enqueue notifications after successful commits |
| `Program.cs` | options, sender factory, queue, worker, `profile` rate-limit policy |
| `appsettings*.json`, compose, Dockerfile, `.env.example` | configuration only |
| MVC files per plan | register field, profile page, Users column, nav link |

## Foreign keys

Unchanged. `Orders.UserId` Restrict, `StockMovements.UserId` SetNull, `CartItems.UserId` and `RefreshTokens.UserId` Cascade. Deleting a user (no endpoint exists) would delete the address with the row.

## Test infrastructure touched

| Type | File | Change |
|---|---|---|
| `ApiFactory` | `CategoryProduct.API.Tests/ApiFactory.cs` | `Email:Enabled=true`, replace `IEmailSender` with `RecordingEmailSender`, expose it, `RateLimiting:ProfilePermitLimit=1000` |
| `RecordingEmailSender` | new, same project | thread-safe list, `WaitForCountAsync`, optional `ThrowOnSend` |
| `SqlServerApiFactory` | `CategoryProduct.API.SqlServerTests` | reused unchanged |
| `FakeApi`, `FakeResponse`, `HtmlHelpers`, `MvcTestBase` | `CategoryProduct.MVC.Tests/Support` | reused unchanged |
