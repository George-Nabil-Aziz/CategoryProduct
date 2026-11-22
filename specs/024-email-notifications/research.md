# Research: Email Notifications

All statements about existing code were checked by reading it at the commit after `09a7c3b` (023-security-cleanup). Statements marked "to verify" must be confirmed during implementation.

## R1. What exists today

- `CategoryProduct.API/Models/User.cs`: `Id`, `UserName`, `PasswordHash`, `Role` only. No email.
- `RegisterRequestDto(UserName, Password)`; `AccountController.Register` checks the name case-insensitively with `ToLower()`, catches `DbUpdateException` for the unique `UserName` index and returns 409 `ProblemDetails` "Username already taken.".
- `UserResponseDto(Id, UserName, Role)` is returned by register (201), `GET api/users` (Admin) and `PUT api/users/{id}/role` (Admin).
- `AccountController` (route `api/[controller]`, lower-case URLs) has `[EnableRateLimiting]` on `register`, `login`, `refresh`, `logout` (policies registered in `Program.cs`, limits from `RateLimiting:*` config, `ApiFactory` raises them to 1000).
- `ProductImageStorage` resolves `ProductImages:StoragePath`: `Path.IsPathRooted(p) ? p : Path.Combine(env.ContentRootPath, p)` then `Path.GetFullPath`; options bound via `Configure<ProductImageOptions>(GetSection(...))`; registered as a singleton. The pickup sender copies this approach. `CategoryProduct.API/App_Data/` is in `.gitignore`.
- `InventoryService`: `Threshold => _options.Effective` (`LowStockThreshold < 0 ? 5 : LowStockThreshold`); `LogIfLowStock(movement)` logs when `QuantityChange < 0 && QuantityAfter < Threshold`. `StockMovement` has `QuantityChange` and `QuantityAfter`, so the quantity before is `QuantityAfter - QuantityChange`. `ApplyMovementAsync` (used by `ProductController.PostStockMovement`) saves and calls `LogIfLowStock`; checkout stages movements, saves once, then calls `LogIfLowStock` per movement. Product quantity changes only through movements (`ProductController` update rejects a changed quantity: "Use stock movements to change quantity.").
- `OrdersController`: `Checkout` fixes up `order.User` with `FindAsync` after the commit; `CancelCoreAsync` reads the order with `Include(Items)` and `Include(User)`, returns it on success; `SetStatus` for Confirmed reads with `Include(Items)`, `Include(User)`. Cancel is reachable by the owner (`POST {id}/cancel`) and by an Admin (`POST {id}/cancel` and `PUT {id}/status` with `Cancelled`, which calls `CancelCoreAsync`).
- `RefreshTokenCleanupWorker : BackgroundService` shows the project's hosted-service style (scope factory, try/catch per iteration, `OperationCanceledException` handling).
- `ApiFactory` (tests): InMemory database, seeds Admin `tester` and User `plainuser`, uses `builder.ConfigureServices` for overrides, virtual properties for per-test settings (`LowStockThreshold`, `SeedAdmin`, permit limits). Helpers: `AuthHelper.LoggedInAsync`, `CartOrderTestHelpers` (checkout, add to cart, create product).
- MVC: `AccountController` Register/Login use `CategoryProductApiClient`; `ApiResult` has `Success`, `Conflict` (409 detail text), `Errors` (400 field map), `TooManyRequests`, `Unauthorized`; `UsersController` shows the session-expired pattern (`SignOutAsync` + redirect to login with return URL); `ApiRateLimit.Message` exists (023). MVC tests use `FakeApi` (`On`, `Single`, `Calls`), `FakeResponse.Json/Status`, `HtmlHelpers`, `PostFormAsync`.
- Compose passes API settings as `Section__Key` environment variables with `${VAR:-default}`; `/data/product-images` is a named volume created and `chown`ed in `CategoryProduct.API/Dockerfile`.

## R2. In-process queue or database outbox

| | In-process `Channel` + `BackgroundService` | DB outbox table + poller |
|---|---|---|
| Survives restart/crash | No | Yes |
| Schema/migration | None | New table, index, migration |
| Moving parts | Channel, worker | Table, poller, locking for multiple instances, retry/backoff, cleanup |
| Request cost | One in-memory write | One extra insert in the business transaction |
| Test determinism | Easy (idle signal, recording sender) | Needs polling/time control |
| Fit today | Notifications are informational (the order already exists in the database and UI); no real SMTP exists | Worth it when mails carry obligations (password reset links, invoices) |

