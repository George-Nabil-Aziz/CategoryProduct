namespace CategoryProduct.API.Services
{
    public sealed record EmailMessage(string To, string Subject, string Body)
    {
        public const int MaxSubjectLength = 200;

        /// <summary>Null when valid; otherwise a short reason that never contains the body.</summary>
        public string? Validate()
        {
            if (string.IsNullOrEmpty(To)
                || !EmailAddressRules.IsStructurallySafe(To)
                || !EmailAddressRules.TryNormalize(To, out var normalized, out _)
                || normalized is null)
                return "Recipient is not a valid address.";
            if (string.IsNullOrWhiteSpace(Subject))
                return "Subject is empty.";
            if (Subject.Length > MaxSubjectLength)
                return "Subject is too long.";
            if (Subject.Contains('\r') || Subject.Contains('\n'))
                return "Subject contains a line break.";
            if (string.IsNullOrWhiteSpace(Body))
                return "Body is empty.";
            return null;
        }
    }
}
