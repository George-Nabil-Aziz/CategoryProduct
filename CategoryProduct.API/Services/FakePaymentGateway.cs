using System.Security.Cryptography;
using System.Text;

namespace CategoryProduct.API.Services
{
    /// <summary>
    /// Deterministic, stateless stand-in for a payment provider (no I/O, clock or randomness). No real money moves.
    /// The token only decides the outcome and is never logged, stored or returned.
    /// </summary>
    public sealed class FakePaymentGateway : IPaymentGateway
    {
        public const string ChargePrefix = "fake_ch_";
        public const string RefundFailChargePrefix = "fake_chf_";
        public const string RefundPrefix = "fake_re_";

        public Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken = default)
        {
            if (!PaymentTokenRules.IsWellFormedToken(request.PaymentToken))
                return Task.FromResult(new ChargeResult(false, null, "invalid_token"));

            switch (request.PaymentToken)
            {
                case "tok_decline":
                    return Task.FromResult(new ChargeResult(false, null, "card_declined"));
                case "tok_insufficient_funds":
                    return Task.FromResult(new ChargeResult(false, null, "insufficient_funds"));
                case "tok_error":
                    throw new InvalidOperationException("Simulated provider failure.");
                case "tok_refund_fail":
                    return Task.FromResult(new ChargeResult(true, RefundFailChargePrefix + Hash(request.IdempotencyKey), null));
                default:
                    return Task.FromResult(new ChargeResult(true, ChargePrefix + Hash(request.IdempotencyKey), null));
            }
        }

        public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
        {
            var reference = request.ProviderReference ?? string.Empty;
            if (reference.StartsWith(RefundFailChargePrefix, StringComparison.Ordinal))
                return Task.FromResult(new RefundResult(false, null, "refund_failed"));
            if (!reference.StartsWith(ChargePrefix, StringComparison.Ordinal))
                return Task.FromResult(new RefundResult(false, null, "unknown_reference"));
            return Task.FromResult(new RefundResult(true, RefundPrefix + Hash(request.IdempotencyKey), null));
        }

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];
    }
}
