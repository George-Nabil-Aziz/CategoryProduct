using CategoryProduct.API.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace CategoryProduct.API.Tests
{
    public class PaymentOptionsTests
    {
        private static PaymentOptions Create(params (string Key, string? Value)[] values)
        {
            var dict = values.ToDictionary(v => "Payments:" + v.Key, v => v.Value);
            var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
            return PaymentOptions.Create(config.GetSection("Payments"));
        }

        [Fact]
        public void MissingSection_UsesDefaults()
        {
            var o = Create();
            Assert.Equal("Fake", o.Provider);
            Assert.Equal("USD", o.Currency);
        }

        [Theory]
        [InlineData("fake")]
        [InlineData("FAKE")]
        [InlineData(" Fake ")]
        public void Provider_IsCaseInsensitive_AndNormalized(string value) =>
            Assert.Equal("Fake", Create(("Provider", value)).Provider);

        [Fact]
        public void EmptyValues_BindToDefaults()
        {
            var o = Create(("Provider", ""), ("Currency", ""));
            Assert.Equal("Fake", o.Provider);
            Assert.Equal("USD", o.Currency);
        }

        [Fact]
        public void Currency_IsUpperCased() => Assert.Equal("EUR", Create(("Currency", "eur")).Currency);

        [Theory]
        [InlineData("US")]
        [InlineData("USDX")]
        [InlineData("12A")]
        public void InvalidCurrency_Throws(string value) =>
            Assert.Throws<InvalidOperationException>(() => Create(("Currency", value)));

        [Fact]
        public void UnknownProvider_Throws_AndNamesTheSupportedOne()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Create(("Provider", "Stripe")));
            Assert.Contains("Supported: Fake", ex.Message);
            Assert.Contains("'Stripe'", ex.Message);
        }

        private sealed class StripeFactory : ApiFactory
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.UseSetting("Payments:Provider", "Stripe");
            }
        }

        [Fact]
        public void AppWithUnknownProvider_FailsToStart()
        {
            using var factory = new StripeFactory();
            var ex = Record.Exception(() => factory.CreateClient());
            Assert.NotNull(ex);
            var found = false;
            for (var e = ex; e != null; e = e.InnerException)
                if (e.Message.Contains("Unknown Payments:Provider 'Stripe'. Supported: Fake.")) found = true;
            Assert.True(found, "Expected the unknown provider startup error, got: " + ex);
        }
    }
}
