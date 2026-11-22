# Implementation Plan: Email Notifications

**Branch**: `024-email-notifications` | **Date**: 2026-11-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/024-email-notifications/spec.md`

## Summary

(1) `User.Email` (nullable, unique when present) with an `AddUserEmail` migration, optional email on register (API and MVC form), `GET api/account/me` and `PUT api/account/email` plus an MVC `/Account/Profile` page, and an Email column in the Admin Users list. (2) A generic `IEmailSender` with two implementations (pickup `.eml` files by default, optional SMTP through `System.Net.Mail`), fed by a bounded in-process `Channel` queue and drained by a `BackgroundService`; failures are logged and swallowed. (3) A scoped `NotificationService` that composes plain-text mails and enqueues them for: order placed, order Confirmed/Cancelled (to the order's owner), and a low-stock downward crossing (to every Admin with an email). Everything is behind `Email:Enabled`. No new package, no secrets, no new project.

## Technical Context

**Language/Version**: C# / .NET 10.

**Primary Dependencies**: none added. `System.Threading.Channels` and `System.Net.Mail` are part of the shared framework; EF Core and `Microsoft.Extensions.Options` already referenced.

**Storage**: SQL Server via EF Core: one nullable column and one filtered unique index (migration). InMemory in tests (does not enforce the unique index; controllers check too). Pickup files on disk.

**Testing**: xunit. `CategoryProduct.API.Tests` (`ApiFactory`, InMemory), `CategoryProduct.MVC.Tests` (`FakeApi`, `MvcTestBase`, `HtmlHelpers`), `CategoryProduct.API.SqlServerTests` (CI only, not in `CategoryProduct.slnx`).

**Target Platform**: local `dotnet test CategoryProduct.slnx`; GitHub Actions jobs unchanged.

**Project Type**: existing web API + MVC; no new project.

**Constraints**: constitution principles I-V; sending never blocks or fails a request; no real credentials in tracked files; existing tests keep their expectations; API contract only grows (new optional fields, new endpoints).

**Scale/Scope**: 1 model change + 1 generated migration, ~8 new API files (options, message, sender interface, 2 senders, queue, worker, notification service, address rules), 3 API controllers edited, `Program.cs` registration, 2 appsettings, compose/Dockerfile/.env.example, MVC (client, view model, controller actions, 1 new view, 2 views edited, layout nav), ~8 new/edited test files.

## Constitution Check

Checked against `.specify/memory/constitution.md` v1.0.0.

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | New endpoints use the existing `api/account` controller route pattern with standard verbs (`GET me`, `PUT email`) and standard status codes (200, 400 `ValidationProblem`, 401, 409 `ProblemDetails`, 429). No list endpoint is added; `GET api/users` keeps its pagination. Existing routes, verbs and status codes are unchanged; only optional response fields are added (`email`). `api/account` is singular because the existing controller is; a plural resource would be a second style for one feature. | PASS |
| II. Schema Changes Only Through EF Core Migrations | `User.Email` ships with `dotnet ef migrations add AddUserEmail --project CategoryProduct.API` in the same change; nothing is hand-edited; the SQL Server filter for the unique index is declared in `OnModelCreating` (`HasFilter`), not in the migration by hand. The MVC keeps no model of the API. | PASS |
| III. Simplicity & YAGNI | Controllers still use `ApplicationDbContext` directly. New abstractions have concrete second consumers: `IEmailSender` has two real implementations (pickup, SMTP) plus the test recorder and is explicitly needed by 025; the queue/worker is needed because sending must not run on the request path. No interface for `EmailQueue` or `NotificationService` (one implementation each; tests use the real ones). No outbox, retry or template engine. See Complexity Tracking. | PASS (justified) |
| IV. Secrets & Credential Hygiene (NON-NEGOTIABLE) | SMTP host/user/password come only from configuration or environment; `appsettings*.json`, `.env.example` and compose hold empty values or commented placeholders; the password is never logged, never in an exception message, never in the pickup file. Addresses are personal data: logs mask the local part, the pickup folder is outside any served path and ignored by git. No password or token is ever put in an email by this feature. | PASS |
| V. API/MVC Separation | MVC talks to `api/account/me`, `api/account/email` and `register` over HTTP with its own view models; it references no API type. MVC tests reference only `CategoryProduct.MVC`. | PASS |

Workflow gates: "Every change that adds or modifies a Model class MUST include the migration": task T019 (generation) and T034 (review of the diff). "No secret values in tracked configuration": review task T033 (grep). "Controller behavior verified": automated tests per story.

## Project Structure

### Documentation

```text
specs/024-email-notifications/
  spec.md  plan.md  research.md  data-model.md  quickstart.md  tasks.md
  contracts/{email-sender.md, email-config.md, email-messages.md, profile-endpoints.md}
  checklists/requirements.md
