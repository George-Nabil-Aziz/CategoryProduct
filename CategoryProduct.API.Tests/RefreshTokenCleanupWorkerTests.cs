using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using CategoryProduct.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CategoryProduct.API.Tests
{
    public class RefreshTokenCleanupWorkerTests
    {
        private sealed class SignalingTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
        {
            public TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                var t = base.CreateTimer(callback, state, dueTime, period);
                TimerCreated.TrySetResult();
                return t;
            }
        }

        private sealed class Harness : IAsyncDisposable
        {
            public SignalingTimeProvider Time { get; } = new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
            public ServiceProvider Provider { get; }
            public RefreshTokenCleanupWorker Worker { get; }
            public int ScopeCalls;
            private readonly string _dbName = "worker-" + Guid.NewGuid();
            private readonly CancellationTokenSource _cts = new();
            public CancellationToken Token => _cts.Token;

            private sealed class CountingScopeFactory(IServiceScopeFactory inner, Harness h, bool failFirst) : IServiceScopeFactory
            {
                public IServiceScope CreateScope()
                {
                    var n = Interlocked.Increment(ref h.ScopeCalls);
                    if (failFirst && n == 1) throw new InvalidOperationException("boom");
                    return inner.CreateScope();
                }
            }

            public Harness(string? intervalMinutes, bool failFirst = false)
            {
                var values = new Dictionary<string, string?>();
                if (intervalMinutes != null) values["RefreshTokenCleanup:IntervalMinutes"] = intervalMinutes;
                var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
                var services = new ServiceCollection();
                services.AddSingleton<IConfiguration>(config);
                services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(_dbName));
                services.AddScoped<RefreshTokenCleanupService>();
                Provider = services.BuildServiceProvider();
                var factory = new CountingScopeFactory(Provider.GetRequiredService<IServiceScopeFactory>(), this, failFirst);
                Worker = new RefreshTokenCleanupWorker(factory, config, NullLogger<RefreshTokenCleanupWorker>.Instance, Time);
            }

            private async Task SeedAsync(string hash, int createdDaysAgo, int expiresInDays)
            {
                using var scope = Provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var now = Time.GetUtcNow().UtcDateTime;
                var user = await db.Users.FirstOrDefaultAsync()
                    ?? db.Users.Add(new User { UserName = "u", PasswordHash = "x", Role = "User" }).Entity;
                db.RefreshTokens.Add(new RefreshToken
                {
                    User = user,
                    TokenHash = hash,
                    CreatedAtUtc = now.AddDays(-createdDaysAgo),
                    ExpiresAtUtc = now.AddDays(expiresInDays)
                });
                await db.SaveChangesAsync();
            }

            public async Task StartAsync(CancellationToken token)
            {
                await Worker.StartAsync(token);
                await Time.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(10)); // wait until ExecuteAsync registered its PeriodicTimer
            }

            public Task SeedOldAsync(string hash) => SeedAsync(hash, 30, -20);
            public Task SeedRecentAsync(string hash) => SeedAsync(hash, 1, 5);

            public async Task<List<string>> HashesAsync()
            {
                using var scope = Provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                return await db.RefreshTokens.Select(r => r.TokenHash).OrderBy(x => x).ToListAsync();
            }

            public async ValueTask DisposeAsync()
            {
                _cts.Cancel();
                await Worker.StopAsync(CancellationToken.None);
                await Provider.DisposeAsync();
                _cts.Dispose();
            }
        }

        private static async Task WaitUntilAsync(Func<Task<bool>> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (await condition()) return;
                await Task.Delay(20);
            }
            Assert.True(await condition(), "Condition not met within 10 seconds");
        }

        [Fact]
        public async Task Interval_Elapsed_PurgesOldAndKeepsRecent()
        {
            await using var h = new Harness("60");
            await h.SeedOldAsync("old");
            await h.SeedRecentAsync("recent");
            await h.StartAsync(h.Token);

            h.Time.Advance(TimeSpan.FromMinutes(60));

            await WaitUntilAsync(async () => (await h.HashesAsync()).SequenceEqual(new[] { "recent" }));
        }

        [Fact]
        public async Task Before_Interval_NothingPurged()
        {
            await using var h = new Harness("60");
            await h.SeedOldAsync("old");
            await h.StartAsync(h.Token);

            h.Time.Advance(TimeSpan.FromMinutes(59));
            await Task.Delay(200);

            Assert.Equal(new[] { "old" }, await h.HashesAsync());
            Assert.Equal(0, h.ScopeCalls);
        }

        [Fact]
        public async Task Repeats_OnNextInterval()
        {
            await using var h = new Harness("60");
            await h.StartAsync(h.Token);

            await h.SeedOldAsync("old1");
            h.Time.Advance(TimeSpan.FromMinutes(60));
            await WaitUntilAsync(async () => (await h.HashesAsync()).Count == 0);

            await h.SeedOldAsync("old2");
            h.Time.Advance(TimeSpan.FromMinutes(60));
            await WaitUntilAsync(async () => (await h.HashesAsync()).Count == 0);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-5")]
        public async Task NonPositiveInterval_FallsBackTo60Minutes(string interval)
        {
            await using var h = new Harness(interval);
            await h.SeedOldAsync("old");
            await h.StartAsync(h.Token);

            h.Time.Advance(TimeSpan.FromMinutes(59));
            await Task.Delay(200);
            Assert.Equal(new[] { "old" }, await h.HashesAsync());

            h.Time.Advance(TimeSpan.FromMinutes(1));
            await WaitUntilAsync(async () => (await h.HashesAsync()).Count == 0);
        }

        [Fact]
        public async Task ThrowingFirstTick_DoesNotStopWorker()
        {
            await using var h = new Harness("60", failFirst: true);
            await h.SeedOldAsync("old");
            await h.StartAsync(h.Token);

            h.Time.Advance(TimeSpan.FromMinutes(60));
            await WaitUntilAsync(() => Task.FromResult(h.ScopeCalls >= 1));
            Assert.Equal(new[] { "old" }, await h.HashesAsync());

            h.Time.Advance(TimeSpan.FromMinutes(60));
            await WaitUntilAsync(async () => (await h.HashesAsync()).Count == 0);
        }

        [Fact]
        public async Task StopAsync_Completes()
        {
            await using var h = new Harness("60");
            await h.StartAsync(h.Token);

            var stop = h.Worker.StopAsync(CancellationToken.None);

            Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(10))));
            await stop;
        }

        [Fact]
        public async Task CancelBeforeFirstTick_PurgesNothing()
        {
            await using var h = new Harness("60");
            await h.SeedOldAsync("old");
            using var cts = new CancellationTokenSource();
            await h.StartAsync(cts.Token);
            cts.Cancel();

            h.Time.Advance(TimeSpan.FromMinutes(120));
            await Task.Delay(200);

            Assert.Equal(new[] { "old" }, await h.HashesAsync());
            Assert.Equal(0, h.ScopeCalls);
        }
    }
}
