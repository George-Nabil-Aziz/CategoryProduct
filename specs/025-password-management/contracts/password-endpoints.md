# Contract: password endpoints (API)

All on `AccountController` (`api/account/...`, JSON). Errors use the existing shapes: `ValidationProblem` (400) and `ProblemDetails` (409). No response ever contains a password, a hash or a reset token.

## DTOs (`CategoryProduct.API/Dtos/PasswordDtos.cs`)

```csharp
public record ChangePasswordRequestDto(
    [Required(AllowEmptyStrings = false)] string CurrentPassword,
    [Required(AllowEmptyStrings = false)] [MinLength(8)] string NewPassword);

public record ForgotPasswordRequestDto(
    [Required(AllowEmptyStrings = false)] [StringLength(254)] string UserNameOrEmail);

public record ResetPasswordRequestDto(
    [Required(AllowEmptyStrings = false)] string Token,
    [Required(AllowEmptyStrings = false)] [MinLength(8)] string NewPassword);
```

The new-password rules are identical to `RegisterRequestDto.Password`.

## POST api/account/change-password

`[Authorize]`, `[EnableRateLimiting("password")]`. User id from `ClaimTypes.NameIdentifier`.

| Status | When | Body |
|---|---|---|
| 200 | password changed | `LoginResponseDto(token, expiresAtUtc, role, refreshToken, refreshTokenExpiresAtUtc)`, a new token family |
| 400 | model rules fail (`NewPassword` missing or shorter than 8, `CurrentPassword` missing) | `ValidationProblem`, keys `NewPassword` / `CurrentPassword` |
| 400 | wrong current password | `ValidationProblem`, key `CurrentPassword`: `Current password is incorrect.` |
| 400 | new equals current | `ValidationProblem`, key `NewPassword`: `New password must differ from the current password.` |
| 401 | no/invalid token, or the user row no longer exists | empty |
| 409 | `DbUpdateConcurrencyException` (a refresh raced the revoke) | `ProblemDetails` `Please try again.` |
| 429 | `password` policy exceeded | empty |

Order: authorize, model binding (400), load user (401), verify current (400), equality (400), set hash, revoke all non-revoked refresh tokens, `IssueAsync(user, Guid.NewGuid())` (one save), `PasswordChangedAsync`. Nothing is changed on any 4xx.

## POST api/account/forgot-password

Anonymous, `[EnableRateLimiting("forgot-password")]`.

| Status | When | Body |
|---|---|---|
| 202 | every valid-shaped request: user exists or not, has email or not, mail sent or not, throttled, email disabled, internal error after validation | empty |
| 400 | `userNameOrEmail` missing, empty, whitespace or longer than 254 | `ValidationProblem` key `UserNameOrEmail` |
| 429 | policy exceeded | empty |

A token and a mail are produced only when ALL hold: a user matches (user name case-insensitive, or normalized email equals `Users.Email`); the user has a non-empty email; `Email:Enabled` is true; no unused token of that user has `CreatedAtUtc > now - PasswordReset:MinIntervalSeconds`. Then: older unused tokens get `MarkUsed(now)`, a new `PasswordResetToken` is added, one save, then `NotificationService.PasswordResetRequestedAsync(user, link, minutes)`.

## POST api/account/reset-password

Anonymous, `[EnableRateLimiting("password")]`.

| Status | When | Body |
|---|---|---|
| 204 | password reset | empty |
| 400 | `NewPassword` fails the rules (token NOT consumed) | `ValidationProblem` key `NewPassword` |
| 400 | token empty, unknown, expired (`ExpiresAtUtc <= now`), used/invalidated, or lost a concurrent race | `ValidationProblem` key `Token`: `The reset link is invalid or has expired.` (one fixed message) |
| 429 | policy exceeded | empty |

On success (one save): `User.PasswordHash` replaced, token `MarkUsed(now)`, the user's other unused tokens `MarkUsed(now)`, all non-revoked refresh tokens `Revoke(now)`. Then `PasswordChangedAsync(user)`. No tokens are issued.

## Logging rules

Never log: passwords, raw tokens, token hashes, full email addresses (use `EmailAddressRules.Mask` if an address must appear). `NotificationService` failures log a Warning without recipient details, as in 024.

## Unchanged

`register`, `login`, `refresh`, `logout`, `me`, `email` keep routes, policies, status codes and bodies. `login` still works with the new password immediately and rejects the old one.