```

### Source changes

```text
CategoryProduct.API/Models/User.cs                                NEW property Email
CategoryProduct.API/Data/ApplicationDbContext.cs                  EDIT Email: MaxLength(254), unique index with filter
CategoryProduct.API/Migrations/<ts>_AddUserEmail(.Designer).cs    NEW generated; snapshot regenerated by the tool
CategoryProduct.API/Dtos/RegisterRequestDto.cs                    EDIT optional Email
CategoryProduct.API/Dtos/UserResponseDto.cs                       EDIT optional Email
CategoryProduct.API/Dtos/ProfileDtos.cs                           NEW UpdateEmailRequestDto, ProfileResponseDto
CategoryProduct.API/Controllers/AccountController.cs              EDIT register email; GET me; PUT email
CategoryProduct.API/Controllers/UsersController.cs                EDIT list/role responses carry Email
CategoryProduct.API/Controllers/OrdersController.cs               EDIT inject NotificationService; 3 call sites
CategoryProduct.API/Controllers/ProductController.cs              EDIT inject NotificationService; 1 call site
CategoryProduct.API/Services/EmailAddressRules.cs                 NEW normalize/validate
CategoryProduct.API/Services/EmailMessage.cs                      NEW record + validation
CategoryProduct.API/Services/IEmailSender.cs                      NEW
CategoryProduct.API/Services/EmailOptions.cs                      NEW (+ SmtpOptions)
CategoryProduct.API/Services/PickupDirectoryEmailSender.cs        NEW
CategoryProduct.API/Services/SmtpEmailSender.cs                   NEW
CategoryProduct.API/Services/EmailQueue.cs                        NEW bounded channel + idle signal
CategoryProduct.API/Services/EmailDispatchWorker.cs               NEW BackgroundService
CategoryProduct.API/Services/NotificationService.cs               NEW compose + enqueue (never throws)
CategoryProduct.API/Program.cs                                    EDIT options, sender factory, queue, worker, "profile" rate limit
CategoryProduct.API/appsettings.json, appsettings.Development.json EDIT Email section
CategoryProduct.API/Dockerfile                                    EDIT /data/email-outbox
docker-compose.yml, .env.example                                  EDIT passthroughs, volume, placeholders

CategoryProduct.API.Tests/ApiFactory.cs                           EDIT recording sender, Email:Enabled, ProfilePermitLimit
CategoryProduct.API.Tests/RecordingEmailSender.cs                 NEW
CategoryProduct.API.Tests/EmailAddressRulesTests.cs               NEW
CategoryProduct.API.Tests/UserEmailTests.cs                       NEW register/me/email/uniqueness/visibility
CategoryProduct.API.Tests/PickupDirectoryEmailSenderTests.cs      NEW
CategoryProduct.API.Tests/EmailQueueTests.cs                      NEW (queue + worker)
CategoryProduct.API.Tests/EmailOptionsTests.cs                    NEW provider selection and fallbacks
CategoryProduct.API.Tests/OrderEmailTests.cs                      NEW
CategoryProduct.API.Tests/LowStockEmailTests.cs                   NEW
CategoryProduct.API.SqlServerTests/UserEmailIndexSqlServerTests.cs NEW

