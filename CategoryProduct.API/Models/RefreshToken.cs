namespace CategoryProduct.API.Models
{
    public class RefreshToken
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
        public string? ReplacedByTokenHash { get; set; }
        public Guid FamilyId { get; set; }
        public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

        public void Revoke(DateTime utcNow)
        {
            RevokedAtUtc = utcNow;
            ConcurrencyStamp = Guid.NewGuid();
        }
    }
}