**Decision**: in-process channel. The mails are informational, the default transport is a local folder, and the constitution (III) rejects speculative machinery. The loss window (restart, crash, 5 s drain limit) is a documented Known Gap. 025 (password reset) can keep using `EmailQueue`: a lost reset mail only makes the user ask again; if that proves unacceptable the queue can be backed by an outbox without changing `IEmailSender` or `NotificationService`.

**Bounded**: capacity 1000 with a non-blocking `TryWrite`, so a stuck sender (for example an unreachable SMTP host) cannot grow memory without limit and the request path never waits.

## R3. Sender abstraction and providers

- `IEmailSender.SendAsync(EmailMessage, CancellationToken)` takes a generic message (`To`, `Subject`, `Body`): nothing about orders or users, so 025 can reuse it. `From` comes from options, not from the message, so callers cannot spoof it.
- Name collision check: the project does not reference ASP.NET Core Identity UI, so `Microsoft.AspNetCore.Identity.UI.Services.IEmailSender` is not in scope (only `Microsoft.AspNetCore.Identity.PasswordHasher` is used). Our interface lives in `CategoryProduct.API.Services`. To verify: the build has no ambiguity.
- **Pickup** (default): `.eml` rather than `.txt` so mail clients open it; the content is still readable text. Headers `From`, `To`, `Subject`, `Date` (RFC 1123), `Message-ID` (`<guid@categoryproduct.local>`), `MIME-Version: 1.0`, `Content-Type: text/plain; charset=utf-8`, `Content-Transfer-Encoding: 8bit`, blank line, body. CRLF line endings. `FileMode.CreateNew` so a clash can never overwrite. Subject is ASCII by construction except a product name in the low-stock subject; non-ASCII is RFC 2047 encoded (`=?utf-8?B?...?=`) by the writer when any character is above 0x7F.
- **Smtp** (optional): `System.Net.Mail.SmtpClient` + `MailMessage` (`IsBodyHtml = false`, `SubjectEncoding`/`BodyEncoding` UTF-8). Microsoft marks `SmtpClient` as not recommended for new development (no compile-time obsolescence). MailKit would add a NuGet dependency; the request prefers none. Because the feature is optional and behind the interface, replacing it later is a one-class change.
- **Selection** by `Email:Provider` (case-insensitive `Pickup` or `Smtp`) in a DI factory evaluated once. Unknown value, or Smtp with an empty host: log Error, use Pickup (fail safe, like the 023 bootstrap). Credentials are used only if `UserName` is non-empty (some relays are anonymous).
- **No secrets**: `Email:Smtp:UserName`/`Password` appear nowhere in tracked config except as empty strings or commented placeholders; dev uses user secrets (`CategoryProduct.API` has a `UserSecretsId`); compose uses `${EMAIL_SMTP_PASSWORD:-}`. The sender never puts the password in a log or exception message (it catches and rethrows with the exception type and message of the SMTP library only, to verify that library messages do not echo credentials: they do not, they report server responses).

## R4. Configuration binding of empty values (to verify)

Compose passes `Email__Enabled: ${EMAIL_ENABLED:-}`, which yields an empty string. The .NET configuration binder treats an empty string for a non-string property (bool, int) as "no value" and leaves the default (to verify in `EmailOptionsTests`; if it throws on this SDK, bind with `GetValue<bool?>` and defaults instead of the generic `Bind`). Production default is therefore `Enabled=false` (from `appsettings.json`), consistent with 023's empty bootstrap passthrough.

## R5. Email address validation

`System.ComponentModel.DataAnnotations.EmailAddressAttribute` accepts `a@b` and some odd inputs and is not a safety check. `MailAddress.TryCreate` accepts display names (`A <a@b.c>`), comments, quoted local parts and surrounding whitespace. The helper therefore: trims; rejects empty (means none), length over 254 (RFC 5321 path limit), any whitespace or control character (blocks CR/LF header injection before anything else sees the value), then requires `MailAddress.TryCreate(value, out a)` with `a.Address == value` (rejects display-name form and normalization differences) and a dot in the host part. Lower-cases invariant. No DNS lookup, no verification.

## R6. Uniqueness when present

SQL Server treats NULLs in a unique index as equal values, so a plain unique index would allow only one user without an email. A filtered unique index `[Email] IS NOT NULL` fixes it; EF Core's SQL Server provider already adds this filter for a unique index on a nullable column, but `HasFilter("[Email] IS NOT NULL")` is declared explicitly so the snapshot and migration state it (to verify in the generated migration). The InMemory provider does not enforce unique indexes (the API tests use it), so the controllers query first (`AnyAsync(u => u.Email == normalized && u.Id != userId)`) and also catch `DbUpdateException` for the race, as register does for `UserName`. The SQL Server test (CI) proves the index.