CategoryProduct.MVC/Models/RegisterViewModel.cs                   EDIT Email; UserItem.Email
CategoryProduct.MVC/Models/ProfileViewModel.cs                    NEW
CategoryProduct.MVC/Services/CategoryProductApiClient.cs          EDIT RegisterAsync(email), GetMeAsync, SetEmailAsync
CategoryProduct.MVC/Controllers/AccountController.cs              EDIT Register email errors; Profile GET/POST
CategoryProduct.MVC/Views/Account/Register.cshtml, Profile.cshtml EDIT / NEW
CategoryProduct.MVC/Views/Users/Index.cshtml                      EDIT Email column
CategoryProduct.MVC/Views/Shared/_Layout.cshtml                   EDIT "Profile" link
CategoryProduct.MVC.Tests/AccountTests.cs, UsersTests.cs          EDIT additions
CategoryProduct.MVC.Tests/ProfileTests.cs                         NEW
```

**Structure Decision**: no new project. Foundation (US2) first because US3 and US4 depend on it; US1 (address) is independent of US2 and can run in parallel with it; US3 and US4 touch different controller methods but both touch `OrdersController` (Checkout), so they are sequenced.

## Design

1. **Model and migration**: `User.Email` (string?), `HasMaxLength(254)`, `HasIndex(u => u.Email).IsUnique().HasFilter("[Email] IS NOT NULL")`. Run `dotnet ef migrations add AddUserEmail`; verify it only adds the column and the index; snapshot diff limited to the same.
2. **Address rules** (`EmailAddressRules.TryNormalize(string? input, out string? normalized, out string? error)`): trim; empty means null; length, whitespace/control checks, `MailAddress.TryCreate(trimmed, out var a) && a.Address == trimmed`, domain has a dot; lower-case.
3. **Transport**: `IEmailSender.SendAsync(EmailMessage, ct)`. `PickupDirectoryEmailSender` builds an RFC 5322 text (CRLF line endings, `Content-Transfer-Encoding: 8bit`, UTF-8) and writes `FileMode.CreateNew`. `SmtpEmailSender` builds a `System.Net.Mail.MailMessage` (`IsBodyHtml=false`, UTF-8) and calls `SmtpClient.SendMailAsync`. Selection by a factory in `Program.cs` reading `EmailOptions` once.
4. **Queue/worker**: `EmailQueue` singleton: `Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(1000){ SingleReader = true, FullMode = Wait })`, `TryEnqueue` uses `TryWrite` (false when full), `Interlocked` pending counter, `WaitForIdleAsync(timeout)` completes when pending is 0 (a `TaskCompletionSource` swapped under a lock; no polling delays). `EmailDispatchWorker.ExecuteAsync` loops `ReadAllAsync`, try/catch per message, decrements pending in `finally`. `StopAsync` completes the writer and drains for up to 5 s.
5. **Notifications**: `NotificationService` (scoped; `EmailQueue`, `ApplicationDbContext`, `IOptions<InventoryOptions>`, `ILogger`). Builders in the contract. `OrderPlacedAsync` and `OrderStatusChangedAsync` read `order.User?.Email`; `LowStockAsync` filters crossings, loads Admin emails once, builds ONE digest message per Admin. Each public method wraps everything in try/catch.
6. **Controllers**: `AccountController` (register validation, `me`, `email` with `[EnableRateLimiting("profile")]` on `PUT`), `UsersController` projections add `u.Email` and `ApplyRoleAsync` returns it, `OrdersController` calls after success, `ProductController.PostStockMovement` calls after Ok. `Checkout` collects the saved movements it already has (`movements`) for `LowStockAsync`.
7. **Config/compose/docs**: per `contracts/email-config.md`.
8. **MVC**: `RegisterAsync` sends `email` only when non-empty; conflicts are mapped to the Email field when the API detail is `Email already in use.` (otherwise the existing "Username already taken."); `Profile` actions per `contracts/profile-endpoints.md`.
9. **Tests first** per story (tasks.md orders them that way). `ApiFactory`: `Email:Enabled=true`, `RemoveAll<IEmailSender>()` then register the recorder singleton (exposed as `Factory.Emails`), `RateLimiting:ProfilePermitLimit=1000`.

## Complexity Tracking

| Decision | Why it needs a reviewer's eye | Simpler alternative rejected |
|---|---|---|
| Background worker + bounded channel | Introduces the first non-trivial hosted service besides `RefreshTokenCleanupWorker`, and in-memory state that is lost on restart. | Sending inline in the request (violates "never block or fail a request"); DB outbox (new table, polling, migration, locking; disproportionate before real SMTP exists; see research R2). |
| `IEmailSender` interface | Principle III asks for a second concrete consumer: pickup, SMTP, test recorder and 025. | A static helper (cannot be swapped in tests or by config). |
| No interface for `EmailQueue` / `NotificationService` | Tests use the real classes and observe the recording sender. | Interfaces per class (speculative). |
| Optional trailing parameter on `RegisterRequestDto` and `UserResponseDto` | Changes two DTO shapes (additive, optional). | New DTO versions (duplicates for no gain). |
| Notification work inside the request (one Admin query on a low-stock crossing) | Small extra DB read on the request path. | Fully async resolution of recipients (needs an outbox or a queued "event" type). |

## Phase order

Phase 1 setup; Phase 2 US2 (foundation: sender, options, queue, worker, wiring, `ApiFactory` recorder); Phase 3 US1 (user email; parallel with Phase 2 except `ApiFactory`); Phase 4 US3 (order emails); Phase 5 US4 (low stock); Phase 6 hygiene; Phase 7 final verification and Known Gaps.
