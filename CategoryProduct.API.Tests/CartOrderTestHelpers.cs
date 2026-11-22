using System.Net.Http.Json;
using System.Text.Json;
using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace CategoryProduct.API.Tests
{
    internal static class CartOrderTestHelpers
    {
        public const string UserPassword = "longenough1";

        public static async Task<int> CreateProductAsync(HttpClient admin, string? name = null, decimal price = 10, int quantity = 10)
        {
            var r = await admin.PostAsJsonAsync("api/product",
                new { name = name ?? AuthHelper.Unique("cart"), price, quantity, categoryId = 3 });
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        }

        public static Task<HttpResponseMessage> AddToCartAsync(HttpClient c, int productId, int quantity) =>
            c.PostAsJsonAsync("api/cart/items", new { productId, quantity });

        public static async Task AddOkAsync(HttpClient c, int productId, int quantity) =>
            (await AddToCartAsync(c, productId, quantity)).EnsureSuccessStatusCode();

        public static Task<HttpResponseMessage> CheckoutAsync(HttpClient c) => c.PostAsync("api/orders", null);

        public static async Task<JsonElement> JsonAsync(HttpResponseMessage r) =>
            await r.Content.ReadFromJsonAsync<JsonElement>();

        public static async Task<int> CheckoutOkAsync(HttpClient c)
        {
            var r = await CheckoutAsync(c);
            Assert.Equal(System.Net.HttpStatusCode.Created, r.StatusCode);
            return (await JsonAsync(r)).GetProperty("id").GetInt32();
        }

        public static async Task<JsonElement> GetCartAsync(HttpClient c)
        {
            var r = await c.GetAsync("api/cart");
            r.EnsureSuccessStatusCode();
            return await JsonAsync(r);
        }

        public static Task<int> GetProductQuantityAsync(HttpClient c, int id) => InventoryHelper.QuantityAsync(c, id);

        public static Task<JsonElement> GetMovementsAsync(HttpClient admin, int productId) =>
            InventoryHelper.MovementsAsync(admin, productId, "?pageSize=50");

        public static async Task<List<JsonElement>> MovementsOfReasonAsync(HttpClient admin, int productId, string reason) =>
            (await GetMovementsAsync(admin, productId)).GetProperty("items").EnumerateArray()
                .Where(m => m.GetProperty("reason").GetString() == reason).ToList();

        public static async Task<(HttpClient Client, string UserName)> RegisterAndLoginAsync(ApiFactory f)
        {
            var name = AuthHelper.Unique("buyer");
            var c = f.CreateClient();
            var r = await c.PostAsJsonAsync("api/account/register", new { userName = name, password = UserPassword });
            r.EnsureSuccessStatusCode();
            return (await AuthHelper.LoggedInAsync(f, name, UserPassword), name);
        }

        public static async Task<JsonElement> GetOrderAsync(HttpClient c, int id)
        {
            var r = await c.GetAsync($"api/orders/{id}");
            r.EnsureSuccessStatusCode();
            return await JsonAsync(r);
        }

        public static async Task<string> OrderStatusAsync(HttpClient c, int id) =>
            (await GetOrderAsync(c, id)).GetProperty("status").GetString()!;

        public static Task<HttpResponseMessage> SetStatusAsync(HttpClient c, int id, string? status) =>
            c.PutAsJsonAsync($"api/orders/{id}/status", new { status });

        public static Task<HttpResponseMessage> CancelAsync(HttpClient c, int id) =>
            c.PostAsync($"api/orders/{id}/cancel", null);

        public static Task<HttpResponseMessage> PayAsync(HttpClient c, int orderId, string? token = "tok_visa", string? key = null) =>
            c.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentToken = token, idempotencyKey = key });

        public static async Task<JsonElement> PayOkAsync(HttpClient c, int orderId, string token = "tok_visa", string? key = null)
        {
            var r = await PayAsync(c, orderId, token, key);
            Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
            return await JsonAsync(r);
        }

        public static async Task<string> PaymentStatusAsync(HttpClient c, int id) =>
            (await GetOrderAsync(c, id)).GetProperty("paymentStatus").GetString()!;

        public static async Task<List<string>> ErrorKeysAsync(HttpResponseMessage r) =>
            (await JsonAsync(r)).GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
    }

    public class CartRaceInterceptor : SaveChangesInterceptor
    {
        public DbContextOptions<ApplicationDbContext>? Options;
        private int _remaining;
        private int _hits;
        public int Hits => Volatile.Read(ref _hits);

        /// <summary>Product whose stock is changed by the racing writer (0 = none).</summary>
        public int StockProductId;
        public int StockDelta = 100;
        /// <summary>Status the racing writer gives the order being saved (null = none).</summary>
        public OrderStatus? RaceStatus;
        /// <summary>User name whose cart is checked out by a racing request that commits first (null = none).</summary>
        public string? RacingCheckoutUserName;
        /// <summary>Order cancelled completely (stock restored, movements, status) by a racing request that commits first (0 = none).</summary>
        public int RacingCancelOrderId;
        /// <summary>Order that gets a competing Pending payment (and a rotated stamp) from a racing pay that claims first (0 = none).</summary>
        public int RacingPayOrderId;

        public void Arm(int times)
        {
            Interlocked.Exchange(ref _hits, 0);
            Interlocked.Exchange(ref _remaining, times);
        }

        public void Disarm()
        {
            Interlocked.Exchange(ref _remaining, 0);
            StockProductId = 0;
            StockDelta = 100;
            RaceStatus = null;
            RacingCheckoutUserName = null;
            RacingCancelOrderId = 0;
            RacingPayOrderId = 0;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var ctx = eventData.Context;
            if (ctx == null || Volatile.Read(ref _remaining) <= 0) return result;

            var orderEntry = ctx.ChangeTracker.Entries<Order>()
                .FirstOrDefault(e => e.State == EntityState.Added || e.State == EntityState.Modified);
            var movementAdded = ctx.ChangeTracker.Entries<StockMovement>().Any(e => e.State == EntityState.Added);
            if (orderEntry == null && !movementAdded) return result;
            if (Interlocked.Decrement(ref _remaining) < 0) return result;

            Interlocked.Increment(ref _hits);
            using var other = new ApplicationDbContext(Options!);
            if (StockProductId != 0)
            {
                var p = await other.Products.SingleAsync(x => x.Id == StockProductId, cancellationToken);
                p.Quantity += StockDelta;
                p.ConcurrencyStamp = Guid.NewGuid();
            }
            if (RaceStatus != null && orderEntry is { State: EntityState.Modified })
            {
                var id = orderEntry.Entity.Id;
                var o = await other.Orders.SingleAsync(x => x.Id == id, cancellationToken);
                o.Status = RaceStatus.Value;
                o.ConcurrencyStamp = Guid.NewGuid();
            }
            if (RacingCancelOrderId != 0)
            {
                var o = await other.Orders.Include(x => x.Items).SingleAsync(x => x.Id == RacingCancelOrderId, cancellationToken);
                o.Status = OrderStatus.Cancelled;
                o.ConcurrencyStamp = Guid.NewGuid();
                foreach (var i in o.Items.Where(i => i.ProductId != null))
                {
                    var p = await other.Products.SingleAsync(x => x.Id == i.ProductId, cancellationToken);
                    p.Quantity += i.Quantity;
                    p.ConcurrencyStamp = Guid.NewGuid();
                    other.StockMovements.Add(new StockMovement { ProductId = p.Id, QuantityChange = i.Quantity,
                        QuantityAfter = p.Quantity, Reason = StockMovementReason.OrderCancelled,
                        Note = $"Order #{o.Id} cancelled", CreatedAtUtc = DateTime.UtcNow });
                }
            }
            if (RacingPayOrderId != 0)
            {
                var o = await other.Orders.SingleAsync(x => x.Id == RacingPayOrderId, cancellationToken);
                other.Payments.Add(new Payment { OrderId = o.Id, Amount = o.Total, Currency = "USD",
                    Status = PaymentState.Pending, IdempotencyKey = "race-" + Guid.NewGuid().ToString("N"),
                    CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
                o.ConcurrencyStamp = Guid.NewGuid();
            }
            if (RacingCheckoutUserName != null)
            {
                // A complete competing checkout: stock decrement, order, cart emptied.
                var uid = (await other.Users.SingleAsync(x => x.UserName == RacingCheckoutUserName, cancellationToken)).Id;
                var lines = await other.CartItems.Include(c => c.Product)
                    .Where(c => c.UserId == uid).ToListAsync(cancellationToken);
                var order = new Order { UserId = uid, Status = OrderStatus.Pending };
                foreach (var l in lines)
                {
                    l.Product!.Quantity -= l.Quantity;
                    l.Product.ConcurrencyStamp = Guid.NewGuid();
                    order.Items.Add(new OrderItem { ProductId = l.ProductId, ProductName = l.Product.Name,
                        UnitPrice = l.Product.Price, Quantity = l.Quantity, LineTotal = l.Product.Price * l.Quantity });
                }
                order.Total = order.Items.Sum(i => i.LineTotal);
                other.Orders.Add(order);
                other.CartItems.RemoveRange(lines);
            }
            await other.SaveChangesAsync(cancellationToken);
            return result;
        }
    }

    public class CartRacingFactory : ApiFactory
    {
        public readonly CartRaceInterceptor Interceptor = new();

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