## R7. Visibility of emails

- Own email: register 201, `GET api/account/me`, `PUT api/account/email` responses.
- All emails: only `GET api/users` and `PUT api/users/{id}/role` (both `[Authorize(Roles = "Admin")]`).
- Nowhere else: `OrderResponseDto`/`OrderSummaryDto` carry `UserName` only; stock movement DTOs carry `UserName`; login response has none. Tests assert the absence of an `email` property in orders and movements.
- Logs: only a masked form (`a***@example.com`); never the body.

## R8. Low-stock crossing

Rule: `QuantityChange < 0 && QuantityAfter < T && QuantityAfter - QuantityChange >= T`, `T = InventoryOptions.Effective`.

- Matches the existing definition of low ("below the threshold": `LogIfLowStock`, `GET api/product/low-stock` use `<`). The request wrote "to/below"; using "at or below" would contradict the existing UI flag, so the existing rule is kept and the difference recorded in Assumptions.
- No state needed: the movement carries before and after, so a product that stays low produces no further mail, a restock above T re-arms it, and concurrent movements are each judged on their own numbers.
- `T = 0` can never satisfy `QuantityAfter < 0`, so "disabled" needs no special case.
- Checkout can cross several products at once: one digest mail per Admin lists them (limits mail count). Separate messages per Admin avoid exposing Admin addresses to each other.
- Cancel/restock and positive adjustments never trigger (change > 0).
- The Admin list is queried only when at least one crossing exists, so ordinary traffic adds no query.

## R9. Where to call, and exception safety

- Checkout: after `CommitAsync`, after the `FindAsync` user fix-up, before `CreatedAtAction`. The retry loop returns inside the successful path, so the call runs once.
- Cancel: `Cancel` action and the Cancelled branch of `SetStatus` both go through `ToActionResult(CancelResult)`; the call is added where the result is `Ok` (a small private method used by both, `await NotifyIfCancelledAsync(result)`), using `result.Order` (already has `User` and `Items`).
- Confirm: inside the `SaveChangesAsync` success path of `SetStatus`, before `Ok`.
- Stock movement: `ProductController.PostStockMovement` after the status switch (Ok path), with `result.Movement`; its `Product` navigation is set by `StageMovementAsync` (`Product = product`). Checkout uses its `movements` list, whose `Product` is also set.
- `NotificationService` public methods never throw (catch-all, Warning log), including cancellation of the request token (not passed; the work is short and in-process).

## R10. Test determinism

- The real queue and worker run in `ApiFactory` (the host starts hosted services in `WebApplicationFactory`), with a recording `IEmailSender` registered in place of the configured one. The recorder exposes `Messages` (snapshot list) and `WaitForCountAsync(n, timeout)` built on a `SemaphoreSlim`/`TaskCompletionSource`.
- Positive assertions: await the recorder for the expected count with a generous timeout (fails fast on success, bounded on failure).
- Negative assertions ("no email"): after the HTTP call (the enqueue is synchronous before the response), `await queue.WaitForIdleAsync(timeout)` and then assert the recorder is empty. No fixed delays.
- Queue and worker tests construct `EmailQueue` and `EmailDispatchWorker` directly with a throwing/recording sender; `StopAsync` drain tested the same way.
- The pickup sender test uses a temp directory (deleted in `Dispose`), a fake `IHostEnvironment` for the relative-path case, and reads the written file.
- MVC tests do not involve email sending: they assert requests to the fake API (`api/account/register` body, `api/account/me`, `api/account/email`) and rendered HTML.

## R11. Compose and Docker

- Volume `emailoutbox` mounted at `/data/email-outbox`, directory created and `chown`ed to `$APP_UID` in the Dockerfile exactly like `/data/product-images` (a named volume inherits the image directory's ownership on first use).
- `Email__PickupDirectory` is fixed to the mount (like `ProductImages__StoragePath`), the rest are optional passthroughs with empty defaults. Without `EMAIL_ENABLED=true` the stack stays silent; this is stated in the quickstart.
- `compose-smoke` in CI is unaffected (no email settings, feature disabled).

## R12. Rate limiting the profile endpoint

`PUT api/account/email` reveals through 409 whether an address belongs to another user (an enumeration oracle, but only for signed-in users). It is rate limited with a new `profile` policy (30 per minute per IP, `RateLimiting:ProfilePermitLimit`), same shape as the existing four. `register` already carries the existing 5/minute limit and shows the same 409. `GET me` is not limited (cheap, own data).
