using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CategoryProduct.API.Tests
{
    public class ApiFactory : WebApplicationFactory<Program>
    {
        public const string UserName = "tester";
        public const string PlainUserName = "plainuser";
        public const string Password = "P@ssw0rd!";
        public const int SeedCategoryCount = 3;

        protected virtual int LoginPermitLimit => 1000;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Jwt:Key", "test-key-test-key-test-key-test-key-1234567890");
            builder.UseSetting("Jwt:Issuer", "CategoryProduct.API");
            builder.UseSetting("Jwt:Audience", "CategoryProduct.Client");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "unused");
            builder.UseSetting("RateLimiting:LoginPermitLimit", LoginPermitLimit.ToString());

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

                var dbName = Guid.NewGuid().ToString();
                services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));

                using var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = new User { UserName = UserName, PasswordHash = "", Role = "Admin" };
                user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
                db.Users.Add(user);
                var plain = new User { UserName = PlainUserName, PasswordHash = "", Role = "User" };
                plain.PasswordHash = new PasswordHasher<User>().HashPassword(plain, Password);
                db.Users.Add(plain);
                for (var i = 1; i <= SeedCategoryCount; i++)
                    db.Categories.Add(new Category { Name = $"Category {i}" });
                db.SaveChanges();
            });
        }
    }
}
