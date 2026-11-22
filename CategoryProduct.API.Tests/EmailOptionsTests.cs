using CategoryProduct.API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CategoryProduct.API.Tests
{
    public class EmailOptionsTests
    {
        private sealed class FakeEnv : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = "Test";
            public string ApplicationName { get; set; } = "Test";
            public string ContentRootPath { get; set; } = Path.GetTempPath();
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private sealed class CollectingProvider : ILoggerProvider
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();
            public ILogger CreateLogger(string categoryName) => new Logger(this);
            public void Dispose() { }

            private sealed class Logger(CollectingProvider owner) : ILogger
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => true;
                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    lock (owner.Entries) owner.Entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }

        private static IConfiguration Config(params (string Key, string? Value)[] values) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
                .Build();

        private static EmailOptions Bind(IConfiguration configuration)
        {
            var options = new EmailOptions();
            EmailOptions.Bind(configuration.GetSection("Email"), options);
            return options;
        }

        private static (IEmailSender Sender, CollectingProvider Log) Build(params (string, string?)[] values)
        {
            var log = new CollectingProvider();
            var services = new ServiceCollection()
                .AddLogging(b => b.AddProvider(log))
                .AddSingleton<IHostEnvironment>(new FakeEnv())
                .AddEmailNotifications(Config(values))
                .BuildServiceProvider();
            return (services.GetRequiredService<IEmailSender>(), log);
        }

        [Fact]
        public void MissingSection_UsesDefaults()
        {
            var options = Bind(Config());
            Assert.False(options.Enabled);
            Assert.Equal("Pickup", options.Provider);
            Assert.Equal("noreply@categoryproduct.local", options.EffectiveFrom);
            Assert.Equal("App_Data/email-outbox", options.EffectivePickupDirectory);
            Assert.Equal(587, options.Smtp.Port);
            Assert.True(options.Smtp.EnableSsl);
            Assert.True(string.IsNullOrEmpty(options.Smtp.Host));
        }

        [Fact]
        public void EmptyStrings_ForBoolAndInt_BindAsDefaults()
        {
            var options = Bind(Config(
                ("Email:Enabled", ""),
                ("Email:Provider", ""),
                ("Email:From", ""),
                ("Email:PickupDirectory", ""),
                ("Email:Smtp:Host", ""),
                ("Email:Smtp:Port", ""),
                ("Email:Smtp:UserName", ""),
                ("Email:Smtp:Password", ""),
                ("Email:Smtp:EnableSsl", "")));

            Assert.False(options.Enabled);
            Assert.Equal(587, options.Smtp.Port);
            Assert.True(options.Smtp.EnableSsl);
            Assert.Equal(EmailProvider.Pickup, options.ResolveProvider(out var problem));
            Assert.Null(problem);
            Assert.Equal("noreply@categoryproduct.local", options.EffectiveFrom);
            Assert.Equal("App_Data/email-outbox", options.EffectivePickupDirectory);
        }

        [Fact]
        public void ConfiguredValues_Bind()
        {
            var options = Bind(Config(
                ("Email:Enabled", "true"),
                ("Email:Provider", "Smtp"),
                ("Email:From", "shop@example.com"),
                ("Email:Smtp:Host", "smtp.example.test"),
                ("Email:Smtp:Port", "2525"),
                ("Email:Smtp:EnableSsl", "false")));

            Assert.True(options.Enabled);
            Assert.Equal("shop@example.com", options.EffectiveFrom);
            Assert.Equal(2525, options.Smtp.Port);
            Assert.False(options.Smtp.EnableSsl);
            Assert.Equal(EmailProvider.Smtp, options.ResolveProvider(out _));
        }

        [Theory]
        [InlineData("Pickup", null, typeof(PickupDirectoryEmailSender), false)]
        [InlineData("pickup", null, typeof(PickupDirectoryEmailSender), false)]
        [InlineData(null, null, typeof(PickupDirectoryEmailSender), false)]
        [InlineData("Smtp", "smtp.example.test", typeof(SmtpEmailSender), false)]
        [InlineData("SMTP", "smtp.example.test", typeof(SmtpEmailSender), false)]
        [InlineData("sMtP", "smtp.example.test", typeof(SmtpEmailSender), false)]
        [InlineData("Smtp", "", typeof(PickupDirectoryEmailSender), true)]
        [InlineData("Smtp", null, typeof(PickupDirectoryEmailSender), true)]
        [InlineData("Smtp", "   ", typeof(PickupDirectoryEmailSender), true)]
        [InlineData("Carrier-Pigeon", "smtp.example.test", typeof(PickupDirectoryEmailSender), true)]
        public void ProviderSelection(string? provider, string? host, Type expected, bool errorLogged)
        {
            var (sender, log) = Build(("Email:Provider", provider), ("Email:Smtp:Host", host));

            Assert.IsType(expected, sender);
            Assert.Equal(errorLogged, log.Entries.Any(e => e.Level == LogLevel.Error));
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("Email provider"));
        }

        [Fact]
        public void Selection_Logs_NeverThePassword()
        {
            const string password = "pw-should-never-be-logged-123";
            var (_, log) = Build(
                ("Email:Provider", "Smtp"),
                ("Email:Smtp:Host", "smtp.example.test"),
                ("Email:Smtp:UserName", "mailer-user"),
                ("Email:Smtp:Password", password));

            Assert.All(log.Entries, e =>
            {
                Assert.DoesNotContain(password, e.Message);
                Assert.DoesNotContain("mailer-user", e.Message);
            });
        }

        [Fact]
        public void SelectionIsMadeOnce()
        {
            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<IHostEnvironment>(new FakeEnv())
                .AddEmailNotifications(Config(("Email:Provider", "Pickup")))
                .BuildServiceProvider();

            Assert.Same(services.GetRequiredService<IEmailSender>(), services.GetRequiredService<IEmailSender>());
            Assert.Same(services.GetRequiredService<EmailQueue>(), services.GetRequiredService<EmailQueue>());
        }
    }
}
