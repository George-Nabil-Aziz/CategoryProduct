using CategoryProduct.API.Services;

namespace CategoryProduct.API.Tests
{
    public class PaymentTokenRulesTests
    {
        [Theory]
        [InlineData("tok_visa")]
        [InlineData("tok_a")]
        [InlineData("tok_decline")]
        public void ValidTokens_AreAccepted(string token)
        {
            Assert.True(PaymentTokenRules.TryNormalizeToken(token, out var value, out var error));
            Assert.Equal(token, value);
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        public void SixtyCharSuffix_IsAccepted_AndTokenIsTrimmed()
        {
            var token = "tok_" + new string('a', 60);
            Assert.True(PaymentTokenRules.TryNormalizeToken("  " + token + " ", out var value, out _));
            Assert.Equal(token, value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("visa")]
        [InlineData("tok_")]
        [InlineData("tok_a b")]
        [InlineData("tok_é")]
        public void InvalidTokens_AreRejected_WithTheGenericMessage(string? token)
        {
            Assert.False(PaymentTokenRules.TryNormalizeToken(token, out var value, out var error));
            Assert.Equal(string.Empty, value);
            Assert.Equal(PaymentTokenRules.InvalidTokenMessage, error);
        }

        [Fact]
        public void SixtyOneCharSuffix_IsRejected()
        {
            var token = "tok_" + new string('a', 61);
            Assert.False(PaymentTokenRules.TryNormalizeToken(token, out _, out var error));
            Assert.Equal(PaymentTokenRules.InvalidTokenMessage, error);
        }

        [Theory]
        [InlineData("4242424242424242")]
        [InlineData("4242 4242 4242 4242")]
        [InlineData("4242-4242-4242-4242")]
        [InlineData("424242424242")]
        public void CardLikeValues_GetTheCardNumberMessage_AndNeverEchoTheInput(string pan)
        {
            Assert.False(PaymentTokenRules.TryNormalizeToken(pan, out var value, out var error));
            Assert.Equal(string.Empty, value);
            Assert.Equal(PaymentTokenRules.CardNumberMessage, error);
            Assert.DoesNotContain("4242", error);
        }

        [Fact]
        public void ElevenDigits_IsNotCardLike_ButStillInvalid()
        {
            Assert.False(PaymentTokenRules.TryNormalizeToken("42424242424", out _, out var error));
            Assert.Equal(PaymentTokenRules.InvalidTokenMessage, error);
        }

        [Fact]
        public void Messages_NeverContainTheInput()
        {
            Assert.False(PaymentTokenRules.TryNormalizeToken("secret value", out _, out var error));
            Assert.DoesNotContain("secret", error);
            Assert.False(PaymentTokenRules.TryNormalizeKey("bad key!", out _, out var keyError));
            Assert.DoesNotContain("bad", keyError);
        }

        [Fact]
        public void Key_Null_IsGenerated_As32Hex()
        {
            Assert.True(PaymentTokenRules.TryNormalizeKey(null, out var a, out _));
            Assert.True(PaymentTokenRules.TryNormalizeKey("  ", out var b, out _));
            Assert.Matches("^[0-9a-f]{32}$", a);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Key_Valid_IsKept_AndTrimmed()
        {
            Assert.True(PaymentTokenRules.TryNormalizeKey(" order_1-abc ", out var value, out _));
            Assert.Equal("order_1-abc", value);
            Assert.True(PaymentTokenRules.TryNormalizeKey(new string('k', 64), out _, out _));
        }

        [Theory]
        [InlineData("has space")]
        [InlineData("semi;colon")]
        [InlineData("café")]
        public void Key_BadCharacters_AreRejected(string key)
        {
            Assert.False(PaymentTokenRules.TryNormalizeKey(key, out _, out var error));
            Assert.Equal(PaymentTokenRules.InvalidKeyMessage, error);
        }

        [Fact]
        public void Key_65Chars_IsRejected() =>
            Assert.False(PaymentTokenRules.TryNormalizeKey(new string('k', 65), out _, out _));
    }
}
