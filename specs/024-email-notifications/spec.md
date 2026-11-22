# Feature Specification: Email Notifications (user email, order and low-stock mails)

**Feature Branch**: `024-email-notifications`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Add email notifications. Users get an optional email address (register + a profile page). The API sends plain-text emails when an order is placed, when an order is Confirmed or Cancelled (including a user cancelling), and to all Admins when a stock movement takes a product below the low-stock threshold. No real SMTP credentials anywhere: an `IEmailSender` abstraction whose default implementation writes each email as a file to a pickup folder, plus an optional SMTP implementation configured only from config/environment. Sending must never block or fail a request. Keep the sender generic so a later password-reset feature (025) can reuse it."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A user can have an email address (Priority: P1)

A person can give an email address when registering (optional) and, once signed in, view, set, change or clear it on a profile page. The address is validated, unique among users when present, and visible to nobody except its owner and (in the Users list) Admins.

**Why this priority**: every notification needs a recipient; today `User` has only `Id`, `UserName`, `PasswordHash`, `Role` (verified in `CategoryProduct.API/Models/User.cs`) and `RegisterRequestDto` has only `UserName` and `Password`.

**Independent Test**: new API tests (`UserEmailTests`) and MVC tests (register additions in `AccountTests`, new `ProfileTests`); no email sending needed.

**Acceptance Scenarios**:

1. **Given** `POST api/account/register` with a valid `email`, **Then** 201, the response carries `email` (normalized: trimmed, lower-case), and the stored user has that email.
2. **Given** `POST api/account/register` without `email`, with `email: null`, `""` or whitespace, **Then** 201 and the stored email is NULL (all existing register tests and clients keep working).
3. **Given** an invalid email (no `@`, no dot in the domain, display-name form `A <a@b.c>`, longer than 254 characters, containing CR, LF or a space), **Then** 400 `ValidationProblem` with an `Email` entry; no user is created.
4. **Given** an email already used by another user (compared case-insensitively), **Then** 409 with detail `Email already in use.`; no user is created. Same for `PUT api/account/email`.
5. **Given** a signed-in user, **When** `GET api/account/me`, **Then** 200 `{ id, userName, role, email }` for that user only; anonymous gets 401.
6. **Given** a signed-in user, **When** `PUT api/account/email` with `{ "email": "a@b.co" }`, **Then** 200 with the updated profile; with `""`/null the email is cleared (200, `email: null`); setting the email the user already has is 200 (no conflict with itself).
7. **Given** two users, **Then** no endpoint other than `GET api/users` (Admin only) and `PUT api/users/{id}/role` (Admin only) returns a user's email, apart from the caller's own register/me/email responses; orders, stock movements, carts and the login response contain no email.
8. **Given** an Admin, **When** `GET api/users`, **Then** each item has `email` (or null); a non-admin gets 403 as today.
9. **Given** the MVC register form, **Then** it has an optional Email field; a valid value is sent to the API, an empty one is omitted/null, an API 400 or 409 for email is shown next to the Email field.
10. **Given** a signed-in MVC user, **When** opening `/Account/Profile`, **Then** the current email is shown in a form; saving a valid value shows a success message; an invalid or taken value re-renders with the error; an expired session redirects to login; anonymous is redirected to login.
11. **Given** the MVC Users page as Admin, **Then** an Email column shows each user's email or an empty cell.

---

### User Story 2 - Emails are sent safely, off the request path (Priority: P1)

The API has a generic `IEmailSender` (transport) and an in-process queue drained by a background worker. Feature code only enqueues; the request never waits for a send and never fails because of one. By default each email becomes a `.eml` file in a pickup folder and a log line; setting `Email:Provider=Smtp` sends through SMTP instead, configured from config/environment only.

**Why this priority**: all notification stories depend on it, and the safety properties (no secrets, never blocks, never fails a request) are the core of the feature. It is also the part reused by 025.

**Independent Test**: `PickupDirectoryEmailSenderTests` (temp dir), `EmailQueueTests` and `EmailDispatchWorkerTests` (queue/worker directly with a recording sender), `EmailOptionsTests` (provider selection and fallbacks), all without network.

**Acceptance Scenarios**:

