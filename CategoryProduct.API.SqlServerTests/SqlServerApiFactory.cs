using System.Net.Http.Json;
using System.Text.Json;
using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CategoryProduct.API.SqlServerTests
{
    public static class SqlServerEnv
    {
        public const string VariableName = "SQLSERVER_TEST_CONNECTION";

        public static string BuildConnectionString(string databaseName)
        {
            var raw = Environment.GetEnvironmentVariable(VariableName);
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException(
                    "Environment variable SQLSERVER_TEST_CONNECTION is not set. Point it at a SQL Server where the login can create and drop databases, e.g. Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=True;");
            var b = new SqlConnectionStringBuilder(raw) { InitialCatalog = databaseName };
            if (b.ConnectTimeout < 60) b.ConnectTimeout = 60;
            return b.ConnectionString;
        }

        public static string NewDatabaseName() => "CategoryProductTest_" + Guid.NewGuid().ToString("N");

        public static DbContextOptions<ApplicationDbContext> Options(string connectionString) =>
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(connectionString, o => o.CommandTimeout(120))
                .Options;
    }

    public class SqlServerApiFactory : WebApplicationFactory<Program>
    {
        public const string Password = "P@ssw0rd!long";

        public string DatabaseName { get; } = SqlServerEnv.NewDatabaseName();
        public string ConnectionString { get; }

        private int _counter;

        public SqlServerApiFactory()
        {
            ConnectionString = SqlServerEnv.BuildConnectionString(DatabaseName);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Jwt:Key", "sqlserver-test-key-sqlserver-test-key-1234567890");
            builder.UseSetting("Jwt:Issuer", "CategoryProduct.API");
            builder.UseSetting("Jwt:Audience", "CategoryProduct.Client");
            builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.UseSetting("Hosting:UseHttpsRedirection", "false");
            builder.UseSetting("ProductImages:StoragePath", ImageDir);
            builder.UseSetting("RateLimiting:LoginPermitLimit", "1000");
            builder.UseSetting("RateLimiting:RegisterPermitLimit", "1000");
            builder.UseSetting("RateLimiting:RefreshPermitLimit", "1000");
            builder.UseSetting("RateLimiting:LogoutPermitLimit", "1000");

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

                services.AddDbContext<ApplicationDbContext>(o =>
                    o.UseSqlServer(ConnectionString, s => s.CommandTimeout(120)));

                // Create the schema before the host starts serving.
                using var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Database.MigrateAsync().GetAwaiter().GetResult();
            });
        }

        private string ImageDir => Path.Combine(Path.GetTempPath(), "cp-sql-images-" + DatabaseName);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            try
            {
                using var db = new ApplicationDbContext(SqlServerEnv.Options(ConnectionString));
                db.Database.EnsureDeletedAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // Best effort: the CI SQL Server container is ephemeral.
            }
            try
            {
                if (Directory.Exists(ImageDir)) Directory.Delete(ImageDir, true);
            }
            catch (IOException) { }
        }

        public string UniqueName(string prefix) =>
            $"{prefix}{Interlocked.Increment(ref _counter)}_{Guid.NewGuid().ToString("N")[..8]}";

        public async Task<int> CreateUserAsync(string name, string role)
        {
            var id = 0;
            await WithDbAsync(async db =>
            {
                var user = new User { UserName = name, PasswordHash = "", Role = role };
                user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
                db.Users.Add(user);
                await db.SaveChangesAsync();
                id = user.Id;
            });
            return id;
        }

        public async Task<(string Access, string Refresh)> LoginAsync(string name)
        {
            var r = await CreateClient().PostAsJsonAsync("api/account/login", new { userName = name, password = Password });
            r.EnsureSuccessStatusCode();
            var json = await r.Content.ReadFromJsonAsync<JsonElement>();
            return (json.GetProperty("token").GetString()!, json.GetProperty("refreshToken").GetString()!);
        }

        public async Task<HttpClient> LoggedInClientAsync(string name)
        {
            var (access, _) = await LoginAsync(name);
            var c = CreateClient();
            c.Timeout = TimeSpan.FromMinutes(2);
            c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
            return c;
        }

        public async Task<(int Id, HttpClient Client)> NewUserAsync(string role, string prefix = "u")
        {
            var name = UniqueName(prefix);
            var id = await CreateUserAsync(name, role);
            return (id, await LoggedInClientAsync(name));
        }

        public async Task WithDbAsync(Func<ApplicationDbContext, Task> action)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await action(db);
        }

        public async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await action(db);
        }

        public async Task<int> SeedProductAsync(int quantity, decimal price = 10)
        {
            return await WithDbAsync(async db =>
            {
                var category = new Category { Name = UniqueName("cat") };
                db.Categories.Add(category);
                await db.SaveChangesAsync();
                var product = new Product { Name = UniqueName("prod"), Price = price, Quantity = quantity, CategoryId = category.Id };
                db.Products.Add(product);
                await db.SaveChangesAsync();
                return product.Id;
            });
        }

        public async Task AddCartLineAsync(int userId, int productId, int quantity)
        {
            await WithDbAsync(async db =>
            {
                db.CartItems.Add(new CartItem { UserId = userId, ProductId = productId, Quantity = quantity });
                await db.SaveChangesAsync();
            });
        }
    }

    internal static class Parallel8
    {
        /// <summary>Runs the calls on separate tasks, released together through a gate.</summary>
        public static async Task<HttpResponseMessage[]> RunAsync(int count, Func<int, Task<HttpResponseMessage>> call)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, count)
                .Select(i => Task.Run(async () => { await gate.Task; return await call(i); }))
                .ToArray();
            await Task.Delay(300);
            gate.SetResult();
            return await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromMinutes(3));
        }
    }
}
