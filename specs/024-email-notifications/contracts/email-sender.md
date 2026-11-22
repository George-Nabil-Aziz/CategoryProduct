# Contract: IEmailSender, EmailMessage, EmailQueue, worker

Generic on purpose: feature 025 (password reset) injects `EmailQueue` and builds its own `EmailMessage`; nothing here mentions orders or users.

## Types (namespace `CategoryProduct.API.Services`)

```csharp
public sealed record EmailMessage(string To, string Subject, string Body)
{
    /// null when valid; otherwise a short reason that never contains the body.
    public string? Validate();
}

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class EmailQueue          // singleton
{
    public bool TryEnqueue(EmailMessage message);                       // never blocks, never throws
    public Task WaitForIdleAsync(TimeSpan timeout);                      // all enqueued messages have been handled (sent or failed)
    internal ChannelReader<EmailMessage> Reader { get; }                 // for the worker
    internal void MarkHandled();                                         // worker calls after each message
    internal void Complete();                                            // on shutdown
}

public sealed class EmailDispatchWorker : BackgroundService { }
```

## `EmailMessage.Validate()`

- `To`: passes `EmailAddressRules` (already normalized form accepted; whitespace/control characters rejected).
- `Subject`: not empty, at most 200 characters, no CR or LF.
- `Body`: not empty.
- Rejected messages: `EmailQueue.TryEnqueue` returns false and logs Warning with the reason, the masked recipient, never the body.

## `EmailQueue.TryEnqueue`

1. `Email:Enabled` false: return false, Debug log.
2. `Validate()` not null: return false, Warning.
3. `Channel.Writer.TryWrite`: false when full (capacity 1000): Warning "Email queue is full; message dropped."
4. On success increments the pending counter (before the write, rolled back if the write fails) and returns true.
5. Any unexpected exception: caught, logged, returns false.

`WaitForIdleAsync(timeout)`: returns when the pending counter is 0, or throws `TimeoutException` after the timeout. Uses a `TaskCompletionSource` completed by `MarkHandled()` when the counter reaches 0; no fixed delay.

## `EmailDispatchWorker`

```
await foreach (var m in queue.Reader.ReadAllAsync(stopping)):
    try   { await sender.SendAsync(m, stopping); log Information "Email sent to {MaskedTo}" }
    catch (OperationCanceledException) when stopping requested: break (the message stays unhandled; shutdown drain below)
    catch (Exception ex) { log Error(ex, "Email to {MaskedTo} failed; dropped.") }
    finally { queue.MarkHandled(); }
StopAsync: queue.Complete(); keep reading with a separate 5 s CancellationTokenSource; leftovers are logged as lost (count only).
```

The worker resolves `IEmailSender` from DI once (singleton). It never logs `Body` or the SMTP password. Exceptions are logged with `ex` (type and message) only.

## Providers

### `PickupDirectoryEmailSender`

- Directory: `Email:PickupDirectory` resolved like `ProductImageStorage` (`Path.IsPathRooted ? p : Path.Combine(env.ContentRootPath, p)`, then `Path.GetFullPath`); `Directory.CreateDirectory` before each write.
- File name: `{DateTime.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.eml`; `FileMode.CreateNew`, `FileShare.None`; on failure the partial file is deleted and the exception is rethrown to the worker.
- Content (CRLF line endings, UTF-8 without BOM):

```
From: <Email:From>
To: <message.To>
Subject: <message.Subject, RFC 2047 encoded when non-ASCII>
Date: <UTC, RFC 1123>
Message-ID: <{guid}@categoryproduct.local>
MIME-Version: 1.0
Content-Type: text/plain; charset=utf-8
Content-Transfer-Encoding: 8bit

<body>
```

- The body is written as is (plain text; lines normalized to CRLF); a body line starting with `From ` needs no escaping (not mbox).
- Logs Information: `Email written to {FileName}` (file name only, no path with user data, no body).

### `SmtpEmailSender`

- `new SmtpClient(host, port) { EnableSsl = options.EnableSsl, DeliveryMethod = Network, Timeout = 30000 }`; `Credentials = new NetworkCredential(user, password)` only when `UserName` is non-empty.
- `MailMessage(from, to, subject, body) { IsBodyHtml = false, SubjectEncoding = UTF8, BodyEncoding = UTF8 }`; `await client.SendMailAsync(mail, ct)`.
- One client per send (disposed), no pooling (volume is tiny).
- Never logs credentials; exceptions propagate to the worker's catch.

## Selection (Program.cs)

```
Enabled false     -> still registers the pickup sender (never used; queue refuses messages)
Provider Smtp     -> Host non-empty ? SmtpEmailSender : (log Error; PickupDirectoryEmailSender)
Provider Pickup   -> PickupDirectoryEmailSender
other             -> log Error; PickupDirectoryEmailSender
```

Registered as `services.AddSingleton<IEmailSender>(sp => ...)`; tests call `services.RemoveAll<IEmailSender>()` and add their own.

## Reuse by 025

```csharp
// inside a password-reset service
queue.TryEnqueue(new EmailMessage(user.Email, "Reset your password", body));
```

No change to this contract is needed; 025 decides what is safe to put in a body (a reset link or token is a secret: it must not be logged here, and this contract already never logs bodies).