1. **Given** `Email:Enabled=true`, Provider `Pickup` and a pickup directory, **When** a message is sent through the pickup sender, **Then** one `.eml` file appears in the directory (created if missing) with `From`, `To`, `Subject`, `Date`, `Message-ID`, `MIME-Version`, `Content-Type: text/plain; charset=utf-8` headers and the body; the file name is unique per message; an Information log names the file but not the body.
2. **Given** a relative `Email:PickupDirectory`, **Then** it resolves against `ContentRootPath` (like `ProductImages:StoragePath` in `ProductImageStorage`); an absolute path is used as is.
3. **Given** a subject or recipient containing CR or LF, **Then** the message is rejected (never written, never sent) and a Warning is logged; no header injection is possible.
4. **Given** `Email:Enabled=false` (or unset in production), **When** feature code enqueues, **Then** nothing is queued, nothing is written, and the request behaves exactly as before.
5. **Given** the sender throws (disk full, SMTP down), **Then** the worker logs an Error (recipient masked, no body, no password), drops that message, and keeps processing later ones; the HTTP request that enqueued it already succeeded.
6. **Given** the queue is full (capacity 1000), **Then** the enqueue returns false immediately, a Warning is logged and the request is unaffected.
7. **Given** `Email:Provider=Smtp` with `Email:Smtp:Host`, `Port`, `UserName`, `Password`, `EnableSsl` from configuration, **Then** the SMTP sender is used; **given** Smtp is selected but `Host` is empty, or the provider name is unknown, **Then** the API still starts, logs an Error and uses the Pickup sender (fail safe, no outage because of a typo).
8. **Given** the host stops, **Then** the worker tries to drain already queued mails for a short bounded time; whatever remains is lost (documented Known Gap).
9. **Given** any tracked file, **Then** none contains an SMTP user, password or host: `appsettings*.json`, `.env.example`, compose and the specs hold placeholders or empty values only.
10. **Given** the compose file, **Then** `Email__*` settings are optional passthroughs with empty defaults, and the pickup folder is a named volume (`/data/email-outbox`).

---

### User Story 3 - Order emails (Priority: P1)

The user who owns an order gets a plain-text email when the order is placed and when it becomes Confirmed or Cancelled (cancelled by the user, by an Admin through `POST api/orders/{id}/cancel`, or by an Admin through `PUT api/orders/{id}/status`). Nothing is sent if the user has no email.

**Why this priority**: the main user-visible value.

**Independent Test**: `OrderEmailTests` in `CategoryProduct.API.Tests` with the recording sender.

**Acceptance Scenarios**:

1. **Given** a user with an email and a non-empty cart, **When** checkout succeeds (201), **Then** exactly one email is queued to that address with subject `Order #<id> placed`, and a body containing the order id, status Pending, every item (name, quantity, unit price, line total) and the total.
2. **Given** a user without an email, **When** checkout succeeds, **Then** no email is queued and the response is unchanged.
3. **Given** checkout fails (409 insufficient stock, empty cart, 401), **Then** no email is queued.
4. **Given** an Admin confirms a Pending order of a user with an email, **Then** one email `Order #<id> confirmed` goes to the order's owner (not to the Admin).
5. **Given** a user cancels their own Pending order, **Then** one email `Order #<id> cancelled` goes to that user; an Admin cancelling it through either endpoint sends the same email to the owner.
6. **Given** a status change that is rejected (409 not Pending, 404, invalid status 400), **Then** no email is queued.
7. **Given** the owner has no email, **Then** confirm and cancel queue nothing.
8. **Given** the sender fails or the queue rejects the mail, **Then** the order endpoints still return their normal success status.
9. **Given** product names with newline characters or markup, **Then** the body is plain text (not HTML) and the headers are untouched (the subject contains only the order id).

---

### User Story 4 - Low-stock alert to Admins (Priority: P2)

When a stock movement takes a product from at or above the threshold to below it, every Admin with an email gets one email. A product that stays below the threshold does not generate more emails.

**Why this priority**: useful operations signal, but only for Admins with an address.

**Independent Test**: `LowStockEmailTests` in `CategoryProduct.API.Tests`.

**Acceptance Scenarios**:

1. **Given** threshold 5 (`Inventory:LowStockThreshold`), product quantity 6 and two Admins with emails and one without, **When** a movement of -2 is posted (`POST api/product/{id}/stock-movements`) leaving 4, **Then** two emails are queued, one per Admin with an email (separate messages, so Admin addresses are not shown to each other), subject `Low stock: <product name>`, body with product id, name, quantity now, threshold.
2. **Given** quantity 4 (already below), **When** another -1 is posted (leaving 3), **Then** no email is queued.
3. **Given** a movement that leaves the quantity exactly at the threshold (5 with threshold 5), **Then** no email is queued (the existing rule is "below the threshold", see Assumptions).
4. **Given** an increase, or a decrease that stays at or above the threshold, **Then** no email is queued.
5. **Given** threshold 0 (disabled), **Then** no email is ever queued.
6. **Given** a checkout that takes several products below the threshold, **Then** each Admin gets ONE email listing all products that crossed in that checkout.
7. **Given** a product that crosses again after being restocked above the threshold and then sold down, **Then** a new email is queued (the crossing is evaluated per movement from `QuantityAfter - QuantityChange`).
8. **Given** no Admin has an email, **Then** nothing is queued and no error occurs.
9. **Given** an order cancel restocks products, **Then** no low-stock email is sent (increases never cross downward).

---

### User Story 5 - Known Gaps and final verification (Priority: P3)

The spec's Known Gaps carry the final facts (test counts, CI run, what stays open) and the build and test gates pass.

**Independent Test**: `dotnet build CategoryProduct.slnx`, `dotnet build CategoryProduct.API.SqlServerTests`, `dotnet test CategoryProduct.slnx` three times.

**Acceptance Scenarios**:

1. **Given** the finished feature, **Then** both builds have 0 errors and the full test run passes three consecutive times with more tests than the 023 baseline of 560 (314 MVC, 246 API).
2. **Given** `specs/024-email-notifications/spec.md`, **Then** Known Gaps lists the open items (lost queued mails on restart, no retry, no verification, SQL Server index test only on CI) with final facts.

---

### Edge Cases

