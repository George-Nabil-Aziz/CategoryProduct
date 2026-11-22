using System.Text.RegularExpressions;

namespace CategoryProduct.API.Services
{
    /// <summary>
    /// Validation of the opaque payment token and the idempotency key. Messages are static: the input is never echoed.
    /// </summary>
    public static partial class PaymentTokenRules
    {
        public const string CardNumberMessage = "Card numbers are not accepted. Send a payment token.";
        public const string InvalidTokenMessage = "Payment token is invalid.";
        public const string InvalidKeyMessage = "Idempotency key is invalid.";

        [GeneratedRegex("^tok_[A-Za-z0-9_]{1,60}$")]
        private static partial Regex TokenPattern();

        [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
        private static partial Regex KeyPattern();

        public static bool IsWellFormedToken(string? token) => token != null && TokenPattern().IsMatch(token);

        /// <summary>Returns true and the trimmed token, or false and a static error message.</summary>
        public static bool TryNormalizeToken(string? token, out string value, out string error)
        {
            value = string.Empty;
            error = InvalidTokenMessage;
            if (string.IsNullOrWhiteSpace(token)) return false;
            var trimmed = token.Trim();
            if (LooksLikeCardNumber(trimmed))
            {
                error = CardNumberMessage;
                return false;
            }
            if (!TokenPattern().IsMatch(trimmed)) return false;
            value = trimmed;
            error = string.Empty;
            return true;
        }

        /// <summary>Null or blank gives a generated key (32 hex chars); otherwise the key must match the pattern.</summary>
        public static bool TryNormalizeKey(string? key, out string value, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                value = Guid.NewGuid().ToString("N");
                return true;
            }
            var trimmed = key.Trim();
            if (!KeyPattern().IsMatch(trimmed))
            {
                value = string.Empty;
                error = InvalidKeyMessage;
                return false;
            }
            value = trimmed;
            return true;
        }

        private static bool LooksLikeCardNumber(string s)
        {
            var digits = 0;
            foreach (var c in s)
            {
                if (c == ' ' || c == '-') continue;
                if (c < '0' || c > '9') return false;
                digits++;
            }
            return digits is >= 12 and <= 19;
        }
    }
}
