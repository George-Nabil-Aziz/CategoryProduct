using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.Data
{
    public static class DatabaseMigrator
    {
        private const int MaxAttempts = 10;
        private static readonly TimeSpan Delay = TimeSpan.FromSeconds(3);

        public static async Task MigrateIfEnabledAsync(
            IServiceProvider services, IConfiguration configuration, ILogger logger)
        {
            if (!configuration.GetValue("Database:MigrateOnStartup", false))
                return;

            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (!db.Database.IsRelational())
            {
                logger.LogInformation("Skipping startup migration: database provider is not relational.");
                return;
            }

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    logger.LogInformation("Applying migrations (attempt {Attempt}/{Max})", attempt, MaxAttempts);
                    await db.Database.MigrateAsync();
                    logger.LogInformation("Migrations applied.");
                    return;
                }
                catch (Exception ex) when (attempt < MaxAttempts)
                {
                    logger.LogWarning(ex, "Migration attempt {Attempt} failed; retrying in {Seconds}s",
                        attempt, Delay.TotalSeconds);
                    await Task.Delay(Delay);
                }
            }
        }
    }
}
