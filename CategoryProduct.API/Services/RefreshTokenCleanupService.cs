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

        /// <summary>Deletes tokens expired or revoked before now - RetentionDays; returns the number removed.</summary>
        public async Task<int> PurgeAsync(DateTime utcNow, CancellationToken ct = default)
        {
            var days = _configuration.GetValue("RefreshTokenCleanup:RetentionDays", 7);
            if (days <= 0) days = 7;
            var cutoff = utcNow.AddDays(-days);

            var old = await _context.RefreshTokens
                .Where(r => r.ExpiresAtUtc < cutoff || (r.RevokedAtUtc != null && r.RevokedAtUtc < cutoff))
                .ToListAsync(ct);
            _context.RefreshTokens.RemoveRange(old);
            await _context.SaveChangesAsync(ct);
            return old.Count;
        }
    }
}
