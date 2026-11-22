namespace CategoryProduct.API.Models
{
    public class PasswordResetToken
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? UsedAtUtc { get; set; }
        public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

        /// <summary>Marks the token consumed or invalidated and rotates the concurrency stamp.</summary>
        public void MarkUsed(DateTime utcNow)
        {
            UsedAtUtc = utcNow;
            ConcurrencyStamp = Guid.NewGuid();
        }
    }
}
