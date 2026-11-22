using CategoryProduct.API.Data;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.Services
{
    public class RefreshTokenCleanupService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public RefreshTokenCleanupService(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        /// <summary>Deletes refresh and password reset tokens expired, revoked or used before now - RetentionDays; returns the total removed.</summary>
        public async Task<int> PurgeAsync(DateTime utcNow, CancellationToken ct = default)
        {
            var days = _configuration.GetValue("RefreshTokenCleanup:RetentionDays", 7);
            if (days <= 0) days = 7;
            var cutoff = utcNow.AddDays(-days);

            var old = await _context.RefreshTokens
                .Where(r => r.ExpiresAtUtc < cutoff || (r.RevokedAtUtc != null && r.RevokedAtUtc < cutoff))
                .ToListAsync(ct);
            _context.RefreshTokens.RemoveRange(old);
            var oldResets = await _context.PasswordResetTokens
                .Where(t => t.ExpiresAtUtc < cutoff || (t.UsedAtUtc != null && t.UsedAtUtc < cutoff))
                .ToListAsync(ct);
            _context.PasswordResetTokens.RemoveRange(oldResets);
            await _context.SaveChangesAsync(ct);
            return old.Count + oldResets.Count;
        }
    }
}
