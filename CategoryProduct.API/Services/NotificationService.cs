using System.Globalization;
using System.Text;
using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CategoryProduct.API.Services
{
    /// <summary>
    /// Composes plain-text notification emails and enqueues them. Every public method catches all
    /// exceptions (Warning log) so callers never fail because of a notification.
    /// </summary>
    public class NotificationService
    {
        private const string DateFormat = "yyyy-MM-dd HH:mm 'UTC'";

        private readonly EmailQueue _queue;
        private readonly ApplicationDbContext _context;
        private readonly InventoryOptions _inventory;
        private readonly EmailOptions _email;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(EmailQueue queue, ApplicationDbContext context,
            IOptions<InventoryOptions> inventory, IOptions<EmailOptions> email,
            ILogger<NotificationService> logger)
        {
            _queue = queue;
            _context = context;
            _inventory = inventory.Value;
            _email = email.Value;
            _logger = logger;
        }

        /// <summary>The order's User and Items must be loaded.</summary>
        public Task OrderPlacedAsync(Order order)
        {
            try
            {
                if (!_email.Enabled) return Task.CompletedTask;
                var to = order.User?.Email;
                if (string.IsNullOrEmpty(to)) return Task.CompletedTask;

                var sb = new StringBuilder();
                sb.Append("Hello ").Append(order.User!.UserName).Append(",\n\n");
                sb.Append("Your order #").Append(order.Id.ToString(CultureInfo.InvariantCulture))
                    .Append(" was placed on ").Append(Date(order.CreatedAtUtc)).Append(".\n");
                sb.Append("Status: Pending\n\n");
                AppendItemsAndTotal(sb, order);
                _queue.TryEnqueue(new EmailMessage(to,
                    $"Order #{order.Id.ToString(CultureInfo.InvariantCulture)} placed", sb.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Order placed notification failed.");
            }
            return Task.CompletedTask;
        }

        /// <summary>For a Confirmed or Cancelled order; User and Items must be loaded.</summary>
        public Task OrderStatusChangedAsync(Order order)
        {
            try
            {
                if (!_email.Enabled) return Task.CompletedTask;
                if (order.Status != OrderStatus.Confirmed && order.Status != OrderStatus.Cancelled)
                    return Task.CompletedTask;
                var to = order.User?.Email;
                if (string.IsNullOrEmpty(to)) return Task.CompletedTask;

                var word = order.Status == OrderStatus.Confirmed ? "Confirmed" : "Cancelled";
                var id = order.Id.ToString(CultureInfo.InvariantCulture);
                var sb = new StringBuilder();
                sb.Append("Hello ").Append(order.User!.UserName).Append(",\n\n");
                sb.Append("Your order #").Append(id).Append(" is now ").Append(word)
                    .Append(" (updated ").Append(Date(order.UpdatedAtUtc)).Append(").\n");
                if (order.Status == OrderStatus.Cancelled)
                    sb.Append("Reserved stock was released.\n");
                sb.Append('\n');
                AppendItemsAndTotal(sb, order);
                _queue.TryEnqueue(new EmailMessage(to, $"Order #{id} {word.ToLowerInvariant()}", sb.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Order status notification failed.");
            }
            return Task.CompletedTask;
        }

        /// <summary>After a payment became Succeeded; order User and Items must be loaded. No token or key inside.</summary>
        public Task PaymentReceiptAsync(Order order, Payment payment)
        {
            try
            {
                if (!_email.Enabled) return Task.CompletedTask;
                var to = order.User?.Email;
                if (string.IsNullOrEmpty(to)) return Task.CompletedTask;

                var id = order.Id.ToString(CultureInfo.InvariantCulture);
                var sb = new StringBuilder();
                sb.Append("Hello ").Append(StripLineBreaks(order.User!.UserName)).Append(",\n\n");
                sb.Append("We received your payment for order #").Append(id).Append(" on ")
                    .Append(Date(payment.UpdatedAtUtc)).Append(".\n");
                sb.Append("Amount: ").Append(payment.Amount.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(' ').Append(payment.Currency).Append('\n');
                sb.Append("Reference: ").Append(StripLineBreaks(payment.ProviderReference ?? string.Empty)).Append("\n\n");
                AppendItemsAndTotal(sb, order);
                _queue.TryEnqueue(new EmailMessage(to, $"Payment received for order #{id}", sb.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Payment receipt notification failed.");
            }
            return Task.CompletedTask;
        }

        /// <summary>After a paid order was cancelled and the refund succeeded; order User must be loaded.</summary>
        public Task PaymentRefundedAsync(Order order, Payment payment)
        {
            try
            {
                if (!_email.Enabled) return Task.CompletedTask;
                var to = order.User?.Email;
                if (string.IsNullOrEmpty(to)) return Task.CompletedTask;

                var id = order.Id.ToString(CultureInfo.InvariantCulture);
                var sb = new StringBuilder();
                sb.Append("Hello ").Append(StripLineBreaks(order.User!.UserName)).Append(",\n\n");
                sb.Append("Your payment of ").Append(payment.Amount.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(' ').Append(payment.Currency).Append(" for order #").Append(id)
                    .Append(" was refunded on ").Append(Date(payment.UpdatedAtUtc)).Append(".\n");
                sb.Append("Reference: ").Append(StripLineBreaks(payment.ProviderReference ?? string.Empty)).Append('\n');
                _queue.TryEnqueue(new EmailMessage(to, $"Payment refunded for order #{id}", sb.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Payment refund notification failed.");
            }
            return Task.CompletedTask;
        }

        /// <summary>Movements must have been saved; movement.Product is used for names.</summary>
        public async Task LowStockAsync(IEnumerable<StockMovement> movements)
        {
            try
            {
                if (!_email.Enabled) return;
                var threshold = _inventory.Effective;
                var crossings = movements
                    .Where(m => m.QuantityChange < 0
                        && m.QuantityAfter < threshold
                        && m.QuantityAfter - m.QuantityChange >= threshold)
                    .ToList();
                if (crossings.Count == 0) return;

                var admins = await _context.Users
                    .Where(u => u.Role == "Admin" && u.Email != null)
                    .Select(u => u.Email!)
                    .ToListAsync();
                if (admins.Count == 0) return;

                var subject = crossings.Count == 1
                    ? "Low stock: " + ProductName(crossings[0])
                    : "Low stock: " + crossings.Count.ToString(CultureInfo.InvariantCulture) + " products";
                subject = StripLineBreaks(subject);
                if (subject.Length > EmailMessage.MaxSubjectLength)
                    subject = subject[..EmailMessage.MaxSubjectLength];

                var sb = new StringBuilder();
                sb.Append("Stock fell below the threshold (").Append(threshold.ToString(CultureInfo.InvariantCulture))
                    .Append("):\n\n");
                foreach (var m in crossings)
                    sb.Append("- #").Append(m.ProductId.ToString(CultureInfo.InvariantCulture)).Append(' ')
                        .Append(ProductName(m)).Append(": ")
                        .Append(m.QuantityAfter.ToString(CultureInfo.InvariantCulture)).Append(" left (was ")
                        .Append((m.QuantityAfter - m.QuantityChange).ToString(CultureInfo.InvariantCulture))
                        .Append(")\n");
                var body = sb.ToString();

                foreach (var to in admins)
                    _queue.TryEnqueue(new EmailMessage(to, subject, body));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Low stock notification failed.");
            }
        }

        /// <summary>Queues the reset link mail. The link and its token are never logged.</summary>
        public Task PasswordResetRequestedAsync(User user, string link, int validMinutes)
        {
            try
            {
                if (!_email.Enabled) return Task.CompletedTask;
                var to = user.Email;
                if (string.IsNullOrEmpty(to)) return Task.CompletedTask;

                var sb = new StringBuilder();
                sb.Append("Hello ").Append(StripLineBreaks(user.UserName)).Append(",\n\n");
                sb.Append("We received a request to reset the password of your account.\n");
                sb.Append("Open this link to choose a new password (valid for ")
                    .Append(validMinutes.ToString(CultureInfo.InvariantCulture)).Append(" minutes, one use):\n\n");
                sb.Append(link).Append("\n\n");
                sb.Append("If you did not ask for this, ignore this email: your password stays as it is.\n");
                _queue.TryEnqueue(new EmailMessage(to, "Reset your password", sb.ToString()));
            }
            catch (Exception)
            {
                _logger.LogWarning("Password reset notification failed.");
            }
            return Task.CompletedTask;
        }

        /// <summary>Queues the "password changed" notice (no password, token or link inside).</summary>
        public Task PasswordChangedAsync(User user)
        {
            try
            {
                if (!_email.Enabled) return Task.CompletedTask;
                var to = user.Email;
                if (string.IsNullOrEmpty(to)) return Task.CompletedTask;

                var sb = new StringBuilder();
                sb.Append("Hello ").Append(StripLineBreaks(user.UserName)).Append(",\n\n");
                sb.Append("The password of your account was changed on ").Append(Date(DateTime.UtcNow)).Append(".\n");
                sb.Append("All other sessions were signed out.\n\n");
                sb.Append("If you did not do this, reset your password immediately using \"Forgot password\" on the login page, and contact an administrator.\n");
                _queue.TryEnqueue(new EmailMessage(to, "Your password was changed", sb.ToString()));
            }
            catch (Exception)
            {
                _logger.LogWarning("Password changed notification failed.");
            }
            return Task.CompletedTask;
        }

        private static string ProductName(StockMovement m) =>
            StripLineBreaks(m.Product?.Name ?? ("product " + m.ProductId.ToString(CultureInfo.InvariantCulture)));

        private static string StripLineBreaks(string s) => s.Replace("\r", "").Replace("\n", "");

        private static string Date(DateTime utc) => utc.ToString(DateFormat, CultureInfo.InvariantCulture);

        private static void AppendItemsAndTotal(StringBuilder sb, Order order)
        {
            sb.Append("Items:\n");
            foreach (var i in order.Items)
                sb.Append("- ").Append(i.ProductName).Append(" x").Append(i.Quantity.ToString(CultureInfo.InvariantCulture))
                    .Append(" @ ").Append(i.UnitPrice.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" = ").Append(i.LineTotal.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("\nTotal: ").Append(order.Total.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
        }
    }
}
