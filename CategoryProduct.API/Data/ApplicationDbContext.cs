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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RefreshToken>(b =>
            {
                b.HasIndex(r => r.TokenHash).IsUnique();
                b.Property(r => r.TokenHash).HasMaxLength(128).IsRequired();
                b.Property(r => r.ReplacedByTokenHash).HasMaxLength(128);
                b.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
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
