using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CategoryProduct.API.Tests
{
    public class CollectingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));

        public bool Contains(string text) =>
            Entries.Any(e => e.Message.Contains(text) || (e.Exception?.ToString().Contains(text) ?? false));
    }

    public class BootstrapAdminTests
    {
        private const string BootName = "boot.admin";
        private const string BootPassword = "Str0ng-Throwaway-Pw";

        private sealed class Env : IDisposable
        {
            public ServiceProvider Services { get; }
            public CollectingLogger Logger { get; } = new();

            public Env()
            {
                var name = Guid.NewGuid().ToString();
                var root = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
                Services = new ServiceCollection()
                    .AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name, root))
                    .BuildServiceProvider();
            }

            public IConfiguration Config(string? name, string? password)
            {
                var d = new Dictionary<string, string?>();
                if (name != null) d["Bootstrap:AdminUserName"] = name;
                if (password != null) d["Bootstrap:AdminPassword"] = password;
                return new ConfigurationBuilder().AddInMemoryCollection(d).Build();
            }

            public Task<BootstrapAdminOutcome> RunAsync(string? name, string? password) =>
                BootstrapAdmin.EnsureIfConfiguredAsync(Services, Config(name, password), Logger);

            public async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> f)
            {
                using var scope = Services.CreateScope();
                return await f(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
            }

            public Task AddUserAsync(string name, string role, string hash = "x") => DbAsync(async db =>
            {
                db.Users.Add(new User { UserName = name, Role = role, PasswordHash = hash });
                await db.SaveChangesAsync();
                return 0;
            });

            public void Dispose() => Services.Dispose();
        }

        [Fact]
        public async Task ValidSettings_NoAdmin_CreatesAdmin_WithHashedPassword_AndNoPasswordInLogs()
        {
            using var env = new Env();

            var outcome = await env.RunAsync("  " + BootName + "  ", BootPassword);

            Assert.Equal(BootstrapAdminOutcome.Created, outcome);
            var users = await env.DbAsync(db => db.Users.ToListAsync());
            var user = Assert.Single(users);
            Assert.Equal(BootName, user.UserName);
            Assert.Equal("Admin", user.Role);
            Assert.NotEqual(BootPassword, user.PasswordHash);
            Assert.Equal(PasswordVerificationResult.Success,
                new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, BootPassword));
            Assert.Contains(env.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains(BootName));
            Assert.False(env.Logger.Contains(BootPassword));
        }

        [Fact]
        public async Task AdminExists_Skips_AndChangesNothing()
        {
            using var env = new Env();
            await env.AddUserAsync("existing", "Admin");

            var outcome = await env.RunAsync(BootName, BootPassword);

            Assert.Equal(BootstrapAdminOutcome.SkippedAdminExists, outcome);
            Assert.Equal(1, await env.DbAsync(db => db.Users.CountAsync()));
        }

        [Fact]
        public async Task SecondRun_IsIdempotent()
        {
            using var env = new Env();
            Assert.Equal(BootstrapAdminOutcome.Created, await env.RunAsync(BootName, BootPassword));
            Assert.Equal(BootstrapAdminOutcome.SkippedAdminExists, await env.RunAsync(BootName, BootPassword));
            Assert.Equal(1, await env.DbAsync(db => db.Users.CountAsync()));
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("   ", "   ")]
        public async Task NothingConfigured_DoesNothing_AndWarnsAboutMissingAdmin(string? name, string? password)
        {
            using var env = new Env();

            var outcome = await env.RunAsync(name, password);

            Assert.Equal(BootstrapAdminOutcome.NotConfigured, outcome);
            Assert.Equal(0, await env.DbAsync(db => db.Users.CountAsync()));
            Assert.Contains(env.Logger.Entries,
                e => e.Level == LogLevel.Warning && e.Message.Contains("No Admin user exists"));
        }

        [Fact]
        public async Task NothingConfigured_AdminPresent_NoWarning()
        {
            using var env = new Env();
            await env.AddUserAsync("existing", "Admin");

            Assert.Equal(BootstrapAdminOutcome.NotConfigured, await env.RunAsync(null, null));
            Assert.DoesNotContain(env.Logger.Entries, e => e.Level == LogLevel.Warning);
        }

        [Theory]
        [InlineData(BootName, null)]
        [InlineData(BootName, "")]
        [InlineData(null, BootPassword)]
        [InlineData("  ", BootPassword)]
        public async Task OnlyOneSetting_DoesNothing_AndWarns(string? name, string? password)
        {
            using var env = new Env();

            var outcome = await env.RunAsync(name, password);

            Assert.Equal(BootstrapAdminOutcome.NotConfigured, outcome);
            Assert.Equal(0, await env.DbAsync(db => db.Users.CountAsync()));
            Assert.Contains(env.Logger.Entries,
                e => e.Level == LogLevel.Warning && e.Message.Contains("required"));
            Assert.False(env.Logger.Contains(BootPassword));
        }

        [Fact]
        public async Task ShortPassword_Rejected_WithoutLoggingPassword()
        {
            using var env = new Env();
            const string shortPassword = "Sh0rt-7";

            var outcome = await env.RunAsync(BootName, shortPassword);

            Assert.Equal(BootstrapAdminOutcome.Rejected, outcome);
            Assert.Equal(0, await env.DbAsync(db => db.Users.CountAsync()));
            Assert.Contains(env.Logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Password"));
            Assert.False(env.Logger.Contains(shortPassword));
        }

        [Theory]
        [InlineData("ab")]
        [InlineData("bad name")]
        [InlineData("bad/name")]
        public async Task BadUserName_Rejected(string name)
        {
            using var env = new Env();

            Assert.Equal(BootstrapAdminOutcome.Rejected, await env.RunAsync(name, BootPassword));
            Assert.Equal(0, await env.DbAsync(db => db.Users.CountAsync()));
        }

        [Fact]
        public async Task TooLongUserName_Rejected()
        {
            using var env = new Env();

            Assert.Equal(BootstrapAdminOutcome.Rejected, await env.RunAsync(new string('a', 51), BootPassword));
            Assert.Equal(0, await env.DbAsync(db => db.Users.CountAsync()));
        }

        [Fact]
        public async Task NameTakenByNonAdmin_DifferentCase_NotPromoted()
        {
            using var env = new Env();
            await env.AddUserAsync(BootName.ToUpperInvariant(), "User", "original-hash");

            var outcome = await env.RunAsync(BootName, BootPassword);

            Assert.Equal(BootstrapAdminOutcome.SkippedUserNameTaken, outcome);
            var user = Assert.Single(await env.DbAsync(db => db.Users.ToListAsync()));
            Assert.Equal("User", user.Role);
            Assert.Equal("original-hash", user.PasswordHash);
            Assert.Contains(env.Logger.Entries, e => e.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task BrokenDatabase_ReturnsFailed_DoesNotThrow_AndDoesNotLogPassword()
        {
            var env = new Env();
            env.Services.Dispose();

            var outcome = await env.RunAsync(BootName, BootPassword);

            Assert.Equal(BootstrapAdminOutcome.Failed, outcome);
            Assert.False(env.Logger.Contains(BootPassword));
        }

        // ---- parity with POST api/account/register ----

        public static IEnumerable<object[]> ParityCases() => new[]
        {
            new object[] { "alice", "12345678", true },
            new object[] { "a.b_c-d", "P@ssw0rd!", true },
            new object[] { "abc", "12345678", true },
            new object[] { new string('a', 50), "12345678", true },
            new object[] { "ab", "12345678", false },
            new object[] { new string('a', 51), "12345678", false },
            new object[] { "bad name", "12345678", false },
            new object[] { "bad/name", "12345678", false },
            new object[] { "alice", "1234567", false },
        };

        [Theory]
        [MemberData(nameof(ParityCases))]
        public async Task Rules_MatchRegistration(string name, string password, bool valid)
        {
            using var env = new Env();
            var helper = await env.RunAsync(name, password);
            Assert.Equal(valid ? BootstrapAdminOutcome.Created : BootstrapAdminOutcome.Rejected, helper);

            using var factory = new ApiFactory();
            var response = await factory.CreateClient().PostAsJsonAsync("api/account/register",
                new { userName = name, password });
            Assert.Equal(valid ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
        }

        // ---- host level ----

        public class NoAdminFactory : ApiFactory
        {
            protected override bool SeedAdmin => false;

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.UseSetting("Bootstrap:AdminUserName", BootName);
                builder.UseSetting("Bootstrap:AdminPassword", BootPassword);
            }
        }

        public class AdminExistsFactory : ApiFactory
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.UseSetting("Bootstrap:AdminUserName", BootName);
                builder.UseSetting("Bootstrap:AdminPassword", BootPassword);
            }
        }

        public class MigrateNoAdminFactory : NoAdminFactory
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.UseSetting("Database:MigrateOnStartup", "true");
            }
        }

        private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string name, string password) =>
            await client.PostAsJsonAsync("api/account/login", new { userName = name, password });

        [Fact]
        public async Task Host_NoAdmin_BootstrapUserCanLogIn_AsAdmin()
        {
            using var factory = new NoAdminFactory();
            var client = factory.CreateClient();

            var response = await LoginAsync(client, BootName, BootPassword);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Admin", json.GetProperty("role").GetString());
        }

        [Fact]
        public async Task Host_AdminSeeded_BootstrapSkipped()
        {
            using var factory = new AdminExistsFactory();
            var client = factory.CreateClient();

            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, BootName, BootPassword)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, ApiFactory.UserName, ApiFactory.Password)).StatusCode);
        }

        [Fact]
        public async Task Host_MigrateOnStartupTrue_InMemory_StillCreatesAdmin()
        {
            using var factory = new MigrateNoAdminFactory();
            var client = factory.CreateClient();

            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, BootName, BootPassword)).StatusCode);
        }
    }
}
