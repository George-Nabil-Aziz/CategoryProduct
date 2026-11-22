using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using CategoryProduct.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CategoryProduct.API.Tests
{
    internal static class InventoryHelper
    {
        public static async Task<int> CreateProductAsync(HttpClient admin, int quantity = 10, int categoryId = 3, string? name = null)
        {
            var r = await admin.PostAsJsonAsync("api/product",
                new { name = name ?? AuthHelper.Unique("inv"), price = 5, quantity, categoryId });
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        }

        public static async Task<JsonElement> GetProductAsync(HttpClient c, int id)
        {
            var r = await c.GetAsync($"api/product/{id}");
            r.EnsureSuccessStatusCode();
            return await r.Content.ReadFromJsonAsync<JsonElement>();
        }

        public static async Task<int> QuantityAsync(HttpClient c, int id) =>
            (await GetProductAsync(c, id)).GetProperty("quantity").GetInt32();

        public static Task<HttpResponseMessage> MoveAsync(HttpClient c, int id, int change, string reason = "Restock", string? note = null) =>
            c.PostAsJsonAsync($"api/product/{id}/stock-movements", new { quantityChange = change, reason, note });

        public static async Task<JsonElement> MovementsAsync(HttpClient c, int id, string query = "")
        {
            var r = await c.GetAsync($"api/product/{id}/stock-movements{query}");
            r.EnsureSuccessStatusCode();
            return await r.Content.ReadFromJsonAsync<JsonElement>();
        }

        public static async Task<int> UserIdAsync(ApiFactory f, string userName)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync();
        }

        public static async Task<List<string>> ErrorKeysAsync(HttpResponseMessage r)
        {
            var json = await r.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
        }

        public static int MovementCount(ApiFactory f, int productId)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return db.StockMovements.Count(m => m.ProductId == productId);
        }
    }

    public class InventoryTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        public InventoryTests(ApiFactory factory) => _factory = factory;

        private Task<HttpClient> AdminAsync() => AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);

        // ---------- US1 success ----------
        [Fact]
        public async Task Restock_Returns201_AndUpdatesProduct()
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 5);
            var before = DateTime.UtcNow.AddMinutes(-1);

            var r = await InventoryHelper.MoveAsync(admin, id, 10, "Restock", "Supplier delivery");

            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            Assert.Null(r.Headers.Location);
            var body = await r.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(15, body.GetProperty("quantityAfter").GetInt32());
            Assert.Equal(10, body.GetProperty("quantityChange").GetInt32());
            Assert.Equal("Restock", body.GetProperty("reason").GetString());
            Assert.Equal("Supplier delivery", body.GetProperty("note").GetString());
            Assert.Equal(await InventoryHelper.UserIdAsync(_factory, ApiFactory.UserName), body.GetProperty("userId").GetInt32());
            Assert.Equal(ApiFactory.UserName, body.GetProperty("userName").GetString());
            var created = body.GetProperty("createdAtUtc").GetDateTime();
            Assert.True(created > before && created < DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(15, await InventoryHelper.QuantityAsync(admin, id));
        }

        [Theory]
        [InlineData("Damage", -3, 7)]
        [InlineData("Return", 2, 12)]
        [InlineData("Adjustment", -10, 0)]
        [InlineData("restock", 1, 11)]
        public async Task AdminReasons_Accepted_CaseInsensitive(string reason, int change, int expected)
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 10);
            var r = await InventoryHelper.MoveAsync(admin, id, change, reason);
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            var body = await r.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(expected, body.GetProperty("quantityAfter").GetInt32());
            Assert.Equal(char.ToUpper(reason[0]) + reason[1..].ToLower(), body.GetProperty("reason").GetString());
        }

        [Fact]
        public async Task Note_IsTrimmed_AndWhitespaceBecomesNull()
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 10);
            var a = await (await InventoryHelper.MoveAsync(admin, id, 1, "Restock", "  hello  ")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("hello", a.GetProperty("note").GetString());
            var b = await (await InventoryHelper.MoveAsync(admin, id, 1, "Restock", "   ")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Null, b.GetProperty("note").ValueKind);
        }

        // ---------- US1 400 ----------
        [Fact]
        public async Task InvalidQuantityChange_Returns400()
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 10);
            foreach (var change in new[] { 0, 1000001, -1000001 })
            {
                var r = await InventoryHelper.MoveAsync(admin, id, change);
                Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
                Assert.Contains("QuantityChange", await InventoryHelper.ErrorKeysAsync(r));
            }
            Assert.Equal(10, await InventoryHelper.QuantityAsync(admin, id));
            Assert.Equal(1, InventoryHelper.MovementCount(_factory, id)); // only Initial
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Bogus")]
        [InlineData("3")]
        [InlineData("Sale")]
        [InlineData("OrderCancelled")]
        [InlineData("Initial")]
        public async Task InvalidReason_Returns400(string? reason)
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 10);
            var r = await admin.PostAsJsonAsync($"api/product/{id}/stock-movements", new { quantityChange = 1, reason });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("Reason", await InventoryHelper.ErrorKeysAsync(r));
            Assert.Equal(10, await InventoryHelper.QuantityAsync(admin, id));
            Assert.Equal(1, InventoryHelper.MovementCount(_factory, id));
        }

        [Fact]
        public async Task NoteTooLong_Returns400()
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 10);
            var r = await InventoryHelper.MoveAsync(admin, id, 1, "Restock", new string('x', 201));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("Note", await InventoryHelper.ErrorKeysAsync(r));
            Assert.Equal(10, await InventoryHelper.QuantityAsync(admin, id));
        }

        // ---------- US1 status ----------
        [Fact]
        public async Task Anonymous_Returns401_PlainUser_Returns403()
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 10);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await InventoryHelper.MoveAsync(_factory.CreateClient(), id, 1)).StatusCode);
            var plain = await AuthHelper.LoggedInAsync(_factory, ApiFactory.PlainUserName);
            Assert.Equal(HttpStatusCode.Forbidden, (await InventoryHelper.MoveAsync(plain, id, 1)).StatusCode);
        }

        [Fact]
        public async Task UnknownProduct_Returns404_NothingWritten()
        {
            var admin = await AdminAsync();
            var r = await InventoryHelper.MoveAsync(admin, 987654, 1);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Equal(0, InventoryHelper.MovementCount(_factory, 987654));
        }

        [Fact]
        public async Task BelowZero_Returns409_ExactlyZero_Returns201()
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 3);
            var r = await InventoryHelper.MoveAsync(admin, id, -4, "Damage");
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var problem = await r.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(409, problem.GetProperty("status").GetInt32());
            Assert.Equal(3, await InventoryHelper.QuantityAsync(admin, id));
            Assert.Equal(1, InventoryHelper.MovementCount(_factory, id));

            Assert.Equal(HttpStatusCode.Created, (await InventoryHelper.MoveAsync(admin, id, -3, "Damage")).StatusCode);
            Assert.Equal(0, await InventoryHelper.QuantityAsync(admin, id));
        }

        [Fact]
        public async Task Overflow_Returns409()
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 1);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var p = await db.Products.SingleAsync(x => x.Id == id);
                p.Quantity = int.MaxValue - 1;
                await db.SaveChangesAsync();
            }
            var r = await InventoryHelper.MoveAsync(admin, id, 5);
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal(int.MaxValue - 1, await InventoryHelper.QuantityAsync(admin, id));
        }

        // ---------- US1 unit ----------
        private static (ApplicationDbContext db, InventoryService svc) UnitService(ILogger<InventoryService>? logger = null, int threshold = 5)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var db = new ApplicationDbContext(options);
            var svc = new InventoryService(db, Options.Create(new InventoryOptions { LowStockThreshold = threshold }),
                logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<InventoryService>.Instance);
            return (db, svc);
        }

        private static async Task<int> SeedAsync(ApplicationDbContext db, int quantity)
        {
            var p = new Product { Name = "Unit", Price = 1, Quantity = quantity, CategoryId = 1 };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            return p.Id;
        }

        [Fact]
        public async Task Service_Ok_SetsQuantityStampAndMovement()
        {
            var (db, svc) = UnitService();
            var id = await SeedAsync(db, 5);
            var stamp = (await db.Products.FindAsync(id))!.ConcurrencyStamp;

            var result = await svc.ApplyMovementAsync(id, 4, StockMovementReason.Restock, "n", null);

            Assert.Equal(MovementStatus.Ok, result.Status);
            Assert.Equal(9, result.Movement!.QuantityAfter);
            var p = (await db.Products.FindAsync(id))!;
            Assert.Equal(9, p.Quantity);
            Assert.NotEqual(stamp, p.ConcurrencyStamp);
            Assert.Equal(1, await db.StockMovements.CountAsync());
        }

        [Fact]
        public async Task Service_InsufficientStock_LeavesEverythingUnchanged()
        {
            var (db, svc) = UnitService();
            var id = await SeedAsync(db, 2);
            var stamp = (await db.Products.FindAsync(id))!.ConcurrencyStamp;

            var result = await svc.ApplyMovementAsync(id, -3, StockMovementReason.Damage, null, null);

            Assert.Equal(MovementStatus.InsufficientStock, result.Status);
            var p = (await db.Products.FindAsync(id))!;
            Assert.Equal(2, p.Quantity);
            Assert.Equal(stamp, p.ConcurrencyStamp);
            Assert.Equal(0, await db.StockMovements.CountAsync());
        }

        [Fact]
        public async Task Service_UnknownId_ReturnsNotFound()
        {
            var (_, svc) = UnitService();
            var result = await svc.ApplyMovementAsync(42, 1, StockMovementReason.Restock, null, null);
            Assert.Equal(MovementStatus.NotFound, result.Status);
        }

        [Fact]
        public async Task Service_StageMovement_DoesNotSave_AndSeveralCanShareOneSave()
        {
            var (db, svc) = UnitService();
            var a = await SeedAsync(db, 5);
            var b = await SeedAsync(db, 5);

            Assert.Equal(MovementStatus.Ok, (await svc.StageMovementAsync(a, -2, StockMovementReason.Sale, null, null)).Status);
            Assert.Equal(MovementStatus.Ok, (await svc.StageMovementAsync(b, -1, StockMovementReason.Sale, null, null)).Status);

            Assert.Equal(0, await db.StockMovements.AsNoTracking().CountAsync());

            await db.SaveChangesAsync();
            Assert.Equal(2, await db.StockMovements.CountAsync());
            Assert.Equal(3, (await db.Products.FindAsync(a))!.Quantity);
            Assert.Equal(4, (await db.Products.FindAsync(b))!.Quantity);
        }

        // ---------- US1 concurrency ----------
        public class RaceInterceptor : SaveChangesInterceptor
        {
            public DbContextOptions<ApplicationDbContext>? Options;
            private int _remaining;
            public int Fired;
            public void Arm(int times) { Interlocked.Exchange(ref _remaining, times); Interlocked.Exchange(ref Fired, 0); }
            public void Disarm() => Interlocked.Exchange(ref _remaining, 0);

            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                var entry = eventData.Context?.ChangeTracker.Entries<Product>()
                    .FirstOrDefault(e => e.State == EntityState.Modified || e.State == EntityState.Deleted);
                if (entry != null && Volatile.Read(ref _remaining) > 0)
                {
                    Interlocked.Decrement(ref _remaining);
                    Interlocked.Increment(ref Fired);
                    var id = entry.Entity.Id;
                    using var other = new ApplicationDbContext(Options!);
                    var p = await other.Products.SingleAsync(x => x.Id == id, cancellationToken);
                    p.Quantity += 100;
                    p.ConcurrencyStamp = Guid.NewGuid();
                    await other.SaveChangesAsync(cancellationToken);
                }
                return result;
            }
        }

        public class RacingFactory : ApiFactory
        {
            public readonly RaceInterceptor Interceptor = new();

            protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.ConfigureServices(services =>
                {
                    Interceptor.Options = new DbContextOptionsBuilder<ApplicationDbContext>()
                        .UseInMemoryDatabase(DbName, DbRoot).Options;
                    services.ConfigureDbContext<ApplicationDbContext>(o => o.AddInterceptors(Interceptor));
                });
            }
        }
    }

    public class InventoryConcurrencyTests : IClassFixture<InventoryTests.RacingFactory>
    {
        private readonly InventoryTests.RacingFactory _factory;
        public InventoryConcurrencyTests(InventoryTests.RacingFactory factory) => _factory = factory;

        [Fact]
        public async Task RaceOnce_RetriesAndSucceeds()
        {
            var admin = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);
            var id = await InventoryHelper.CreateProductAsync(admin, 10);
            _factory.Interceptor.Arm(1);
            var r = await InventoryHelper.MoveAsync(admin, id, -3, "Damage");
            _factory.Interceptor.Disarm();

            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            Assert.Equal(1, _factory.Interceptor.Fired);
            Assert.Equal(10 + 100 - 3, await InventoryHelper.QuantityAsync(admin, id));
            var body = await r.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(107, body.GetProperty("quantityAfter").GetInt32());
        }

        [Fact]
        public async Task RaceAlways_Returns409_After3Attempts()
        {
            var admin = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);
            var id = await InventoryHelper.CreateProductAsync(admin, 10);
            _factory.Interceptor.Arm(100);
            var r = await InventoryHelper.MoveAsync(admin, id, -3, "Damage");
            var fired = _factory.Interceptor.Fired;
            _factory.Interceptor.Disarm();

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal(3, fired);
            Assert.Equal(310, await InventoryHelper.QuantityAsync(admin, id));
            // InMemory is not transactional, so partial writes of the failed save cannot be asserted here (SQL Server rolls the whole save back).
        }

        // ---------- US4 ----------
        [Fact]
        public async Task Put_RacedByStockChange_Returns409()
        {
            var admin = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);
            var id = await InventoryHelper.CreateProductAsync(admin, 10);
            _factory.Interceptor.Arm(1);
            var r = await admin.PutAsJsonAsync($"api/product/{id}",
                new { name = "Renamed", price = 5, quantity = 10, categoryId = 3 });
            _factory.Interceptor.Disarm();
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal(409, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32());
        }

        [Fact]
        public async Task Delete_RacedByStockChange_Returns409()
        {
            var admin = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);
            var id = await InventoryHelper.CreateProductAsync(admin, 10);
            _factory.Interceptor.Arm(1);
            var r = await admin.DeleteAsync($"api/product/{id}");
            _factory.Interceptor.Disarm();
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        }
    }

    public class InventoryHistoryTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        public InventoryHistoryTests(ApiFactory factory) => _factory = factory;
        private Task<HttpClient> AdminAsync() => AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);

        // ---------- US2 ----------
        [Fact]
        public async Task Create_WithQuantity_WritesInitialMovement()
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 7);
            var page = await InventoryHelper.MovementsAsync(admin, id);
            Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
            var m = page.GetProperty("items")[0];
            Assert.Equal("Initial", m.GetProperty("reason").GetString());
            Assert.Equal(7, m.GetProperty("quantityChange").GetInt32());
            Assert.Equal(7, m.GetProperty("quantityAfter").GetInt32());
            Assert.Equal(await InventoryHelper.UserIdAsync(_factory, ApiFactory.UserName), m.GetProperty("userId").GetInt32());
        }

        [Fact]
        public async Task Create_WithZeroQuantity_NoMovement_AndSeededHaveNone()
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 0);
            Assert.Equal(0, (await InventoryHelper.MovementsAsync(admin, id)).GetProperty("totalCount").GetInt32());
            for (var seeded = 1; seeded <= 3; seeded++)
                Assert.Equal(0, (await InventoryHelper.MovementsAsync(admin, seeded)).GetProperty("totalCount").GetInt32());
        }

        [Fact]
        public async Task History_NewestFirst_AndSumMatchesQuantity()
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 7);
            await InventoryHelper.MoveAsync(admin, id, 3);
            await InventoryHelper.MoveAsync(admin, id, -2, "Damage");
            var page = await InventoryHelper.MovementsAsync(admin, id);
            var items = page.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(3, items.Count);
            Assert.Equal(new[] { "Damage", "Restock", "Initial" }, items.Select(i => i.GetProperty("reason").GetString()).ToArray());
            var ids = items.Select(i => i.GetProperty("id").GetInt32()).ToList();
            Assert.Equal(ids.OrderByDescending(x => x).ToList(), ids);
            Assert.Equal(await InventoryHelper.QuantityAsync(admin, id), items.Sum(i => i.GetProperty("quantityChange").GetInt32()));
        }

        [Fact]
        public async Task History_Paging_AndClamping()
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 1);
            for (var i = 0; i < 4; i++) await InventoryHelper.MoveAsync(admin, id, 1);

            var p2 = await InventoryHelper.MovementsAsync(admin, id, "?pageSize=2&page=2");
            Assert.Equal(5, p2.GetProperty("totalCount").GetInt32());
            Assert.Equal(3, p2.GetProperty("totalPages").GetInt32());
            Assert.Equal(2, p2.GetProperty("items").GetArrayLength());
            Assert.Equal(3, p2.GetProperty("items")[0].GetProperty("quantityAfter").GetInt32());
            var p3 = await InventoryHelper.MovementsAsync(admin, id, "?pageSize=2&page=3");
            Assert.Equal(1, p3.GetProperty("items").GetArrayLength());
            Assert.Equal("Initial", p3.GetProperty("items")[0].GetProperty("reason").GetString());

            Assert.Equal(1, (await InventoryHelper.MovementsAsync(admin, id, "?pageSize=0")).GetProperty("pageSize").GetInt32());
            Assert.Equal(50, (await InventoryHelper.MovementsAsync(admin, id, "?pageSize=500")).GetProperty("pageSize").GetInt32());
            Assert.Equal(1, (await InventoryHelper.MovementsAsync(admin, id, "?page=0")).GetProperty("page").GetInt32());
        }

        [Fact]
        public async Task History_404_401_403()
        {
            var admin = await AdminAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("api/product/987654/stock-movements")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("api/product/1/stock-movements")).StatusCode);
            var plain = await AuthHelper.LoggedInAsync(_factory, ApiFactory.PlainUserName);
            Assert.Equal(HttpStatusCode.Forbidden, (await plain.GetAsync("api/product/1/stock-movements")).StatusCode);
        }

        // ---------- US3 flag ----------
        [Fact]
        public async Task IsLowStock_FlagBoundaries_InSingleListAndCategory()
        {
            var admin = await AdminAsync();
            var lowId = await InventoryHelper.CreateProductAsync(admin, 4, 3);
            var edgeId = await InventoryHelper.CreateProductAsync(admin, 5, 3);
            var highId = await InventoryHelper.CreateProductAsync(admin, 6, 3);

            Assert.True((await InventoryHelper.GetProductAsync(admin, lowId)).GetProperty("isLowStock").GetBoolean());
            Assert.False((await InventoryHelper.GetProductAsync(admin, edgeId)).GetProperty("isLowStock").GetBoolean());
            Assert.False((await InventoryHelper.GetProductAsync(admin, highId)).GetProperty("isLowStock").GetBoolean());

            var cat = await (await admin.GetAsync("api/category/3/products")).Content.ReadFromJsonAsync<JsonElement>();
            bool CatFlag(int id) => cat.EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == id).GetProperty("isLowStock").GetBoolean();
            Assert.True(CatFlag(lowId));
            Assert.False(CatFlag(edgeId));

            var list = await (await admin.GetAsync($"api/product?categoryId=3&pageSize=50")).Content.ReadFromJsonAsync<JsonElement>();
            var items = list.GetProperty("items").EnumerateArray().ToList();
            Assert.True(items.Single(p => p.GetProperty("id").GetInt32() == lowId).GetProperty("isLowStock").GetBoolean());
            Assert.False(items.Single(p => p.GetProperty("id").GetInt32() == highId).GetProperty("isLowStock").GetBoolean());

            var p = await InventoryHelper.GetProductAsync(admin, lowId);
            foreach (var key in new[] { "id", "name", "price", "quantity", "categoryId" })
                Assert.True(p.TryGetProperty(key, out _), key);
        }

        [Fact]
        public async Task IsLowStock_FlipsAfterRestock_AndPostPutCarryFlag()
        {
            var admin = await AdminAsync();
            var post = await admin.PostAsJsonAsync("api/product", new { name = AuthHelper.Unique("flag"), price = 5, quantity = 4, categoryId = 3 });
            var created = await post.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(created.GetProperty("isLowStock").GetBoolean());
            var id = created.GetProperty("id").GetInt32();

            var put = await admin.PutAsJsonAsync($"api/product/{id}", new { name = "Renamed flag", price = 5, quantity = 4, categoryId = 3 });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            Assert.True((await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isLowStock").GetBoolean());

            await InventoryHelper.MoveAsync(admin, id, 1);
            Assert.False((await InventoryHelper.GetProductAsync(admin, id)).GetProperty("isLowStock").GetBoolean());
        }

        // ---------- US4 PUT ----------
        [Fact]
        public async Task Put_DifferentQuantity_Returns400_NothingChanged()
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 10, 3, AuthHelper.Unique("put"));
            var before = await InventoryHelper.GetProductAsync(admin, id);

            var r = await admin.PutAsJsonAsync($"api/product/{id}", new { name = "Changed name", price = 99, quantity = 11, categoryId = 2 });

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            var json = await r.Content.ReadFromJsonAsync<JsonElement>();
            var errors = json.GetProperty("errors");
            var key = errors.EnumerateObject().Single(p => p.Name.Equals("Quantity", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Use stock movements to change quantity.", key.Value[0].GetString());
            var after = await InventoryHelper.GetProductAsync(admin, id);
            Assert.Equal(before.GetProperty("name").GetString(), after.GetProperty("name").GetString());
            Assert.Equal(before.GetProperty("price").GetDecimal(), after.GetProperty("price").GetDecimal());
            Assert.Equal(3, after.GetProperty("categoryId").GetInt32());
            Assert.Equal(10, after.GetProperty("quantity").GetInt32());
            Assert.Equal(1, InventoryHelper.MovementCount(_factory, id));
        }

        [Fact]
        public async Task Put_SameQuantity_NewName_Returns200_NoMovement()
        {
            var admin = await AdminAsync();
            var id = await InventoryHelper.CreateProductAsync(admin, 10);
            var r = await admin.PutAsJsonAsync($"api/product/{id}", new { name = "Brand new name", price = 7, quantity = 10, categoryId = 3 });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("Brand new name", (await InventoryHelper.GetProductAsync(admin, id)).GetProperty("name").GetString());
            Assert.Equal(1, InventoryHelper.MovementCount(_factory, id));
        }
    }

    public class LowStockEndpointTests
    {
        [Fact]
        public async Task LowStock_ListsOnlyLow_OrderedAndPaged()
        {
            using var f = new ApiFactory(); // seeded: 3 products with quantity 1
            var admin = await AuthHelper.LoggedInAsync(f, ApiFactory.UserName);
            var q4 = await InventoryHelper.CreateProductAsync(admin, 4);
            var q0 = await InventoryHelper.CreateProductAsync(admin, 0);
            var q5 = await InventoryHelper.CreateProductAsync(admin, 5);
            var q9 = await InventoryHelper.CreateProductAsync(admin, 9);

            var r = await admin.GetAsync("api/product/low-stock?pageSize=50");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var page = await r.Content.ReadFromJsonAsync<JsonElement>();
            var items = page.GetProperty("items").EnumerateArray().ToList();
            var ids = items.Select(i => i.GetProperty("id").GetInt32()).ToList();
            Assert.Equal(new[] { q0, 1, 2, 3, q4 }, ids);
            Assert.DoesNotContain(q5, ids);
            Assert.DoesNotContain(q9, ids);
            Assert.All(items, i => Assert.True(i.GetProperty("isLowStock").GetBoolean()));
            Assert.Equal(5, page.GetProperty("totalCount").GetInt32());

            var p2 = await (await admin.GetAsync("api/product/low-stock?pageSize=2&page=2")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(3, p2.GetProperty("totalPages").GetInt32());
            Assert.Equal(new[] { 2, 3 }, p2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToArray());
        }

        [Fact]
        public async Task LowStock_401_403()
        {
            using var f = new ApiFactory();
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("api/product/low-stock")).StatusCode);
            var plain = await AuthHelper.LoggedInAsync(f, ApiFactory.PlainUserName);
            Assert.Equal(HttpStatusCode.Forbidden, (await plain.GetAsync("api/product/low-stock")).StatusCode);
        }

        private class ZeroFactory : ApiFactory { protected override int LowStockThreshold => 0; }
        private class HundredFactory : ApiFactory { protected override int LowStockThreshold => 100; }

        [Fact]
        public async Task Threshold0_NothingFlagged_EmptyList()
        {
            using var f = new ZeroFactory();
            var admin = await AuthHelper.LoggedInAsync(f, ApiFactory.UserName);
            Assert.False((await InventoryHelper.GetProductAsync(admin, 1)).GetProperty("isLowStock").GetBoolean());
            var page = await (await admin.GetAsync("api/product/low-stock")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
            Assert.Equal(0, page.GetProperty("items").GetArrayLength());
        }

        [Fact]
        public async Task Threshold100_EverySeededProductFlagged()
        {
            using var f = new HundredFactory();
            var admin = await AuthHelper.LoggedInAsync(f, ApiFactory.UserName);
            for (var id = 1; id <= 3; id++)
                Assert.True((await InventoryHelper.GetProductAsync(admin, id)).GetProperty("isLowStock").GetBoolean());
            var page = await (await admin.GetAsync("api/product/low-stock")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
        }

        [Theory]
        [InlineData(-1, 5)]
        [InlineData(0, 0)]
        [InlineData(7, 7)]
        public void Options_Effective_MapsNegativeToFive(int configured, int expected) =>
            Assert.Equal(expected, new InventoryOptions { LowStockThreshold = configured }.Effective);
    }

    public class LowStockLogTests
    {
        private class CapturingLogger : ILogger<InventoryService>
        {
            public readonly List<(LogLevel Level, string Message)> Entries = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
        }

        private static async Task<(InventoryService svc, int id)> SetupAsync(CapturingLogger log, int quantity)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var p = new Product { Name = "Log", Price = 1, Quantity = quantity, CategoryId = 1 };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            return (new InventoryService(db, Options.Create(new InventoryOptions { LowStockThreshold = 5 }), log), p.Id);
        }

        [Fact]
        public async Task DecreaseCrossingBelowThreshold_LogsOneWarning()
        {
            var log = new CapturingLogger();
            var (svc, id) = await SetupAsync(log, 8);
            await svc.ApplyMovementAsync(id, -4, StockMovementReason.Damage, null, null);
            var warning = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
            Assert.Contains(id.ToString(), warning.Message);
            Assert.Contains("4", warning.Message);
            Assert.Contains("5", warning.Message);
        }

        [Fact]
        public async Task RestockOrDecreaseStayingAtThreshold_LogsNothing()
        {
            var log = new CapturingLogger();
            var (svc, id) = await SetupAsync(log, 2);
            await svc.ApplyMovementAsync(id, 10, StockMovementReason.Restock, null, null); // 12
            await svc.ApplyMovementAsync(id, -7, StockMovementReason.Damage, null, null);  // 5, not below
            Assert.DoesNotContain(log.Entries, e => e.Level == LogLevel.Warning);
        }
    }
}
