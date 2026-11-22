using CategoryProduct.API.Services;

namespace CategoryProduct.API.Tests
{
    public class EmailAddressRulesTests
    {
        [Theory]
        [InlineData("a@b.co", "a@b.co")]
        [InlineData("Alice@Example.COM", "alice@example.com")]
        [InlineData("  a@b.co  ", "a@b.co")]
        [InlineData("first.last+tag@sub.example.org", "first.last+tag@sub.example.org")]
        public void Valid_IsNormalized(string input, string expected)
        {
            Assert.True(EmailAddressRules.TryNormalize(input, out var normalized, out var error));
            Assert.Equal(expected, normalized);
            Assert.Null(error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void None_IsNullWithoutError(string? input)
        {
            Assert.True(EmailAddressRules.TryNormalize(input, out var normalized, out var error));
            Assert.Null(normalized);
            Assert.Null(error);
        }

        [Theory]
        [InlineData("a")]
        [InlineData("a@b")]
        [InlineData("@b.co")]
        [InlineData("a@")]
        [InlineData("A <a@b.co>")]
        [InlineData("a b@c.co")]
        [InlineData("a@b .co")]
        [InlineData("a\rb@c.co")]
        [InlineData("a\nb@c.co")]
        [InlineData("a\tb@c.co")]
        [InlineData("a@b.co\r\nBcc: x@y.zz")]
        [InlineData("a@@b.co")]
        public void Invalid_ReturnsError(string input)
        {
            Assert.False(EmailAddressRules.TryNormalize(input, out var normalized, out var error));
            Assert.Null(normalized);
            Assert.Equal(EmailAddressRules.InvalidMessage, error);
        }

        [Fact]
        public void TooLong_IsInvalid_AndMaxLengthIsValid()
        {
            var tooLong = new string('a', 255 - "@b.co".Length) + "@b.co";
            Assert.Equal(255, tooLong.Length);
            Assert.False(EmailAddressRules.TryNormalize(tooLong, out _, out _));

            var max = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 56) + ".co";
            Assert.True(max.Length <= 254);
            Assert.True(EmailAddressRules.TryNormalize(max, out var n, out _));
            Assert.NotNull(n);
        }

        [Theory]
        [InlineData("alice@example.com", "a***@example.com")]
        [InlineData("x@y.zz", "x***@y.zz")]
        [InlineData("noat", "***")]
        [InlineData("", "***")]
        [InlineData(null, "***")]
        public void Mask_HidesLocalPart(string? input, string expected) =>
            Assert.Equal(expected, EmailAddressRules.Mask(input));
    }
}
