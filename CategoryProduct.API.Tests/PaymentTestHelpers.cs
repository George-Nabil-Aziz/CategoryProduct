using System.Collections.Concurrent;
using System.Net.Http.Json;
using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static CategoryProduct.API.Tests.CartOrderTestHelpers;

namespace CategoryProduct.API.Tests
{
    /// <summary>Collects every message written through a typed ILogger (and exception text).</summary>
    public sealed class CapturingLogSink
    {
        public ConcurrentQueue<string> Messages { get; } = new();
    }

    public sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly CapturingLogSink _sink;
        public CapturingLogger(CapturingLogSink sink) => _sink = sink;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _sink.Messages.Enqueue(formatter(state, exception) + " " + exception);
    }

    /// <summary>ApiFactory whose typed-logger output can be inspected (to prove tokens and keys are never logged).</summary>
    public class LogCapturingApiFactory : ApiFactory
    {
        public CapturingLogSink Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(Logs);
                services.AddSingleton(typeof(ILogger<>), typeof(CapturingLogger<>));
            });
        }
    }

    internal static class PaymentTestHelpers
    {
        public static async Task<T> InDbAsync<T>(ApiFactory f, Func<ApplicationDbContext, Task<T>> body)
        {
            using var scope = f.Services.CreateScope();
            return await body(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }

        public static Task<List<Payment>> PaymentsAsync(ApiFactory f, int orderId) =>
            InDbAsync(f, db => db.Payments.AsNoTracking().Where(p => p.OrderId == orderId)
                .OrderBy(p => p.Id).ToListAsync());

        /// <summary>A Pending order of <paramref name="buyer"/> worth price x qty. Returns the order id and the product id.</summary>
        public static async Task<(int OrderId, int ProductId, decimal Total)> PlaceOrderAsync(
            ApiFactory f, HttpClient admin, HttpClient buyer, decimal price = 15m, int qty = 2, int stock = 10)
        {
            var pid = await CreateProductAsync(admin, null, price, stock);
            await AddOkAsync(buyer, pid, qty);
            return (await CheckoutOkAsync(buyer), pid, price * qty);
        }

        public static async Task SetEmailAsync(HttpClient c, string? email = null)
        {
            email ??= $"p{Guid.NewGuid():N}@example.com";
            (await c.PutAsJsonAsync("api/account/email", new { email })).EnsureSuccessStatusCode();
        }

        public static Task SeedPaymentAsync(ApiFactory f, int orderId, PaymentState state, string key,
            string? reference = null, decimal amount = 30m) =>
            InDbAsync(f, async db =>
            {
                db.Payments.Add(new Payment
                {
                    OrderId = orderId, Amount = amount, Currency = "USD", Status = state, IdempotencyKey = key,
                    ProviderReference = reference, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
                return 0;
            });

        /// <summary>Changes an order directly in the store like a competing request would (status, stamp).</summary>
        public static Task ForceOrderStatusAsync(ApiFactory f, int orderId, OrderStatus status) =>
            InDbAsync(f, async db =>
            {
                var o = await db.Orders.SingleAsync(x => x.Id == orderId);
                o.Status = status;
                o.ConcurrencyStamp = Guid.NewGuid();
                await db.SaveChangesAsync();
                return 0;
            });

        public static async Task<HttpClient> LoggedInAdminAsync(ApiFactory f) =>
            await AuthHelper.LoggedInAsync(f, ApiFactory.UserName);
    }
}
