using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CategoryProduct.API.Services
{
    public enum PayOutcome
    {
        Succeeded, Declined, ProviderError, NotFound, NotPending, AlreadyPaid,
        InProgress, KeyUsed, NothingToPay, Conflict
    }

    /// <summary><see cref="Detail"/> is the 409 message for the conflict-like outcomes.</summary>
    public sealed record PayResult(PayOutcome Outcome, Payment? Payment = null, string? Detail = null);

    public enum RefundOutcome { NotNeeded, Refunded, InProgress, ProviderError }

    public sealed record RefundStepResult(RefundOutcome Outcome, int? PaymentId = null);

    /// <summary>
    /// Pay and refund orchestration. The gateway is always called outside any database transaction:
    /// a Pending payment plus a rotated order stamp "claims" the order first, the charge follows, then one
    /// save finalizes. Tokens and idempotency keys are never logged or stored.
    /// </summary>
    public class PaymentService
    {
        private const int MaxAttempts = 3;

        public const string InProgressDetail = "A payment for this order is already in progress or completed.";
        public const string NotPendingDetail = "Only a Pending order can be paid.";
        public const string AlreadyPaidDetail = "The order is already paid.";
        public const string KeyUsedDetail = "Idempotency key already used.";
        public const string NothingToPayDetail = "The order has nothing to pay.";
        public const string CancelledRefundedDetail = "The order was cancelled; the payment was refunded.";
        public const string ChangedDetail = "The payment could not be completed because the order changed. Please try again.";
        public const string CancelInProgressDetail = "A payment is in progress for this order.";

        private readonly ApplicationDbContext _context;
        private readonly IPaymentGateway _gateway;
        private readonly PaymentOptions _options;
        private readonly NotificationService _notifications;
        private readonly ILogger<PaymentService> _logger;

        public PaymentService(ApplicationDbContext context, IPaymentGateway gateway, IOptions<PaymentOptions> options,
            NotificationService notifications, ILogger<PaymentService> logger)
        {
            _context = context;
            _gateway = gateway;
            _options = options.Value;
            _notifications = notifications;
            _logger = logger;
        }

        private IQueryable<Order> OrdersForMail() => _context.Orders.Include(o => o.Items).Include(o => o.User);

        // ---------- Pay ----------
        public async Task<PayResult> PayAsync(int orderId, int callerId, string token, string key,
            CancellationToken cancellationToken = default)
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                _context.ChangeTracker.Clear();

                var order = await OrdersForMail().FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
                if (order == null || order.UserId != callerId) return new PayResult(PayOutcome.NotFound);

                var payment = await _context.Payments.FirstOrDefaultAsync(p => p.IdempotencyKey == key, cancellationToken);
                if (payment != null)
                {
                    if (payment.OrderId != orderId) return new PayResult(PayOutcome.KeyUsed, null, KeyUsedDetail);
                    switch (payment.Status)
                    {
                        case PaymentState.Succeeded:
                        case PaymentState.Refunded:
                            return new PayResult(PayOutcome.Succeeded, payment);
                        case PaymentState.Failed:
                            return new PayResult(PayOutcome.Declined, payment);
                    }
                    // Pending: re-drive the (idempotent) charge with this request's token.
                }
                else
                {
                    if (order.Status != OrderStatus.Pending)
                        return new PayResult(PayOutcome.NotPending, null, NotPendingDetail);
                    if (order.PaymentStatus == OrderPaymentStatus.Paid)
                        return new PayResult(PayOutcome.AlreadyPaid, null, AlreadyPaidDetail);
                    var amount = Math.Round(order.Total, 2);
                    if (amount <= 0) return new PayResult(PayOutcome.NothingToPay, null, NothingToPayDetail);
                    if (await _context.Payments.AnyAsync(p => p.OrderId == orderId
                            && (p.Status == PaymentState.Pending || p.Status == PaymentState.Succeeded), cancellationToken))
                        return new PayResult(PayOutcome.InProgress, null, InProgressDetail);

                    var now = DateTime.UtcNow;
                    payment = new Payment
                    {
                        OrderId = orderId,
                        Amount = amount,
                        Currency = _options.Currency,
                        Status = PaymentState.Pending,
                        IdempotencyKey = key,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };
                    try
                    {
                        // Step 1, the lock: rotate the order stamp. Of two requests that read the same stamp only one
                        // succeeds. Kept as its own save so a lost race never leaves a half-written payment behind on
                        // a store without atomic saves.
                        order.ConcurrencyStamp = Guid.NewGuid();
                        await _context.SaveChangesAsync(cancellationToken);
                        // Step 2, the record: the Pending payment (unique key and one-active-payment-per-order index).
                        _context.Payments.Add(payment);
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateException)
                    {
                        // Lost the claim (order stamp or a unique index): the next attempt sees the winner.
                        continue;
                    }
                }

                return await ChargeAndFinalizeAsync(order, payment, token, cancellationToken);
            }

            _context.ChangeTracker.Clear();
            return new PayResult(PayOutcome.Conflict, null, InProgressDetail);
        }

        private async Task<PayResult> ChargeAndFinalizeAsync(Order order, Payment payment, string token,
            CancellationToken cancellationToken)
        {
            ChargeResult result;
            try
            {
                result = await _gateway.ChargeAsync(new ChargeRequest(order.Id, payment.Amount, payment.Currency,
                    payment.IdempotencyKey, token), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Payment provider error for order {OrderId} payment {PaymentId} ({ExceptionType})",
                    order.Id, payment.Id, ex.GetType().Name);
                await MarkFailedAsync(payment, "provider_error", cancellationToken);
                return new PayResult(PayOutcome.ProviderError, payment);
            }

            if (!result.Approved)
            {
                var reason = string.IsNullOrWhiteSpace(result.FailureCode) ? "declined" : result.FailureCode!;
                _logger.LogInformation("Payment declined for order {OrderId} payment {PaymentId}: {Reason}",
                    order.Id, payment.Id, reason);
                await MarkFailedAsync(payment, reason, cancellationToken);
                return new PayResult(PayOutcome.Declined, payment);
            }

            if (string.IsNullOrWhiteSpace(result.ProviderReference))
            {
                _logger.LogWarning("Payment provider returned no reference for order {OrderId} payment {PaymentId}",
                    order.Id, payment.Id);
                await MarkFailedAsync(payment, "provider_error", cancellationToken);
                return new PayResult(PayOutcome.ProviderError, payment);
            }

            return await FinalizeAsync(order, payment, result.ProviderReference!, cancellationToken);
        }

        private async Task MarkFailedAsync(Payment payment, string reason, CancellationToken cancellationToken)
        {
            payment.Status = PaymentState.Failed;
            payment.FailureReason = reason.Length > 200 ? reason[..200] : reason;
            payment.UpdatedAtUtc = DateTime.UtcNow;
            payment.ConcurrencyStamp = Guid.NewGuid();
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _logger.LogError("Could not record the failed payment {PaymentId}; it stays Pending.", payment.Id);
            }
        }

        private async Task<PayResult> FinalizeAsync(Order order, Payment payment, string reference,
            CancellationToken cancellationToken)
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var now = DateTime.UtcNow;
                payment.Status = PaymentState.Succeeded;
                payment.ProviderReference = reference;
                payment.FailureReason = null;
                payment.UpdatedAtUtc = now;
                payment.ConcurrencyStamp = Guid.NewGuid();
                order.PaymentStatus = OrderPaymentStatus.Paid;
                order.UpdatedAtUtc = now;
                order.ConcurrencyStamp = Guid.NewGuid();
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation(
                        "Payment {PaymentId} for order {OrderId} succeeded: {Amount} {Currency}, reference {Reference}",
                        payment.Id, order.Id, payment.Amount, payment.Currency, reference);
                    await _notifications.PaymentReceiptAsync(order, payment);
                    return new PayResult(PayOutcome.Succeeded, payment);
                }
                catch (DbUpdateConcurrencyException)
                {
                    _context.ChangeTracker.Clear();
                    var reloadedOrder = await OrdersForMail().FirstOrDefaultAsync(o => o.Id == order.Id, cancellationToken);
                    var reloadedPayment = await _context.Payments.FirstOrDefaultAsync(p => p.Id == payment.Id, cancellationToken);
                    if (reloadedOrder == null || reloadedPayment == null)
                        return new PayResult(PayOutcome.Conflict, null, ChangedDetail);
                    order = reloadedOrder;
                    payment = reloadedPayment;
                    if (order.Status == OrderStatus.Cancelled)
                        return await CompensateAsync(order, payment, reference, cancellationToken);
                    // Still Pending, or confirmed meanwhile by an Admin: a Paid order is valid either way, retry.
                }
            }

            _context.ChangeTracker.Clear();
            _logger.LogError("Payment {PaymentId} for order {OrderId} was charged but could not be finalized.",
                payment.Id, order.Id);
            return new PayResult(PayOutcome.Conflict, null, ChangedDetail);
        }

        /// <summary>The order was cancelled between the claim and the finalize: give the money back.</summary>
        private async Task<PayResult> CompensateAsync(Order order, Payment payment, string reference,
            CancellationToken cancellationToken)
        {
            var refunded = false;
            try
            {
                var refund = await _gateway.RefundAsync(new RefundRequest(reference, payment.Amount, payment.Currency,
                    RefundKey(payment.Id)), cancellationToken);
                refunded = refund.Succeeded;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Compensation refund failed for payment {PaymentId} ({ExceptionType})",
                    payment.Id, ex.GetType().Name);
            }

            var now = DateTime.UtcNow;
            payment.ProviderReference = reference;
            payment.Status = refunded ? PaymentState.Refunded : PaymentState.Succeeded;
            payment.UpdatedAtUtc = now;
            payment.ConcurrencyStamp = Guid.NewGuid();
            order.PaymentStatus = refunded ? OrderPaymentStatus.Refunded : OrderPaymentStatus.Paid;
            order.ConcurrencyStamp = Guid.NewGuid();
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _logger.LogError("Could not record the compensation for payment {PaymentId}.", payment.Id);
            }

            if (!refunded)
            {
                _logger.LogError("Payment {PaymentId} for cancelled order {OrderId} could not be refunded.",
                    payment.Id, order.Id);
                return new PayResult(PayOutcome.ProviderError, payment);
            }
            _logger.LogInformation("Payment {PaymentId} for cancelled order {OrderId} was refunded.", payment.Id, order.Id);
            await _notifications.PaymentRefundedAsync(order, payment);
            return new PayResult(PayOutcome.Conflict, payment, CancelledRefundedDetail);
        }

        // ---------- Refund for cancel ----------
        private static string RefundKey(int paymentId) => "refund-" + paymentId;

        /// <summary>
        /// Called by the cancel flow before the order is changed. Calls the gateway (idempotent per payment)
        /// but writes nothing: the caller marks the payment and the order Refunded in its own save.
        /// </summary>
        public async Task<RefundStepResult> RefundForCancelAsync(int orderId, CancellationToken cancellationToken = default)
        {
            var active = await _context.Payments.AsNoTracking()
                .Where(p => p.OrderId == orderId && (p.Status == PaymentState.Pending || p.Status == PaymentState.Succeeded))
                .ToListAsync(cancellationToken);
            if (active.Any(p => p.Status == PaymentState.Pending)) return new RefundStepResult(RefundOutcome.InProgress);
            var paid = active.FirstOrDefault(p => p.Status == PaymentState.Succeeded);
            if (paid == null) return new RefundStepResult(RefundOutcome.NotNeeded);

            try
            {
                var refund = await _gateway.RefundAsync(new RefundRequest(paid.ProviderReference ?? string.Empty,
                    paid.Amount, paid.Currency, RefundKey(paid.Id)), cancellationToken);
                if (!refund.Succeeded)
                {
                    _logger.LogWarning("Refund of payment {PaymentId} for order {OrderId} failed: {Reason}",
                        paid.Id, orderId, refund.FailureCode);
                    return new RefundStepResult(RefundOutcome.ProviderError);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Refund of payment {PaymentId} for order {OrderId} failed ({ExceptionType})",
                    paid.Id, orderId, ex.GetType().Name);
                return new RefundStepResult(RefundOutcome.ProviderError);
            }

            _logger.LogInformation("Payment {PaymentId} for order {OrderId} refunded: {Amount} {Currency}",
                paid.Id, orderId, paid.Amount, paid.Currency);
            return new RefundStepResult(RefundOutcome.Refunded, paid.Id);
        }

        /// <summary>Marks the (tracked) payment and order Refunded; the caller saves.</summary>
        public static void MarkRefunded(Payment payment, Order order)
        {
            payment.Status = PaymentState.Refunded;
            payment.UpdatedAtUtc = DateTime.UtcNow;
            payment.ConcurrencyStamp = Guid.NewGuid();
            order.PaymentStatus = OrderPaymentStatus.Refunded;
        }
    }
}
