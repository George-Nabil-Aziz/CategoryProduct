# Contract: email on register, profile endpoints, MVC pages

## API

### POST api/account/register (changed)

Request: `{ "userName": "...", "password": "...", "email": "a@b.co" }`; `email` optional (absent, null, empty, whitespace = none).

| Status | When |
|---|---|
| 201 | created; body `UserResponseDto(id, userName, role, email)`; `email` is the normalized value or null |
| 400 | `ValidationProblem`; `Email` key for an invalid address (message `Email is not a valid address.`); existing rules for the other fields |
| 409 | `Username already taken.` (existing) or `Email already in use.` (new) |
| 429 | existing `register` policy |

Order of checks: model binding errors, email format, username taken, email taken. Race on either unique index: `DbUpdateException` maps to the matching 409 (re-query to tell which, else `Username already taken.`).

### GET api/account/me (new)

`[Authorize]`. Response 200 `ProfileResponseDto(int Id, string UserName, string Role, string? Email)` for the user in the JWT (`ClaimTypes.NameIdentifier`). 401 anonymous, or token for a deleted user.

### PUT api/account/email (new)

`[Authorize]`, `[EnableRateLimiting("profile")]`. Request `UpdateEmailRequestDto(string? Email)`.

| Status | When |
|---|---|
| 200 | updated (or unchanged); body `ProfileResponseDto`; null/empty/whitespace clears the email |
| 400 | invalid format (`Email` key) |
| 401 | anonymous or deleted user |
| 409 | `Email already in use.` by another user (case-insensitive) |
| 429 | profile limit |

The user id always comes from the JWT.

### GET api/users, PUT api/users/{id}/role (changed, Admin only)

`UserResponseDto` gains `email`. Authorization and status codes unchanged.

### Unchanged on purpose

Orders, order lists, stock movements, cart and login responses contain no email.

## MVC

### Register (changed)

`RegisterViewModel.Email` (optional; `[EmailAddress]`, `[StringLength(254)]`, display `Email (optional)`). `Register.cshtml` gets an Email input between User name and Password (`autocomplete="email"`, `type="email"`). `RegisterAsync(userName, password, email)` sends `email` only when non-empty. Error mapping:

- 409 with `Conflict == "Email already in use."` -> field `Email`; any other 409 -> `UserName` "Username already taken." (existing).
- 400 `Errors` key `Email` -> field `Email` (the existing mapping only keeps `UserName`/`Password`; `Email` is added).

### Profile (new) `/Account/Profile`

- `[Authorize]` (anonymous -> login redirect via the cookie scheme, as other pages).
- GET: calls `GET api/account/me`; shows form with the current email (empty when none) and the user name and role as text. 401 -> sign out and redirect to login with the return URL; 429 -> `ApiRateLimit.Message`; other failure -> the existing 503/Error handling (exception filter).
- POST (`[ValidateAntiForgeryToken]`): `ProfileViewModel.Email` (optional) -> `PUT api/account/email`. Success -> `TempData["Success"] = "Email saved."` (or `"Email removed."` when cleared) and redirect to GET. 400/409 -> re-render with the error on the `Email` field. 401 -> as above. 429 -> `ApiRateLimit.Message` in the validation summary.
- Layout: a "Profile" link (next to "Hello, name") for signed-in users.
- The page only ever shows the signed-in user's own address.

### Users list (changed, Admin)

`UserItem.Email`; `Views/Users/Index.cshtml` gets an Email column (empty cell when null). No new action.

## Test hooks

- API: `UserEmailTests` cover register (valid, absent, empty, invalid variants, duplicate with different case), me, put (set, change, same, clear, invalid, duplicate), visibility (users list for Admin includes email, plain user 403, orders/movements/login JSON have no `email`), 401s.
- MVC: `FakeApi` scripts `api/account/register` (201/400/409), `api/account/me`, `api/account/email`; assertions on the request JSON and the HTML.
