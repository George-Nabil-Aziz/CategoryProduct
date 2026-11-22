using CategoryProduct.API.Services;

namespace CategoryProduct.API.Tests
{
    /// <summary>Thread-safe in-memory IEmailSender for tests. No delays: waiters are signalled on each send.</summary>
    public sealed class RecordingEmailSender : IEmailSender
    {
        private readonly object _gate = new();
        private readonly List<EmailMessage> _messages = new();
        private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = new();

        /// <summary>When true, SendAsync throws (the message is still recorded as an attempt in <see cref="Attempts"/>).</summary>
        public volatile bool ThrowOnSend;

        private int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);

        public IReadOnlyList<EmailMessage> Messages
        {
            get { lock (_gate) return _messages.ToList(); }
        }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _attempts);
            if (ThrowOnSend)
                throw new InvalidOperationException("Simulated send failure.");
            lock (_gate)
            {
                _messages.Add(message);
                foreach (var w in _waiters.Where(w => _messages.Count >= w.Count).ToList())
                {
                    w.Signal.TrySetResult();
                    _waiters.Remove(w);
                }
            }
            return Task.CompletedTask;
        }

        public void Clear()
        {
            lock (_gate) _messages.Clear();
        }

        /// <summary>Completes when at least <paramref name="count"/> messages were recorded; throws TimeoutException otherwise.</summary>
        public Task WaitForCountAsync(int count, TimeSpan? timeout = null)
        {
            TaskCompletionSource signal;
            lock (_gate)
            {
                if (_messages.Count >= count) return Task.CompletedTask;
                signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, signal));
            }
            return signal.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(10));
        }
    }
}
