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
        public const string Password = "P@ssw0rd!";
        public const int SeedCategoryCount = 3;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Jwt:Key", "test-key-test-key-test-key-test-key-1234567890");
            builder.UseSetting("Jwt:Issuer", "CategoryProduct.API");
            builder.UseSetting("Jwt:Audience", "CategoryProduct.Client");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "unused");

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
                var user = new User { UserName = UserName, PasswordHash = "" };
                user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
                db.Users.Add(user);
                for (var i = 1; i <= SeedCategoryCount; i++)
                    db.Categories.Add(new Category { Name = $"Category {i}" });
                db.SaveChanges();
            });
        }
    }
}
