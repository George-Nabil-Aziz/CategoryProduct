using CategoryProduct.API.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CategoryProduct.API.Tests
{
    public class EmailQueueTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private static EmailMessage Msg(string to = "alice@example.com", string subject = "S", string body = "B") =>
            new(to, subject, body);

        private static EmailQueue NewQueue(CollectingLogger log, bool enabled = true, int capacity = EmailQueue.DefaultCapacity) =>
            new(Options.Create(new EmailOptions { Enabled = enabled }), new TypedLogger<EmailQueue>(log), capacity);

        private sealed class TypedLogger<T>(CollectingLogger inner) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                inner.Log(logLevel, eventId, state, exception, formatter);
        }

        private sealed class GatedSender : IEmailSender
        {
            private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly List<string> Sent = new();
            public void Release() => _release.TrySetResult();
            public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
            {
                await _release.Task.WaitAsync(cancellationToken);
                lock (Sent) Sent.Add(message.Subject);
            }
        }

        private static EmailDispatchWorker NewWorker(EmailQueue queue, IEmailSender sender, CollectingLogger log,
            TimeSpan? drain = null) =>
            new(queue, sender, new TypedLogger<EmailDispatchWorker>(log), drain);

        [Fact]
        public void TryEnqueue_Disabled_ReturnsFalse_AndNothingIsPending()
        {
            var queue = NewQueue(new CollectingLogger(), enabled: false);
            Assert.False(queue.TryEnqueue(Msg()));
            Assert.True(queue.WaitForIdleAsync(TimeSpan.Zero).IsCompletedSuccessfully);
        }

        [Fact]
        public void TryEnqueue_InvalidMessage_ReturnsFalse_AndLogsWarningWithoutBody()
        {
            var log = new CollectingLogger();
            var queue = NewQueue(log);

            Assert.False(queue.TryEnqueue(Msg(subject: "bad\r\nsubject", body: "TOP-SECRET-BODY")));
            Assert.False(queue.TryEnqueue(Msg(to: "not-an-address")));
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning);
            Assert.False(log.Contains("TOP-SECRET-BODY"));
            Assert.True(queue.WaitForIdleAsync(TimeSpan.Zero).IsCompletedSuccessfully);
        }

        [Fact]
        public async Task TryEnqueue_Full_ReturnsFalse_AndLogsWarning_ThenIdleStillCompletes()
        {
            var log = new CollectingLogger();
            var queue = NewQueue(log, capacity: 2);
            var sender = new RecordingEmailSender();

            // No worker yet, so nothing is consumed and the third message does not fit.
            Assert.True(queue.TryEnqueue(Msg(subject: "1")));
            Assert.True(queue.TryEnqueue(Msg(subject: "2")));
            Assert.False(queue.TryEnqueue(Msg(subject: "3")));
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("full"));
            Assert.False(queue.WaitForIdleAsync(TimeSpan.Zero).IsCompletedSuccessfully);

            // Only the two accepted messages are pending; the rejected one must not keep the queue busy.
            var worker = NewWorker(queue, sender, log);
            await worker.StartAsync(CancellationToken.None);
            try
            {
                await queue.WaitForIdleAsync(Timeout);
                Assert.Equal(new[] { "1", "2" }, sender.Messages.Select(m => m.Subject));
            }
            finally { await worker.StopAsync(CancellationToken.None); }
        }

        [Fact]
        public async Task Worker_SendsInOrder_AndIdleCompletes()
        {
            var log = new CollectingLogger();
            var queue = NewQueue(log);
            var sender = new RecordingEmailSender();
            var worker = NewWorker(queue, sender, log);
            await worker.StartAsync(CancellationToken.None);
            try
            {
                for (var i = 0; i < 20; i++)
                    Assert.True(queue.TryEnqueue(Msg(subject: $"m{i}")));
                await queue.WaitForIdleAsync(Timeout);

                Assert.Equal(Enumerable.Range(0, 20).Select(i => $"m{i}"), sender.Messages.Select(m => m.Subject));
            }
            finally { await worker.StopAsync(CancellationToken.None); }
        }

        [Fact]
        public async Task Worker_ThrowingSender_DoesNotStopLaterMessages_AndIdleCompletes()
        {
            var log = new CollectingLogger();
            var queue = NewQueue(log);
            var sender = new RecordingEmailSender { ThrowOnSend = true };
            var worker = NewWorker(queue, sender, log);
            await worker.StartAsync(CancellationToken.None);
            try
            {
                Assert.True(queue.TryEnqueue(Msg(subject: "fails-1")));
                Assert.True(queue.TryEnqueue(Msg(subject: "fails-2")));
                await queue.WaitForIdleAsync(Timeout);
                Assert.Empty(sender.Messages);
                Assert.Equal(2, sender.Attempts);

                sender.ThrowOnSend = false;
                Assert.True(queue.TryEnqueue(Msg(subject: "works")));
                await sender.WaitForCountAsync(1, Timeout);
                await queue.WaitForIdleAsync(Timeout);
                Assert.Equal("works", Assert.Single(sender.Messages).Subject);
                Assert.Contains(log.Entries, e => e.Level == LogLevel.Error);
            }
            finally { await worker.StopAsync(CancellationToken.None); }
        }

        [Fact]
        public async Task Worker_Logs_MaskedRecipient_NeverBodyOrFullAddress()
        {
            var log = new CollectingLogger();
            var queue = NewQueue(log);
            var ok = new RecordingEmailSender();
            var worker = NewWorker(queue, ok, log);
            await worker.StartAsync(CancellationToken.None);
            try
            {
                Assert.True(queue.TryEnqueue(Msg("alice@example.com", body: "TOP-SECRET-BODY")));
                await queue.WaitForIdleAsync(Timeout);
                ok.ThrowOnSend = true;
                Assert.True(queue.TryEnqueue(Msg("bob@example.com", body: "TOP-SECRET-BODY")));
                await queue.WaitForIdleAsync(Timeout);
            }
            finally { await worker.StopAsync(CancellationToken.None); }

            Assert.True(log.Contains("a***@example.com"));
            Assert.True(log.Contains("b***@example.com"));
            Assert.False(log.Contains("alice@example.com"));
            Assert.False(log.Contains("bob@example.com"));
            Assert.False(log.Contains("TOP-SECRET-BODY"));
        }

        [Fact]
        public async Task Stop_DrainsQueuedMessages()
        {
            var log = new CollectingLogger();
            var queue = NewQueue(log);
            var sender = new RecordingEmailSender();
            var worker = NewWorker(queue, sender, log);

            // The worker was never started: everything is still queued when stop is requested.
            for (var i = 0; i < 5; i++)
                Assert.True(queue.TryEnqueue(Msg(subject: $"m{i}")));

            await worker.StopAsync(CancellationToken.None);

            Assert.Equal(5, sender.Messages.Count);
            await queue.WaitForIdleAsync(Timeout);
            Assert.False(queue.TryEnqueue(Msg(subject: "after-stop")));
        }

        [Fact]
        public async Task Stop_WhenDrainLimitExpires_LeftoversAreCountedAsLost_AndIdleCompletes()
        {
            var log = new CollectingLogger();
            var queue = NewQueue(log);
            var gated = new GatedSender(); // never released: every send waits until cancelled
            var worker = NewWorker(queue, gated, log, drain: TimeSpan.FromMilliseconds(100));

            for (var i = 0; i < 3; i++)
                Assert.True(queue.TryEnqueue(Msg(subject: $"m{i}")));

            await worker.StopAsync(CancellationToken.None);

            Assert.Empty(gated.Sent);
            await queue.WaitForIdleAsync(Timeout);
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("lost"));
            Assert.False(log.Contains("m0"));
        }

        [Fact]
        public async Task WaitForIdle_TimesOut_WhenWorkIsStuck()
        {
            var queue = NewQueue(new CollectingLogger());
            Assert.True(queue.TryEnqueue(Msg()));
            await Assert.ThrowsAsync<TimeoutException>(() => queue.WaitForIdleAsync(TimeSpan.FromMilliseconds(50)));
        }

        [Fact]
        public async Task ConcurrentEnqueue_AllAreHandled()
        {
            var log = new CollectingLogger();
            var queue = NewQueue(log);
            var sender = new RecordingEmailSender();
            var worker = NewWorker(queue, sender, log);
            await worker.StartAsync(CancellationToken.None);
            try
            {
                await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
                {
                    for (var i = 0; i < 25; i++)
                        Assert.True(queue.TryEnqueue(Msg(subject: $"t{t}-{i}")));
                })));
                await queue.WaitForIdleAsync(Timeout);
                Assert.Equal(200, sender.Messages.Count);
            }
            finally { await worker.StopAsync(CancellationToken.None); }
        }
    }
}
