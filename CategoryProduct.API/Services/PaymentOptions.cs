using System.Text.RegularExpressions;

namespace CategoryProduct.API.Services
{
    /// <summary>
    /// Payment settings. Only a provider name and a currency label exist: this feature has no keys or secrets.
    /// Empty values mean "unset" (defaults), matching how compose passes empty variables.
    /// </summary>
    public sealed class PaymentOptions
    {
        public const string SectionName = "Payments";
        public const string FakeProvider = "Fake";
        public const string DefaultCurrency = "USD";

        public string Provider { get; set; } = FakeProvider;
        public string Currency { get; set; } = DefaultCurrency;

        /// <summary>Reads, normalizes and validates the section. Throws <see cref="InvalidOperationException"/>.</summary>
        public static PaymentOptions Create(IConfiguration section)
        {
            var options = new PaymentOptions
            {
                Provider = section["Provider"] ?? string.Empty,
                Currency = section["Currency"] ?? string.Empty
            };
            options.Normalize();
            options.Validate();
            return options;
        }

        public void Normalize()
        {
            Provider = string.IsNullOrWhiteSpace(Provider) ? FakeProvider : Provider.Trim();
            Currency = string.IsNullOrWhiteSpace(Currency) ? DefaultCurrency : Currency.Trim().ToUpperInvariant();
            if (Provider.Equals(FakeProvider, StringComparison.OrdinalIgnoreCase)) Provider = FakeProvider;
        }

        public void Validate()
        {
            if (!Regex.IsMatch(Currency, "^[A-Z]{3}$"))
                throw new InvalidOperationException(
                    "Invalid Payments:Currency. Use a three-letter ISO 4217 code such as USD.");
            if (!Provider.Equals(FakeProvider, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unknown Payments:Provider '{Provider}'. Supported: {FakeProvider}.");
        }
    }
}
