using Microsoft.EntityFrameworkCore;
using CategoryProduct.API.Models;

namespace CategoryProduct.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<User> Users { get; set; }

        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<StockMovement> StockMovements { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RefreshToken>(b =>
            {
                b.HasIndex(r => r.TokenHash).IsUnique();
                b.Property(r => r.TokenHash).HasMaxLength(128).IsRequired();
                b.Property(r => r.ReplacedByTokenHash).HasMaxLength(128);
                b.Property(r => r.ConcurrencyStamp).IsConcurrencyToken();
                b.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Product>().Property(p => p.ImageFileName).HasMaxLength(100);
            modelBuilder.Entity<Product>().Property(p => p.ConcurrencyStamp).IsConcurrencyToken();

            modelBuilder.Entity<StockMovement>(b =>
            {
                b.Property(m => m.Reason).HasConversion<string>().HasMaxLength(30).IsRequired();
                b.Property(m => m.Note).HasMaxLength(200);
                b.HasOne(m => m.Product).WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.SetNull);
                b.HasIndex(m => new { m.ProductId, m.CreatedAtUtc });
            });

            modelBuilder.Entity<User>()
                .HasIndex(u => u.UserName)
                .IsUnique();

            modelBuilder.Entity<User>()
                .Property(u => u.Role)
                .HasMaxLength(20)
                .IsRequired()
                .HasDefaultValue("User");
        }
    }
}
