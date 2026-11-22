using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using CategoryProduct.API.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace CategoryProduct.API.Tests
{
    public class ApiFactory : WebApplicationFactory<Program>
    {
        public const string UserName = "tester";
        public const string PlainUserName = "plainuser";
        public const string Password = "P@ssw0rd!";
        public const int SeedCategoryCount = 3;

        protected readonly Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot DbRoot = new();
        public readonly string DbName = Guid.NewGuid().ToString();

        public string ImageStoragePath { get; } =
            Path.Combine(Path.GetTempPath(), "cp-images-" + Guid.NewGuid().ToString("N"));

        public string EmailPickupPath { get; } =
            Path.Combine(Path.GetTempPath(), "cp-email-" + Guid.NewGuid().ToString("N"));

        public RecordingEmailSender Emails { get; } = new();

        public EmailQueue EmailQueue => Services.GetRequiredService<EmailQueue>();

        /// <summary>Controls the TimeProvider used by the password reset code (starts at real UTC now).</summary>
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                try { if (Directory.Exists(ImageStoragePath)) Directory.Delete(ImageStoragePath, true); }
                catch (IOException) { }
                try { if (Directory.Exists(EmailPickupPath)) Directory.Delete(EmailPickupPath, true); }
                catch (IOException) { }
            }
        }

        protected virtual int LoginPermitLimit => 1000;
        protected virtual int RefreshPermitLimit => 1000;
        protected virtual int LogoutPermitLimit => 1000;
        protected virtual int ForgotPasswordPermitLimit => 1000;
        protected virtual int PasswordPermitLimit => 1000;
        protected virtual bool EmailEnabled => true;
        protected virtual string PublicMvcBaseUrl => "https://mvc.test";
        protected virtual int LowStockThreshold => 5;
        protected virtual int RefreshReuseGraceSeconds => 0;
        protected virtual bool SeedAdmin => true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ProductImages:StoragePath", ImageStoragePath);
            builder.UseSetting("Jwt:Key", "test-key-test-key-test-key-test-key-1234567890");
            builder.UseSetting("Jwt:Issuer", "CategoryProduct.API");
            builder.UseSetting("Jwt:Audience", "CategoryProduct.Client");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "unused");
            builder.UseSetting("Jwt:RefreshReuseGraceSeconds", RefreshReuseGraceSeconds.ToString());
            builder.UseSetting("Inventory:LowStockThreshold", LowStockThreshold.ToString());
            builder.UseSetting("RateLimiting:LoginPermitLimit", LoginPermitLimit.ToString());
            builder.UseSetting("RateLimiting:RegisterPermitLimit", "1000");
            builder.UseSetting("RateLimiting:RefreshPermitLimit", RefreshPermitLimit.ToString());
            builder.UseSetting("RateLimiting:LogoutPermitLimit", LogoutPermitLimit.ToString());
            builder.UseSetting("RateLimiting:ProfilePermitLimit", "1000");
            builder.UseSetting("RateLimiting:ForgotPasswordPermitLimit", ForgotPasswordPermitLimit.ToString());
            builder.UseSetting("RateLimiting:PasswordPermitLimit", PasswordPermitLimit.ToString());
            builder.UseSetting("App:PublicMvcBaseUrl", PublicMvcBaseUrl);
            builder.UseSetting("Email:Enabled", EmailEnabled ? "true" : "false");
            builder.UseSetting("Email:PickupDirectory", EmailPickupPath);

            builder.ConfigureServices(services =>
            {
                var descriptors = services
                    .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                        || d.ServiceType == typeof(DbContextOptions)
                        || d.ServiceType.IsGenericType
                            && d.ServiceType.GetGenericTypeDefinition().Name.StartsWith("IDbContextOptionsConfiguration")
                            && d.ServiceType.GetGenericArguments()[0] == typeof(ApplicationDbContext))
                    .ToList();
                foreach (var d in descriptors)
                    services.Remove(d);

                services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(DbName, DbRoot));

                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);

                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Emails);

                using var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                if (SeedAdmin)
                {
                    var user = new User { UserName = UserName, PasswordHash = "", Role = "Admin" };
                    user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
                    db.Users.Add(user);
                }
                var plain = new User { UserName = PlainUserName, PasswordHash = "", Role = "User" };
                plain.PasswordHash = new PasswordHasher<User>().HashPassword(plain, Password);
                db.Users.Add(plain);
                for (var i = 1; i <= SeedCategoryCount; i++)
                    db.Categories.Add(new Category { Name = $"Category {i}" });
                db.SaveChanges();
                db.Products.AddRange(
                    new Product { Name = "Widget A", Price = 30, Quantity = 1, CategoryId = 1 },
                    new Product { Name = "Widget B", Price = 10, Quantity = 1, CategoryId = 1 },
                    new Product { Name = "Gadget", Price = 20, Quantity = 1, CategoryId = 2 });
                db.SaveChanges();
            });
        }
    }
}
