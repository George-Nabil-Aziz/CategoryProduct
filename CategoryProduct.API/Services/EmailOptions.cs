using Microsoft.Extensions.Options;

namespace CategoryProduct.API.Services
{
    public enum EmailProvider { Pickup, Smtp }

    public sealed class SmtpOptions
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public bool EnableSsl { get; set; } = true;
    }

    public sealed class EmailOptions
    {
        public const string SectionName = "Email";
        public const string DefaultFrom = "noreply@categoryproduct.local";
        public const string DefaultPickupDirectory = "App_Data/email-outbox";

        public bool Enabled { get; set; }
        public string? Provider { get; set; } = "Pickup";
        public string? From { get; set; } = DefaultFrom;
        public string? PickupDirectory { get; set; } = DefaultPickupDirectory;
        public SmtpOptions Smtp { get; set; } = new();

        // An empty string means "unset": use the default.
        public string EffectiveFrom => string.IsNullOrWhiteSpace(From) ? DefaultFrom : From.Trim();
        public string EffectivePickupDirectory =>
            string.IsNullOrWhiteSpace(PickupDirectory) ? DefaultPickupDirectory : PickupDirectory.Trim();

        /// <summary>
        /// Reads the section by hand: the generic binder throws on an empty string for a bool or int,
        /// but compose-style deployments can pass empty values. Empty or unparsable means "unset" (default).
        /// </summary>
        public static void Bind(IConfiguration section, EmailOptions options)
        {
            options.Enabled = ReadBool(section["Enabled"], options.Enabled);
            options.Provider = ReadString(section["Provider"], options.Provider);
            options.From = ReadString(section["From"], options.From);
            options.PickupDirectory = ReadString(section["PickupDirectory"], options.PickupDirectory);
            var smtp = section.GetSection("Smtp");
            options.Smtp.Host = ReadString(smtp["Host"], options.Smtp.Host);
            options.Smtp.Port = int.TryParse(smtp["Port"], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535
                ? port : options.Smtp.Port;
            options.Smtp.UserName = ReadString(smtp["UserName"], options.Smtp.UserName);
            options.Smtp.Password = ReadString(smtp["Password"], options.Smtp.Password);
            options.Smtp.EnableSsl = ReadBool(smtp["EnableSsl"], options.Smtp.EnableSsl);
        }

        private static bool ReadBool(string? value, bool fallback) =>
            bool.TryParse(value?.Trim(), out var b) ? b : fallback;

        private static string? ReadString(string? value, string? fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;

        /// <summary>
        /// Chooses the provider. Smtp without a host, or an unknown name, falls back to Pickup and
        /// sets <paramref name="problem"/> (to be logged as an Error). Never contains secrets.
        /// </summary>
        public EmailProvider ResolveProvider(out string? problem)
        {
            problem = null;
            var name = string.IsNullOrWhiteSpace(Provider) ? "Pickup" : Provider.Trim();
            if (name.Equals("Pickup", StringComparison.OrdinalIgnoreCase))
                return EmailProvider.Pickup;
            if (name.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(Smtp.Host))
                    return EmailProvider.Smtp;
                problem = "Email:Provider is Smtp but Email:Smtp:Host is empty; using the Pickup provider.";
                return EmailProvider.Pickup;
            }
            problem = $"Email:Provider '{name}' is not supported (use Pickup or Smtp); using the Pickup provider.";
            return EmailProvider.Pickup;
        }
    }

    public static class EmailServiceCollectionExtensions
    {
        /// <summary>Binds <see cref="EmailOptions"/>, selects the sender once, registers the queue and worker.</summary>
        public static IServiceCollection AddEmailNotifications(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<EmailOptions>(o => EmailOptions.Bind(configuration.GetSection(EmailOptions.SectionName), o));
            services.AddSingleton<IEmailSender>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<EmailOptions>>().Value;
                var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("CategoryProduct.API.Email");
                var provider = options.ResolveProvider(out var problem);
                if (problem != null) logger.LogError("{Problem}", problem);
                logger.LogInformation("Email provider {Provider}; notifications enabled: {Enabled}",
                    provider, options.Enabled);
                return provider == EmailProvider.Smtp
                    ? ActivatorUtilities.CreateInstance<SmtpEmailSender>(sp)
                    : ActivatorUtilities.CreateInstance<PickupDirectoryEmailSender>(sp);
            });
            services.AddSingleton<EmailQueue>();
            services.AddHostedService<EmailDispatchWorker>();
            return services;
        }
    }
}
