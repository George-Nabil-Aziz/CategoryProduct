namespace CategoryProduct.API.Services
{
    public class RefreshTokenCleanupWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RefreshTokenCleanupWorker> _logger;
        private readonly TimeProvider _time;

        public RefreshTokenCleanupWorker(
            IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<RefreshTokenCleanupWorker> logger,
            TimeProvider? timeProvider = null)
        {
            _time = timeProvider ?? TimeProvider.System;
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var minutes = _configuration.GetValue("RefreshTokenCleanup:IntervalMinutes", 60);
            if (minutes <= 0) minutes = 60;
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes), _time);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetRequiredService<RefreshTokenCleanupService>();
                        var count = await service.PurgeAsync(_time.GetUtcNow().UtcDateTime, stoppingToken);
                        _logger.LogInformation("Purged {Count} old refresh tokens", count);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Refresh token purge failed");
                    }
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}
