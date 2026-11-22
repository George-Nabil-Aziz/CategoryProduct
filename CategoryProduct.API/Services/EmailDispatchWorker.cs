namespace CategoryProduct.API.Services
{
    /// <summary>Drains <see cref="EmailQueue"/> into <see cref="IEmailSender"/>. Failures are logged and dropped.</summary>
    public sealed class EmailDispatchWorker : BackgroundService
    {
        private readonly EmailQueue _queue;
        private readonly IEmailSender _sender;
        private readonly ILogger<EmailDispatchWorker> _logger;
        private readonly TimeSpan _drainLimit;

        public EmailDispatchWorker(EmailQueue queue, IEmailSender sender, ILogger<EmailDispatchWorker> logger,
            TimeSpan? drainLimit = null)
        {
            _queue = queue;
            _sender = sender;
            _logger = logger;
            _drainLimit = drainLimit ?? TimeSpan.FromSeconds(5);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
                {
                    if (!await HandleAsync(message, stoppingToken)) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);

            _queue.Complete();
            using var drain = new CancellationTokenSource(_drainLimit);
            while (!drain.IsCancellationRequested && _queue.Reader.TryRead(out var message))
                await HandleAsync(message, drain.Token);

            var lost = 0;
            while (_queue.Reader.TryRead(out _))
            {
                _queue.MarkHandled();
                lost++;
            }
            if (lost > 0)
                _logger.LogWarning("{Count} queued email(s) were lost at shutdown.", lost);
        }

        /// <summary>Returns false when processing should stop (cancelled while sending).</summary>
        private async Task<bool> HandleAsync(EmailMessage message, CancellationToken token)
        {
            try
            {
                await _sender.SendAsync(message, token);
                _logger.LogInformation("Email sent to {MaskedTo}", EmailAddressRules.Mask(message.To));
                return true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _logger.LogWarning("Email to {MaskedTo} was cancelled during shutdown.",
                    EmailAddressRules.Mask(message.To));
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email to {MaskedTo} failed; dropped.", EmailAddressRules.Mask(message.To));
                return true;
            }
            finally
            {
                _queue.MarkHandled();
            }
        }
    }
}
