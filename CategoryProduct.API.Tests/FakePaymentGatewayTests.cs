using CategoryProduct.API.Services;

namespace CategoryProduct.API.Tests
{
    public class FakePaymentGatewayTests
    {
        private readonly FakePaymentGateway _gw = new();

        private static ChargeRequest Charge(string token, string key = "k1", decimal amount = 10m) =>
            new(1, amount, "USD", key, token);

        [Theory]
        [InlineData("tok_visa")]
        [InlineData("tok_mastercard")]
        [InlineData("tok_anything_else")]
        public async Task OtherWellFormedTokens_Approve(string token)
        {
            var r = await _gw.ChargeAsync(Charge(token));
            Assert.True(r.Approved);
            Assert.StartsWith("fake_ch_", r.ProviderReference);
            Assert.Null(r.FailureCode);
        }

        [Theory]
        [InlineData("tok_decline", "card_declined")]
        [InlineData("tok_insufficient_funds", "insufficient_funds")]
        public async Task DeclineTokens_Decline_WithCodes(string token, string code)
        {
            var r = await _gw.ChargeAsync(Charge(token));
            Assert.False(r.Approved);
            Assert.Null(r.ProviderReference);
            Assert.Equal(code, r.FailureCode);
        }

        [Fact]
        public async Task TokError_Throws() =>
            await Assert.ThrowsAsync<InvalidOperationException>(() => _gw.ChargeAsync(Charge("tok_error")));

        [Fact]
        public async Task SameKey_GivesSameReference_DifferentKeys_Differ()
        {
            var a = await _gw.ChargeAsync(Charge("tok_visa", "same"));
            var b = await _gw.ChargeAsync(Charge("tok_visa", "same"));
            var c = await _gw.ChargeAsync(Charge("tok_visa", "other"));
            Assert.Equal(a.ProviderReference, b.ProviderReference);
            Assert.NotEqual(a.ProviderReference, c.ProviderReference);
            Assert.Equal("fake_ch_".Length + 24, a.ProviderReference!.Length);
        }

        [Fact]
        public async Task TokRefundFail_UsesTheMarkedPrefix_AndItsRefundFails()
        {
            var charge = await _gw.ChargeAsync(Charge("tok_refund_fail", "rf"));
            Assert.True(charge.Approved);
            Assert.StartsWith("fake_chf_", charge.ProviderReference);
            var refund = await _gw.RefundAsync(new RefundRequest(charge.ProviderReference!, 10m, "USD", "refund-1"));
            Assert.False(refund.Succeeded);
            Assert.Equal("refund_failed", refund.FailureCode);
        }

        [Fact]
        public async Task NormalRefund_Succeeds_AndIsDeterministicPerRefundKey()
        {
            var charge = await _gw.ChargeAsync(Charge("tok_visa"));
            var a = await _gw.RefundAsync(new RefundRequest(charge.ProviderReference!, 10m, "USD", "refund-7"));
            var b = await _gw.RefundAsync(new RefundRequest(charge.ProviderReference!, 10m, "USD", "refund-7"));
            var c = await _gw.RefundAsync(new RefundRequest(charge.ProviderReference!, 10m, "USD", "refund-8"));
            Assert.True(a.Succeeded);
            Assert.StartsWith("fake_re_", a.RefundReference);
            Assert.Equal(a.RefundReference, b.RefundReference);
            Assert.NotEqual(a.RefundReference, c.RefundReference);
        }

        [Fact]
        public async Task UnknownReference_FailsToRefund()
        {
            var r = await _gw.RefundAsync(new RefundRequest("nope_123", 10m, "USD", "refund-1"));
            Assert.False(r.Succeeded);
            Assert.Equal("unknown_reference", r.FailureCode);
        }

        [Theory]
        [InlineData("4242424242424242")]
        [InlineData("visa")]
        [InlineData("")]
        public async Task InvalidToken_IsDeclined_AndNeverEchoed(string token)
        {
            var r = await _gw.ChargeAsync(Charge(token));
            Assert.False(r.Approved);
            Assert.Equal("invalid_token", r.FailureCode);
        }

        [Fact]
        public async Task ConcurrentCalls_GiveIdenticalResults()
        {
            var results = new ChargeResult[50];
            await Parallel.ForEachAsync(Enumerable.Range(0, results.Length), async (i, _) =>
                results[i] = await _gw.ChargeAsync(Charge("tok_visa", "parallel")));
            Assert.All(results, r => Assert.Equal(results[0], r));
        }
    }
}
