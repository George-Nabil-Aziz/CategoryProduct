using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.SqlServerTests
{
    /// <summary>Proves the Payments indexes and the Orders.PaymentStatus default on a real SQL Server (full migration).</summary>
    public class PaymentIndexSqlServerTests
    {
        private static bool IsUniqueViolation(Exception ex)
        {
            for (; ex != null; ex = ex.InnerException!)
                if (ex is SqlException { Number: 2601 or 2627 }) return true;
            return false;
        }

        private static async Task WithDatabaseAsync(Func<ApplicationDbContext, string, Task> test)
        {
            var cs = SqlServerEnv.BuildConnectionString(SqlServerEnv.NewDatabaseName());
            using var db = new ApplicationDbContext(SqlServerEnv.Options(cs));
            try
            {
                await db.Database.MigrateAsync();
                await test(db, cs);
            }
            finally
            {
                await db.Database.EnsureDeletedAsync();
            }
        }

        private static async Task<int> NewOrderAsync(ApplicationDbContext db)
        {
            var user = new User { UserName = "u" + Guid.NewGuid().ToString("N")[..12], PasswordHash = "x", Role = "User" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var order = new Order
            {
                UserId = user.Id, Status = OrderStatus.Pending, Total = 30m,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            return order.Id;
        }

        private static Payment NewPayment(int orderId, PaymentState state, string? key = null) =>
            new()
            {
                OrderId = orderId, Amount = 30m, Currency = "USD", Status = state,
                IdempotencyKey = key ?? Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            };

        private static async Task AddAsync(ApplicationDbContext db, string connectionString, Payment payment)
        {
            // A fresh context per write keeps a failed save from poisoning the next one.
            using var ctx = new ApplicationDbContext(SqlServerEnv.Options(connectionString));
            ctx.Payments.Add(payment);
            await ctx.SaveChangesAsync();
        }

        [Fact]
        public Task DuplicateIdempotencyKey_FailsWithUniqueViolation() => WithDatabaseAsync(async (db, cs) =>
        {
            var a = await NewOrderAsync(db);
            var b = await NewOrderAsync(db);
            await AddAsync(db, cs, NewPayment(a, PaymentState.Failed, "same-key"));

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() =>
                AddAsync(db, cs, NewPayment(b, PaymentState.Failed, "same-key")));
            Assert.True(IsUniqueViolation(ex));
        });

        [Theory]
        [InlineData(PaymentState.Pending, PaymentState.Pending)]
        [InlineData(PaymentState.Pending, PaymentState.Succeeded)]
        [InlineData(PaymentState.Succeeded, PaymentState.Pending)]
        [InlineData(PaymentState.Succeeded, PaymentState.Succeeded)]
        public Task TwoActivePaymentsForOneOrder_FailWithUniqueViolation(PaymentState first, PaymentState second) =>
            WithDatabaseAsync(async (db, cs) =>
            {
                var order = await NewOrderAsync(db);
                await AddAsync(db, cs, NewPayment(order, first));

                var ex = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(db, cs, NewPayment(order, second)));
                Assert.True(IsUniqueViolation(ex));
            });

        [Fact]
        public Task ManyFailedPaymentsForOneOrder_AreAccepted() => WithDatabaseAsync(async (db, cs) =>
        {
            var order = await NewOrderAsync(db);
            for (var i = 0; i < 3; i++) await AddAsync(db, cs, NewPayment(order, PaymentState.Failed));

            Assert.Equal(3, await db.Payments.CountAsync(p => p.OrderId == order));
        });

        [Fact]
        public Task FailedPlusOneSucceeded_IsAccepted() => WithDatabaseAsync(async (db, cs) =>
        {
            var order = await NewOrderAsync(db);
            await AddAsync(db, cs, NewPayment(order, PaymentState.Failed));
            await AddAsync(db, cs, NewPayment(order, PaymentState.Failed));
            await AddAsync(db, cs, NewPayment(order, PaymentState.Succeeded));

            Assert.Equal(3, await db.Payments.CountAsync(p => p.OrderId == order));
        });

        [Fact]
        public Task RefundedPlusANewPending_IsAccepted() => WithDatabaseAsync(async (db, cs) =>
        {
            var order = await NewOrderAsync(db);
            await AddAsync(db, cs, NewPayment(order, PaymentState.Refunded));
            await AddAsync(db, cs, NewPayment(order, PaymentState.Pending));

            Assert.Equal(2, await db.Payments.CountAsync(p => p.OrderId == order));
        });

        [Fact]
        public Task OrderPaymentStatus_DefaultsToUnpaid_WhenInsertedWithoutIt() => WithDatabaseAsync(async (db, _) =>
        {
            var order = await NewOrderAsync(db);
            // Raw SQL omits the column on purpose: the database default must fill it.
            var user = await db.Users.AsNoTracking().Select(u => u.Id).FirstAsync();
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Orders (UserId, Status, Total, CreatedAtUtc, UpdatedAtUtc, ConcurrencyStamp) " +
                "VALUES ({0}, 'Pending', 5, SYSUTCDATETIME(), SYSUTCDATETIME(), NEWID())", user);

            var statuses = await db.Orders.AsNoTracking().Select(o => o.PaymentStatus).ToListAsync();
            Assert.Equal(2, statuses.Count);
            Assert.All(statuses, s => Assert.Equal(OrderPaymentStatus.Unpaid, s));
            Assert.NotEqual(0, order);
        });

        [Fact]
        public Task ModelHasNoPendingChanges() => WithDatabaseAsync((db, _) =>
        {
            Assert.False(db.Database.HasPendingModelChanges(),
                "The model differs from the migrations: generate a migration with dotnet ef.");
            return Task.CompletedTask;
        });
    }
}
