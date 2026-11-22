using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace CategoryProduct.API.Services
{
    /// <summary>
    /// In-process bounded queue of outgoing emails (singleton). Feature code only enqueues; the
    /// <see cref="EmailDispatchWorker"/> sends. Enqueue never blocks and never throws.
    /// </summary>
    public sealed class EmailQueue
    {
        public const int DefaultCapacity = 1000;

        private readonly Channel<EmailMessage> _channel;
        private readonly EmailOptions _options;
        private readonly ILogger<EmailQueue> _logger;
        private readonly object _gate = new();
        private int _pending;
        private TaskCompletionSource _idle = CompletedSignal();

        public EmailQueue(IOptions<EmailOptions> options, ILogger<EmailQueue> logger, int capacity = DefaultCapacity)
        {
            _options = options.Value;
            _logger = logger;
            _channel = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        private static TaskCompletionSource CompletedSignal()
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs.SetResult();
            return tcs;
        }

        public bool TryEnqueue(EmailMessage message)
        {
            try
            {
                if (!_options.Enabled)
                {
                    _logger.LogDebug("Email is disabled; message not queued.");
                    return false;
                }
                var problem = message.Validate();
                if (problem != null)
                {
                    _logger.LogWarning("Email to {MaskedTo} rejected: {Reason}",
                        EmailAddressRules.Mask(message.To), problem);
                    return false;
                }

                lock (_gate)
                {
                    if (_pending == 0)
                        _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pending++;
                }
                if (!_channel.Writer.TryWrite(message))
                {
                    Release();
                    _logger.LogWarning("Email queue is full; message dropped.");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Email could not be queued.");
                return false;
            }
        }

        /// <summary>Completes when every enqueued message has been handled (sent or failed). Throws TimeoutException.</summary>
        public Task WaitForIdleAsync(TimeSpan timeout)
        {
            Task signal;
            lock (_gate) signal = _idle.Task;
            return signal.WaitAsync(timeout);
        }

        internal ChannelReader<EmailMessage> Reader => _channel.Reader;

        internal void MarkHandled() => Release();

        internal void Complete() => _channel.Writer.TryComplete();

        private void Release()
        {
            lock (_gate)
            {
                if (_pending > 0 && --_pending == 0)
                    _idle.TrySetResult();
            }
        }
    }
}
