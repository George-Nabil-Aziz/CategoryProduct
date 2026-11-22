# Contract: reset and notice emails

Plain text (UTF-8), built with `StringBuilder`, `InvariantCulture`, `\n` line breaks, UTC dates `yyyy-MM-dd HH:mm 'UTC'` (the same style as the 024 order mails). One message per recipient, to the user's own stored `Email`. Sent through `EmailQueue.TryEnqueue`; failures are logged and swallowed. No HTML.

## NotificationService additions

```csharp
public Task PasswordResetRequestedAsync(User user, string link, int validMinutes);
public Task PasswordChangedAsync(User user);
```

Both: `try { ... } catch (Exception ex) { log Warning (no address, no token, no link) }`; return a completed task even on failure; do nothing when `Email:Enabled` is false or `user.Email` is null or empty.

## Reset requested

Subject: `Reset your password`

```
Hello {UserName},

We received a request to reset the password of your account.
Open this link to choose a new password (valid for {validMinutes} minutes, one use):

{link}

If you did not ask for this, ignore this email: your password stays as it is.
```

`{link}` = `<App:PublicMvcBaseUrl trimmed of trailing '/'>/Account/ResetPassword?token=<Uri.EscapeDataString(raw token)>`. The raw token is Base64Url, so escaping leaves it unchanged; it is applied anyway. The link is on its own line so mail clients link it fully. `{UserName}` has CR and LF removed (it is validated to `[A-Za-z0-9._-]` by registration, so this is defense in depth). The subject has no user text.

## Password changed

Subject: `Your password was changed`

```
Hello {UserName},

The password of your account was changed on {nowUtc}.
All other sessions were signed out.

If you did not do this, reset your password immediately using "Forgot password" on the login page, and contact an administrator.
```

Sent after a successful change-password and after a successful reset-password. It contains no password, no token and no link. (`All other sessions were signed out.` is accurate for refresh tokens: access tokens already issued live until `Jwt:ExpiryMinutes`; the Known Gap records it.)

## Rules

- The body of the reset mail is the only place the raw token exists outside memory; tests assert that the stored `TokenHash` equals `TokenService.Hash(<token parsed from the mail>)` and differs from the token.
- Neither mail is queued for a user without an email, nor when `Email:Enabled` is false.
- Both messages must pass `EmailMessage.Validate()` (subject under 200 characters, no line breaks, non-empty body).
