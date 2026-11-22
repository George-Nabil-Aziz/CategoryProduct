# Contract: MVC pages and API client

MVC talks to the API only over HTTP (Principle V) with its own view models.

## CategoryProductApiClient additions

```csharp
Task<ApiResult<LoginResult>> ChangePasswordAsync(string currentPassword, string newPassword); // POST api/account/change-password, bearer attached
Task<ApiResult> ForgotPasswordAsync(string userNameOrEmail);                                  // POST api/account/forgot-password (202)
Task<ApiResult> ResetPasswordAsync(string token, string newPassword);                         // POST api/account/reset-password (204)
```

They reuse `SendAsync`/`ToResultAsync`: 429 gives `TooManyRequests`, 400 gives `Errors` (field map), 401 gives `Unauthorized`, 409 gives `Conflict`. JSON property names: `currentPassword`, `newPassword`, `userNameOrEmail`, `token`.

## View models (`Models/PasswordViewModels.cs`)

| Class | Fields and rules |
|---|---|
| `ForgotPasswordViewModel` | `UserNameOrEmail` `[Required][StringLength(254)]`, display "User name or email" |
| `ResetPasswordViewModel` | `Token` `[Required]` (hidden field); `NewPassword` `[Required][MinLength(8)][DataType.Password]`; `ConfirmPassword` `[Required][Compare(NewPassword)]` message `Passwords do not match.` |
| `ChangePasswordViewModel` | `CurrentPassword` `[Required][DataType.Password]`; `NewPassword` `[Required][MinLength(8)]`; `ConfirmNewPassword` `[Required][Compare(NewPassword)]` |

## Actions (MVC `AccountController`)

| Route | Method | Auth | Behavior |
|---|---|---|---|
| `/Account/ForgotPassword` | GET | anonymous | form |
| `/Account/ForgotPassword` | POST | anonymous, antiforgery | invalid model: redisplay. Call API. `TooManyRequests`: model error `ApiRateLimit.Message`. 400 on `UserNameOrEmail`: field error. Anything else: redirect to `ForgotPasswordConfirmation` (uniform). |
| `/Account/ForgotPasswordConfirmation` | GET | anonymous | `If an account with that name or email exists and has an email address, a reset link was sent.` plus a link to Login |
| `/Account/ResetPassword?token=...` | GET | anonymous | sets `Referrer-Policy: no-referrer` and `Cache-Control: no-store`; missing/empty `token`: view with an error and a link to ForgotPassword, no form; otherwise the form with hidden `Token` |
| `/Account/ResetPassword` | POST | anonymous, antiforgery | invalid model: redisplay (token stays hidden). Success: `TempData["Success"] = "Your password was changed. Sign in with your new password."`, redirect to Login. 400 `Token`: invalid-link state (message `This reset link is invalid or has expired.` and a link to ForgotPassword). 400 `NewPassword`: field error. `TooManyRequests`: `ApiRateLimit.Message`. Same headers as GET. |
| `/Account/ChangePassword` | GET | `[Authorize]` | form |
| `/Account/ChangePassword` | POST | `[Authorize]`, antiforgery | Success: `AuthSessionService.SignInAsync(HttpContext, User.Identity.Name, login)` with the returned tokens, `TempData["Success"] = "Password changed."`, redirect to Profile. 400 `CurrentPassword`/`NewPassword`: field errors. `TooManyRequests`: `ApiRateLimit.Message`. `Unauthorized`: sign out and redirect to Login (existing `SessionExpired` pattern, with return URL `/Account/ChangePassword`). |

The MVC never logs or echoes the token (not in `ModelState` errors, not in `ViewData` other than the hidden field value, not in exception messages).

## Views

- `Login.cshtml`: add `<a asp-action="ForgotPassword">Forgot password?</a>` beside Register; show `TempData["Success"]` (alert-success) above the form.
- `Profile.cshtml`: add `<a asp-action="ChangePassword" id="change-password-link">Change password</a>`.
- New: `ForgotPassword.cshtml`, `ForgotPasswordConfirmation.cshtml`, `ResetPassword.cshtml`, `ChangePassword.cshtml`, following `Login.cshtml`/`Profile.cshtml` markup (Bootstrap classes, `asp-validation-summary="ModelOnly"`, `_ValidationScriptsPartial`, `autocomplete="new-password"` on new-password fields and `current-password` on the current one).

## Test hooks (MVC tests)

`FakeApi` scripts `api/account/forgot-password` (202), `reset-password` (204 or 400 `{"errors":{"Token":["..."]}}`), `change-password` (200 login JSON, 400 errors, 429, 401). `ApiJson` gets a helper for the login response if missing. Assertions: request bodies via `RecordedRequest.Json()`, redirect targets via `AssertRedirect`, headers via the response, `HtmlHelpers` for fields and links.
