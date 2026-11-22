using System.Net.Mail;

namespace CategoryProduct.API.Services
{
    /// <summary>Normalizes and validates email addresses. Never throws; returns an error message instead.</summary>
    public static class EmailAddressRules
    {
        public const int MaxLength = 254;
        public const string InvalidMessage = "Email is not a valid address.";

        /// <summary>
        /// Trims the input. Null, empty or whitespace means "no email" (true, normalized null).
        /// Otherwise returns true with the lower-cased address, or false with an error message.
        /// </summary>
        public static bool TryNormalize(string? input, out string? normalized, out string? error)
        {
            normalized = null;
            error = null;
            if (string.IsNullOrWhiteSpace(input))
                return true;

            var trimmed = input.Trim();
            if (!IsStructurallySafe(trimmed)
                || !MailAddress.TryCreate(trimmed, out var parsed)
                || parsed.Address != trimmed
                || !parsed.Host.Contains('.'))
            {
                error = InvalidMessage;
                return false;
            }

            normalized = trimmed.ToLowerInvariant();
            return true;
        }

        /// <summary>True when the text is non-empty, within the length limit and has no whitespace or control characters.</summary>
        public static bool IsStructurallySafe(string value)
        {
            if (value.Length == 0 || value.Length > MaxLength) return false;
            foreach (var c in value)
                if (char.IsWhiteSpace(c) || char.IsControl(c)) return false;
            return true;
        }

        /// <summary>Masks the local part for logs: a***@example.com.</summary>
        public static string Mask(string? address)
        {
            if (string.IsNullOrEmpty(address)) return "***";
            var at = address.LastIndexOf('@');
            if (at <= 0) return "***";
            return address[0] + "***" + address[at..];
        }
    }
}