- Email uniqueness is enforced by a filtered unique index on SQL Server (`[Email] IS NOT NULL`) but NOT by the InMemory provider used by the API tests, so the controllers also check with a query and catch `DbUpdateException` (the same two-layer pattern as `AccountController.Register` for `UserName`).
- Email comparison: stored lower-case and trimmed (invariant), so `A@B.co` and `a@b.co` collide.
- An Admin with an email receives low-stock mails; a user demoted from Admin stops receiving them (the Admin list is read at event time).
- The recipient is read at event time: an email changed between checkout and confirmation gets the confirmation at the new address.
- No email verification: anybody can enter another person's address, so that person could receive unwanted mails. The mails contain no secret and no link; this is a Known Gap. Feature 025 (password reset) must not trust an unverified address blindly; its own spec decides.
- Low-stock crossing is computed from the movement (`QuantityAfter - QuantityChange` against `QuantityAfter`), so it needs no new column and no state; concurrent movements each evaluate their own movement.
- Checkout retries (the `DbUpdateConcurrencyException` loop) send at most one email because the notification is raised once, after the successful commit.
- `Email:Enabled` unset: the code default is `false` (production must opt in); `appsettings.Development.json` sets `true` with Pickup so developers see files.
- Pickup files hold personal data (addresses, order contents). The folder is not served, is ignored by git when under `App_Data/` (already in `.gitignore`), and has no retention in this feature.
- MVC never reads another user's email; the Users page only renders what the API gives an Admin.
- The profile endpoints use the id from the JWT (`ClaimTypes.NameIdentifier`), never a route id, so a user cannot edit another user's email.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `User` MUST gain `public string? Email { get; set; }`; the schema change MUST be a migration created with `dotnet ef migrations add AddUserEmail --project CategoryProduct.API` (column `nvarchar(254) NULL`, unique index `IX_Users_Email` with filter `[Email] IS NOT NULL`), committed with the model change. No existing migration is edited.
- **FR-002**: A static `EmailAddressRules` helper (API, `Services`) MUST normalize and validate: trim; null/empty/whitespace means "no email"; max 254 chars; no whitespace or control characters; parsed by `System.Net.Mail.MailAddress` with `Address` equal to the input and a domain containing a dot; result lower-cased invariant. It returns an error message instead of throwing.
- **FR-003**: `RegisterRequestDto` MUST gain an optional trailing `string? Email = null` (existing callers unaffected). `AccountController.Register` MUST validate it with FR-002 (400 `ValidationProblem`, key `Email`), reject a duplicate (409 `Email already in use.`), store it, and return it in `UserResponseDto`.
- **FR-004**: `UserResponseDto` MUST gain `string? Email = null`. It is filled for the register response (the caller's own), for `GET api/users` and for `PUT api/users/{id}/role` (Admin-only endpoints).
- **FR-005**: `AccountController` MUST gain `GET api/account/me` and `PUT api/account/email` (both `[Authorize]`, user id from the JWT; a missing user row returns 401). Request `UpdateEmailRequestDto(string? Email)`; response `ProfileResponseDto(int Id, string UserName, string Role, string? Email)`. Errors as FR-003. `PUT` is rate limited by a new policy `profile` (`RateLimiting:ProfilePermitLimit`, default 30 per minute per IP, same pattern as `logout`/`refresh`).
- **FR-006**: The API MUST define `IEmailSender` (`Task SendAsync(EmailMessage message, CancellationToken ct)`) and `EmailMessage(string To, string Subject, string Body)`; generic, with no order or user types (reusable by 025).
- **FR-007**: `EmailOptions` (section `Email`): `Enabled` (default false), `Provider` (`Pickup` default, or `Smtp`), `From` (default `noreply@categoryproduct.local`), `PickupDirectory` (default `App_Data/email-outbox`), `Smtp:Host`, `Smtp:Port` (default 587), `Smtp:UserName`, `Smtp:Password`, `Smtp:EnableSsl` (default true). An empty-string value for any non-string setting MUST behave as unset (compose passes empty defaults).
- **FR-008**: `PickupDirectoryEmailSender` MUST write one UTF-8 `.eml` file per message (name `<UTC yyyyMMddTHHmmssfff>-<guid N>.eml`, `FileMode.CreateNew`), resolve the directory like `ProductImageStorage` (relative to `IHostEnvironment.ContentRootPath`), create it if missing, and log Information with the file name.
- **FR-009**: `SmtpEmailSender` MUST use `System.Net.Mail.SmtpClient` (no new package) with host, port, SSL and credentials from options only; credentials are used only when `UserName` is non-empty; the password is never logged or included in an exception message.
- **FR-010**: The provider MUST be selected once at startup (DI factory). Smtp with an empty host or an unknown provider name MUST log an Error and use the Pickup sender; the API MUST still start.
- **FR-011**: A singleton `EmailQueue` (System.Threading.Channels, bounded, capacity 1000, single reader) MUST expose `bool TryEnqueue(EmailMessage)` that never blocks and never throws: it returns false (and logs) when `Email:Enabled` is false, the message is invalid (FR-012) or the queue is full. It MUST also expose an awaitable idle signal (`WaitForIdleAsync(TimeSpan)`) used by tests and by shutdown draining.
- **FR-012**: `EmailMessage` validation: `To` valid per FR-002 rules, `Subject` non-empty, max 200 chars, no CR/LF; `Body` non-empty. Rejected messages are logged at Warning without the body.
- **FR-013**: `EmailDispatchWorker : BackgroundService` MUST read the queue and call `IEmailSender.SendAsync` per message inside try/catch: failures are logged (recipient masked as `a***@domain`, exception logged, no body) and swallowed; no retry. On stop it drains remaining messages for at most 5 seconds.
- **FR-014**: A scoped `NotificationService` (API, `Services`) MUST provide `OrderPlacedAsync(Order)`, `OrderStatusChangedAsync(Order)` and `LowStockAsync(IEnumerable<StockMovement>)`; each builds plain-text messages, enqueues them, and catches every exception (Warning log) so callers never fail. It reads Admin recipients with one query (`Role == "Admin" && Email != null`) only when a crossing exists.
- **FR-015**: `OrdersController.Checkout` MUST call `OrderPlacedAsync` once after the commit and the `order.User` fix-up; `Cancel` and `SetStatus` MUST call `OrderStatusChangedAsync` once after a successful cancel or confirm. No call on any failure path.
- **FR-016**: `ProductController.PostStockMovement` and `OrdersController.Checkout` MUST call `LowStockAsync` with their saved movements after the save. `NotificationService` MUST select crossings only: `QuantityChange < 0 && QuantityAfter < threshold && QuantityAfter - QuantityChange >= threshold`, with `threshold = InventoryOptions.Effective`. `InventoryService.LogIfLowStock` is unchanged.
- **FR-017**: Message formats are in `contracts/email-messages.md`: UTF-8 plain text, `InvariantCulture` money (`F2`), UTC dates, order id, items and totals; the subject never contains user-supplied text except the product name in the low-stock subject (CR/LF removed, truncated so the subject stays within 200 characters).
- **FR-018**: MVC MUST add an optional `Email` to `RegisterViewModel` (format attribute on the client side; the API is the authority), `CategoryProductApiClient.RegisterAsync(userName, password, email)`, `GetMeAsync`, `SetEmailAsync`; a `ProfileViewModel`; `AccountController.Profile` GET/POST (`[Authorize]`), a `Views/Account/Profile.cshtml`, a nav link "Profile" for signed-in users; `UserItem` gains `string? Email = null` with an Email column in `Views/Users/Index.cshtml`. A 429 uses `ApiRateLimit.Message`; a 401 signs the user out and redirects to login (the `UsersController` pattern).
- **FR-019**: `docker-compose.yml` MUST add to `api.environment`: `Email__Enabled: ${EMAIL_ENABLED:-}`, `Email__Provider: ${EMAIL_PROVIDER:-}`, `Email__From: ${EMAIL_FROM:-}`, `Email__PickupDirectory: /data/email-outbox`, `Email__Smtp__Host: ${EMAIL_SMTP_HOST:-}`, `Email__Smtp__Port: ${EMAIL_SMTP_PORT:-}`, `Email__Smtp__UserName: ${EMAIL_SMTP_USERNAME:-}`, `Email__Smtp__Password: ${EMAIL_SMTP_PASSWORD:-}`, `Email__Smtp__EnableSsl: ${EMAIL_SMTP_ENABLESSL:-}`; and a named volume `emailoutbox` mounted at `/data/email-outbox`. `CategoryProduct.API/Dockerfile` MUST create and `chown` that directory like `/data/product-images`. `.env.example` MUST gain commented placeholders only.
- **FR-020**: `appsettings.json` MUST gain an `Email` section with `Enabled: false`, `Provider: Pickup`, `PickupDirectory`, `From` and an `Smtp` object with empty `Host`/`UserName`/`Password`; `appsettings.Development.json` MUST set `Email:Enabled: true`.
- **FR-021**: Tests per the plan: recording sender in `ApiFactory`; API tests for every acceptance scenario; pickup writer tests with a temp dir; MVC tests for the register field and the profile page; a SQL Server test for the filtered unique index (CI only). No `Thread.Sleep` or fixed delays: tests await the recording sender or `EmailQueue.WaitForIdleAsync`.
- **FR-022**: No change to existing endpoint routes, existing status codes, or existing response fields (only added optional `email` fields and new endpoints); no new NuGet package; no new project; no change to `CategoryProduct.slnx`.

### Key Entities

- **User** (changed): gains nullable, unique-when-present `Email`.
- **EmailMessage / IEmailSender / EmailOptions**: generic email transport contract. See `contracts/email-sender.md`.
- **EmailQueue / EmailDispatchWorker**: in-process queue and its consumer.
- **NotificationService**: builds and enqueues the three notification kinds. See `contracts/email-messages.md`.
- **Profile endpoints and pages**: `GET api/account/me`, `PUT api/account/email`, `/Account/Profile`. See `contracts/profile-endpoints.md`.
- **Email configuration**: see `contracts/email-config.md`.

## Success Criteria *(mandatory)*

- **SC-001**: `dotnet build CategoryProduct.slnx` has 0 errors and no new warnings in touched files; `dotnet test CategoryProduct.slnx` passes three consecutive times locally with more tests than the 023 baseline of 560. `CategoryProduct.API.SqlServerTests` (not in the solution) builds with 0 errors.
- **SC-002**: Every acceptance scenario of stories 1-4 has an automated test; no test uses a fixed sleep.
- **SC-003**: With the default configuration and no SMTP settings, a checkout in Development leaves a readable `.eml` in the pickup folder, and a throwing sender does not change the checkout response (test with a throwing sender).
- **SC-004**: A search over tracked files finds no SMTP user, password or host value.
- **SC-005**: A migration `AddUserEmail` exists, the snapshot matches the model (`HasPendingModelChanges()` false), and on CI the SQL Server test proves the unique index allows many NULLs and rejects a duplicate.
- **SC-006**: Existing tests pass without changing their expectations (only helper additions such as the `ApiFactory` registration of the recording sender).

## Out of Scope

- Email verification, confirmation links, unsubscribe, per-user notification preferences.
- Password reset (feature 025; this feature only keeps `IEmailSender` and `EmailQueue` reusable).
- HTML or templated emails, attachments, localization.
- Retry, dead-letter, a database outbox, persistence of queued mails across restarts.
- Pickup folder retention or cleanup.
- Emails for registration welcome, cart events, login or refresh events, product changes.
- Notifying Admins of new orders.
- Changing the `Inventory:LowStockThreshold` semantics or the existing low-stock log line.

## Assumptions

- **No questions were asked** (the requester asked for none); each open decision below is the safe, small option.
- **In-process `Channel` queue, not a DB outbox**: the simplest thing that satisfies "never block or fail a request" with no new table; the cost (mails lost on restart or crash) is accepted and documented (research R2). A DB outbox is the upgrade path when delivery guarantees matter.
- **Low-stock rule is "below the threshold"**, matching the existing `LogIfLowStock` (`QuantityAfter < Threshold`) and the `GET api/product/low-stock` query (`Quantity < threshold`), not "at or below"; an alert fires only on a downward crossing (before >= threshold, after < threshold). Threshold 0 disables.
- **Email is stored lower-case and trimmed**; uniqueness is case-insensitive by construction.
- **Email validation is stricter than `[EmailAddress]`** (which accepts `a@b`): `MailAddress` round trip, a dot in the domain, 254 max, no whitespace or control characters. It is not a deliverability check.
- **Visibility**: a user sees only their own email (`api/account/me`); Admins see all emails in `GET api/users` (needed to reach people; the Users page is already Admin-only); nobody else, and no email appears in orders, movements, or logs (logs mask the local part).
- **One message per recipient** (no shared `To`/`Cc` lists), so addresses are not disclosed between Admins.
- **Notifications go to the order's owner even when the owner performs the action** (a user cancelling gets a confirmation mail).
- **Defaults**: `Email:Enabled` is false in code and `appsettings.json`, true in `appsettings.Development.json` with Pickup. In compose it stays off unless `EMAIL_ENABLED=true` is set (empty passthrough), so a stack never silently fills a volume with files.
- **Failure policy**: failed sends are logged and dropped, no retry (keeps the worker simple; transient SMTP errors are a Known Gap).
- **Misconfigured Smtp falls back to Pickup with an Error log** instead of failing startup, the same fail-safe stance as `BootstrapAdmin` in 023.
- **`System.Net.Mail.SmtpClient`** is used although Microsoft recommends MailKit for new code: no new dependency is wanted, SMTP is optional, and the interface lets MailKit replace it later.
- **New endpoints stay under the existing `api/account` controller** (`me`, `email`) to match the existing route style rather than adding a plural `profiles` resource; the constitution's REST rules (verbs, status codes, DTOs) are followed.
- **`UserResponseDto` gets an optional `Email`** instead of a second DTO; because the user lists are Admin-only, this does not widen exposure.
- **Notification code runs inside the request after the DB commit**, including one small Admin-email query on a low-stock crossing; it never throws (try/catch), so it cannot fail the response. Moving it fully off-request would need an outbox.
- **Tests**: `ApiFactory` replaces `IEmailSender` with a recording sender and sets `Email:Enabled=true`; the real queue and worker run, so the whole path (controller, notification, queue, worker, sender) is covered.

## Known Gaps

- **Not implemented yet** (spec only; filled in at the end, task T037).
- **Queued mails are lost** when the process stops or crashes (in-memory queue; the shutdown drain is best effort, 5 seconds), and failed sends are not retried.
- **No email verification**: any user can enter any address (including another person's); the mails contain no secrets or links.
- **No retention** for the pickup folder; it grows until an operator cleans it.
- **`SmtpClient`** is a maintenance-mode API; replace it with MailKit when SMTP becomes a real production path.
- **Real SMTP delivery is not tested** (no credentials, no server); only the pickup path and the selection logic are covered.
- **The SQL Server filtered-index test** can only run on CI (`sqlserver-tests`); locally it is only compiled.
- **Low-stock rule wording**: the request said "to/below the threshold"; the existing rule "below the threshold" is used (see Assumptions).
