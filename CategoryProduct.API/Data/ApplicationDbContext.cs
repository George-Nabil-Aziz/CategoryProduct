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
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
        public DbSet<StockMovement> StockMovements { get; set; }
        public DbSet<CartItem> CartItems { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Payment> Payments { get; set; }

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

            modelBuilder.Entity<PasswordResetToken>(b =>
            {
                b.HasIndex(t => t.TokenHash).IsUnique();
                b.HasIndex(t => t.UserId);
                b.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
                b.Property(t => t.ConcurrencyStamp).IsConcurrencyToken();
                b.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
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

            modelBuilder.Entity<CartItem>(b =>
            {
                b.HasIndex(c => new { c.UserId, c.ProductId }).IsUnique();
                b.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(c => c.Product).WithMany().HasForeignKey(c => c.ProductId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Order>(b =>
            {
                b.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(o => o.Total).HasColumnType("decimal(18,2)");
                b.Property(o => o.PaymentStatus).HasConversion<string>().HasMaxLength(20).IsRequired()
                    .HasDefaultValue(OrderPaymentStatus.Unpaid);
                b.Property(o => o.ConcurrencyStamp).IsConcurrencyToken();
                b.HasOne(o => o.User).WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Restrict);
                b.HasMany(o => o.Items).WithOne(i => i.Order).HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
                b.HasIndex(o => new { o.UserId, o.CreatedAtUtc });
            });

            modelBuilder.Entity<Payment>(b =>
            {
                b.Property(p => p.Amount).HasColumnType("decimal(18,2)");
                b.Property(p => p.Currency).HasMaxLength(3).IsRequired();
                b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(p => p.ProviderReference).HasMaxLength(100);
                b.Property(p => p.IdempotencyKey).HasMaxLength(64).IsRequired();
                b.Property(p => p.FailureReason).HasMaxLength(200);
                b.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
                b.HasOne(p => p.Order).WithMany(o => o.Payments).HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(p => p.IdempotencyKey).IsUnique();
                b.HasIndex(p => p.OrderId).IsUnique().HasDatabaseName("IX_Payments_OrderId_Active")
                    .HasFilter("[Status] IN ('Pending', 'Succeeded')");
                b.HasIndex(p => new { p.OrderId, p.CreatedAtUtc });
            });

            modelBuilder.Entity<OrderItem>(b =>
            {
                b.Property(i => i.ProductName).HasMaxLength(100).IsRequired();
                b.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
                b.Property(i => i.LineTotal).HasColumnType("decimal(18,2)");
                b.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<User>()
                .HasIndex(u => u.UserName)
                .IsUnique();

            modelBuilder.Entity<User>(b =>
            {
                b.Property(u => u.Email).HasMaxLength(254);
                b.HasIndex(u => u.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
            });

            modelBuilder.Entity<User>()
                .Property(u => u.Role)
                .HasMaxLength(20)
                .IsRequired()
                .HasDefaultValue("User");
        }
    }
}
