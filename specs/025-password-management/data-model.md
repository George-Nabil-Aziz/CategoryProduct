# Data Model: Password Management

## Schema impact

One new table; created only by `dotnet ef migrations add AddPasswordResetTokens --project CategoryProduct.API` (never hand-written). `Users` is unchanged.

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | `int` identity | no | PK |
| `UserId` | `int` | no | FK to `Users.Id`, cascade delete |
| `TokenHash` | `nvarchar(128)` | no | `TokenService.Hash(raw)` (64 hex chars); never the raw token |
| `CreatedAtUtc` | `datetime2` | no | issue time |
| `ExpiresAtUtc` | `datetime2` | no | `CreatedAtUtc + PasswordReset:TokenMinutes` |
| `UsedAtUtc` | `datetime2` | yes | set when consumed OR invalidated by a newer token or a successful reset |
| `ConcurrencyStamp` | `uniqueidentifier` | no | concurrency token; rotated on use (race guard) |

| Index | Definition |
|---|---|
| `IX_PasswordResetTokens_TokenHash` | unique |
| `IX_PasswordResetTokens_UserId` | non-unique (lookup of a user's unused tokens) |

Entity (`CategoryProduct.API/Models/PasswordResetToken.cs`):

```csharp
public class PasswordResetToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    /// <summary>Marks the token consumed or invalidated and rotates the concurrency stamp.</summary>
    public void MarkUsed(DateTime utcNow) { UsedAtUtc = utcNow; ConcurrencyStamp = Guid.NewGuid(); }
}
```

`OnModelCreating` addition (same style as `RefreshToken`):

```csharp
modelBuilder.Entity<PasswordResetToken>(b =>
{
    b.HasIndex(t => t.TokenHash).IsUnique();
    b.HasIndex(t => t.UserId);
    b.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
    b.Property(t => t.ConcurrencyStamp).IsConcurrencyToken();
    b.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
});
```

`public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }` on `ApplicationDbContext`.

## States of a token

| State | Condition | Usable? |
|---|---|---|
| Active | `UsedAtUtc == null && ExpiresAtUtc > now` | yes |
| Expired | `UsedAtUtc == null && ExpiresAtUtc <= now` | no |
| Used or invalidated | `UsedAtUtc != null` | no |

Transitions: issue (Active); use (Active to Used, with a password change); a newer issue for the same user or a successful reset (other Active to Used, no password change); time (Active to Expired). Rows are deleted only by the cleanup after retention.

## Cleanup

`RefreshTokenCleanupService.PurgeAsync(utcNow)`: delete `PasswordResetTokens` where `ExpiresAtUtc < cutoff || (UsedAtUtc != null && UsedAtUtc < cutoff)`, `cutoff = utcNow - RetentionDays` (default 7; `RetentionDays <= 0` falls back to 7, as for refresh tokens). The method returns the total removed (refresh plus reset tokens).

## Non-persistent data

| Item | Where | Notes |
|---|---|---|
| Raw reset token | memory, then the email body | never stored, never logged |
| Emails | `EmailQueue` (024) | in memory; lost on restart (024 Known Gap) |
| Passwords | request bodies only | hashed with `PasswordHasher<User>`; never logged |

## Existing tables touched at run time (no schema change)

- `Users.PasswordHash` (rewritten by change and reset).
- `RefreshTokens.RevokedAtUtc` and `ConcurrencyStamp` (revoke all); one new row on change-password (new family).
