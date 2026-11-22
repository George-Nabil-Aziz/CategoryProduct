using System.Security.Claims;
using CategoryProduct.API.Data;
using CategoryProduct.API.Dtos;
using CategoryProduct.API.Infrastructure;
using CategoryProduct.API.Models;
using CategoryProduct.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class OrdersController : ControllerBase
    {
        private const int MaxAttempts = 3;
        private readonly ApplicationDbContext _context;
        private readonly InventoryService _inventory;
        private readonly NotificationService _notifications;
        private readonly PaymentService _payments;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(ApplicationDbContext context, InventoryService inventory, NotificationService notifications,
            PaymentService payments,
            ILogger<OrdersController> logger)
        {
            _context = context;
            _inventory = inventory;
            _notifications = notifications;
            _payments = payments;
            _logger = logger;
        }

        private int? CurrentUserId() =>
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        private ObjectResult ConflictProblem(string detail) =>
            StatusCode(409, new ProblemDetails { Status = 409, Title = "Conflict", Detail = detail });

        // ---------- Checkout ----------
        [HttpPost]
        public async Task<IActionResult> Checkout()
        {
            if (CurrentUserId() is not int userId) return Unauthorized();

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                _context.ChangeTracker.Clear();

                var lines = await _context.CartItems.Include(c => c.Product)
                    .Where(c => c.UserId == userId).OrderBy(c => c.Id).ToListAsync();
                if (lines.Count == 0)
                {
                    ModelState.AddModelError("Cart", "The cart is empty.");
                    return ValidationProblem(ModelState);
                }

                var shortages = lines.Where(l => l.Product!.Quantity < l.Quantity).ToList();
                if (shortages.Count > 0)
                    return InsufficientStock(shortages);

                var transaction = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync()
                    : null;
                try
                {
                    var now = DateTime.UtcNow;
                    var order = new Order
                    {
                        UserId = userId,
                        Status = OrderStatus.Pending,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };
                    var movements = new List<StockMovement>();
                    foreach (var line in lines)
                    {
                        var product = line.Product!;
                        var staged = await _inventory.StageMovementAsync(product.Id, -line.Quantity,
                            StockMovementReason.Sale, "Checkout", userId);
                        if (staged.Status != MovementStatus.Ok)
                        {
                            var failed = new List<CartItem> { line };
                            if (transaction != null) await transaction.RollbackAsync();
                            return InsufficientStock(failed);
                        }
                        movements.Add(staged.Movement!);
                        order.Items.Add(new OrderItem
                        {
                            ProductId = product.Id,
                            ProductName = product.Name,
                            UnitPrice = product.Price,
                            Quantity = line.Quantity,
                            LineTotal = product.Price * line.Quantity
                        });
                    }
                    order.Total = order.Items.Sum(i => i.LineTotal);
                    _context.Orders.Add(order);
                    _context.CartItems.RemoveRange(lines);

                    await _context.SaveChangesAsync();
                    if (transaction != null) await transaction.CommitAsync();

                    _logger.LogInformation("Order {OrderId} placed by user {UserId}, total {Total}",
                        order.Id, userId, order.Total);
                    foreach (var m in movements) _inventory.LogIfLowStock(m);

                    await _context.Users.FindAsync(userId); // navigation fix-up fills order.User
                    await _notifications.OrderPlacedAsync(order);
                    await _notifications.LowStockAsync(movements);
                    return CreatedAtAction(nameof(GetById), new { id = order.Id }, order.ToResponse());
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (transaction != null) await transaction.RollbackAsync();
                }
                finally
                {
                    if (transaction != null) await transaction.DisposeAsync();
                }
            }

            _context.ChangeTracker.Clear();
            return ConflictProblem("The order could not be completed because stock changed. Please try again.");
        }

        private ObjectResult InsufficientStock(List<CartItem> shortages)
        {
            var items = shortages.Select(s => new
            {
                productId = s.ProductId,
                productName = s.Product!.Name,
                requested = s.Quantity,
                available = s.Product.Quantity
            }).ToList();
            var detail = "Not enough stock for: " + string.Join("; ",
                items.Select(i => $"{i.productName} (requested {i.requested}, available {i.available})"));
            var problem = new ProblemDetails { Status = 409, Title = "Insufficient stock", Detail = detail };
            problem.Extensions["items"] = items;
            return StatusCode(409, problem);
        }

        // ---------- Reads ----------
        [HttpGet]
        public async Task<ActionResult<PagedResponseDto<OrderSummaryDto>>> GetMine(int page = 1, int pageSize = 10)
        {
            if (CurrentUserId() is not int userId) return Unauthorized();
            return await ListAsync(_context.Orders.AsNoTracking().Where(o => o.UserId == userId), page, pageSize);
        }

        [HttpGet("all")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<PagedResponseDto<OrderSummaryDto>>> GetAll(string? status = null,
            int page = 1, int pageSize = 10, string? paymentStatus = null)
        {
            var query = _context.Orders.AsNoTracking();
            if (paymentStatus != null)
            {
                if (!OrderPaymentStatusExtensions.TryParseName(paymentStatus, out var parsedPayment))
                {
                    ModelState.AddModelError("PaymentStatus", "PaymentStatus must be Unpaid, Paid or Refunded.");
                    return ValidationProblem(ModelState);
                }
                query = query.Where(o => o.PaymentStatus == parsedPayment);
            }
            if (status != null)
            {
                if (!OrderStatusExtensions.TryParseName(status, out var parsed))
                {
                    ModelState.AddModelError("status", "Status must be Pending, Confirmed or Cancelled.");
                    return ValidationProblem(ModelState);
                }
                query = query.Where(o => o.Status == parsed);
            }
            return await ListAsync(query, page, pageSize);
        }

        private async Task<PagedResponseDto<OrderSummaryDto>> ListAsync(IQueryable<Order> query, int page, int pageSize)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 50);
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(o => new OrderSummaryDto(o.Id, o.UserId, o.User!.UserName, o.Status.ToString(), o.Total,
                    o.Items.Count, o.CreatedAtUtc, o.UpdatedAtUtc, o.PaymentStatus.ToString()))
                .ToListAsync();
            return new PagedResponseDto<OrderSummaryDto>(items, page, pageSize, total,
                (int)Math.Ceiling(total / (double)pageSize));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<OrderResponseDto>> GetById(int id)
        {
            if (CurrentUserId() is not int callerId) return Unauthorized();
            var order = await _context.Orders.AsNoTracking()
                .Include(o => o.Items).Include(o => o.User)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null || (order.UserId != callerId && !User.IsInRole("Admin")))
                return NotFound();
            return order.ToResponse();
        }

        // ---------- Payments ----------
        [HttpPost("{id:int}/pay")]
        [EnableRateLimiting("payments")]
        public async Task<IActionResult> Pay(int id, PayOrderRequestDto request)
        {
            if (CurrentUserId() is not int callerId) return Unauthorized();

            if (!PaymentTokenRules.TryNormalizeToken(request.PaymentToken, out var token, out var tokenError))
                ModelState.AddModelError("PaymentToken", tokenError);
            if (!PaymentTokenRules.TryNormalizeKey(request.IdempotencyKey, out var key, out var keyError))
                ModelState.AddModelError("IdempotencyKey", keyError);
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var result = await _payments.PayAsync(id, callerId, token, key, HttpContext.RequestAborted);
            switch (result.Outcome)
            {
                case PayOutcome.Succeeded:
                    return Ok(result.Payment!.ToResponse());
                case PayOutcome.NotFound:
                    return NotFound();
                case PayOutcome.Declined:
                {
                    var reason = result.Payment!.FailureReason ?? "declined";
                    var problem = new ProblemDetails
                    {
                        Status = 402,
                        Title = "Payment declined",
                        Detail = $"Payment declined: {reason}."
                    };
                    problem.Extensions["paymentId"] = result.Payment.Id;
                    problem.Extensions["reason"] = reason;
                    return StatusCode(402, problem);
                }
                case PayOutcome.ProviderError:
                    return ProviderErrorProblem();
                default:
                    return ConflictProblem(result.Detail ?? PaymentService.InProgressDetail);
            }
        }

        private ObjectResult ProviderErrorProblem() =>
            StatusCode(502, new ProblemDetails
            {
                Status = 502,
                Title = "Payment provider error",
                Detail = "The payment provider could not complete the request. Please try again later."
            });

        [HttpGet("{id:int}/payments")]
        public async Task<ActionResult<PagedResponseDto<PaymentResponseDto>>> GetPayments(int id, int page = 1, int pageSize = 10)
        {
            if (CurrentUserId() is not int callerId) return Unauthorized();
            var ownerId = await _context.Orders.AsNoTracking().Where(o => o.Id == id)
                .Select(o => (int?)o.UserId).FirstOrDefaultAsync();
            if (ownerId == null || (ownerId != callerId && !User.IsInRole("Admin"))) return NotFound();

            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 50);
            var query = _context.Payments.AsNoTracking().Where(p => p.OrderId == id);
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return new PagedResponseDto<PaymentResponseDto>(items.Select(p => p.ToResponse()).ToList(), page, pageSize,
                total, (int)Math.Ceiling(total / (double)pageSize));
        }

        // ---------- Cancel ----------
        private enum CancelStatus { Ok, NotFound, Conflict, ProviderError }

        private record CancelResult(CancelStatus Status, Order? Order, string? Detail, Payment? RefundedPayment = null);

        private async Task<CancelResult> CancelCoreAsync(int orderId, int callerId, bool isAdmin)
        {
            RefundStepResult? refund = null;
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                _context.ChangeTracker.Clear();

                var order = await _context.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.User)
                    .FirstOrDefaultAsync(o => o.Id == orderId);
                if (order == null || (order.UserId != callerId && !isAdmin))
                    return new CancelResult(CancelStatus.NotFound, null, null);
                if (order.Status != OrderStatus.Pending)
                    return new CancelResult(CancelStatus.Conflict, null, "Only a Pending order can be cancelled.");

                // Refund first (idempotent per payment); memoized so a retried save never calls the gateway again.
                refund ??= await _payments.RefundForCancelAsync(orderId);
                if (refund.Outcome == RefundOutcome.InProgress)
                    return new CancelResult(CancelStatus.Conflict, null, PaymentService.CancelInProgressDetail);
                if (refund.Outcome == RefundOutcome.ProviderError)
                    return new CancelResult(CancelStatus.ProviderError, null, null);

                var transaction = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync()
                    : null;
                try
                {
                    foreach (var item in order.Items)
                    {
                        if (item.ProductId is not int productId)
                        {
                            _logger.LogWarning("Order {OrderId} item {ItemId} skipped on cancel: product deleted",
                                orderId, item.Id);
                            continue;
                        }
                        var staged = await _inventory.StageMovementAsync(productId, item.Quantity,
                            StockMovementReason.OrderCancelled, $"Order #{orderId} cancelled", callerId);
                        if (staged.Status == MovementStatus.NotFound)
                        {
                            _logger.LogWarning("Order {OrderId} item {ItemId} skipped on cancel: product deleted",
                                orderId, item.Id);
                            continue;
                        }
                        if (staged.Status != MovementStatus.Ok)
                        {
                            if (transaction != null) await transaction.RollbackAsync();
                            _context.ChangeTracker.Clear();
                            return new CancelResult(CancelStatus.Conflict, null,
                                "The stock quantity cannot be restored.");
                        }
                    }

                    // The order is read untracked and attached after the products are staged, so that on a
                    // non-transactional store a failed product update is reached before the order update.
                    _context.Attach(order);
                    order.Status = OrderStatus.Cancelled;
                    order.UpdatedAtUtc = DateTime.UtcNow;
                    order.ConcurrencyStamp = Guid.NewGuid();

                    Payment? refunded = null;
                    if (refund.Outcome == RefundOutcome.Refunded)
                    {
                        refunded = await _context.Payments.FirstOrDefaultAsync(p => p.Id == refund.PaymentId);
                        if (refunded != null) PaymentService.MarkRefunded(refunded, order);
                    }

                    await _context.SaveChangesAsync();
                    if (transaction != null) await transaction.CommitAsync();
                    return new CancelResult(CancelStatus.Ok, order, null, refunded);
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (transaction != null) await transaction.RollbackAsync();
                }
                finally
                {
                    if (transaction != null) await transaction.DisposeAsync();
                }
            }

            _context.ChangeTracker.Clear();
            return new CancelResult(CancelStatus.Conflict, null,
                "The order could not be cancelled because it changed. Please try again.");
        }

        private async Task<IActionResult> ToActionResultAsync(CancelResult r)
        {
            switch (r.Status)
            {
                case CancelStatus.Ok:
                    await _notifications.OrderStatusChangedAsync(r.Order!);
                    if (r.RefundedPayment != null)
                        await _notifications.PaymentRefundedAsync(r.Order!, r.RefundedPayment);
                    return Ok(r.Order!.ToResponse());
                case CancelStatus.NotFound:
                    return NotFound();
                case CancelStatus.ProviderError:
                    return ProviderErrorProblem();
                default:
                    return ConflictProblem(r.Detail!);
            }
        }

        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id)
        {
            if (CurrentUserId() is not int callerId) return Unauthorized();
            return await ToActionResultAsync(await CancelCoreAsync(id, callerId, User.IsInRole("Admin")));
        }

        // ---------- Admin status ----------
        [HttpPut("{id:int}/status")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SetStatus(int id, UpdateOrderStatusRequestDto request)
        {
            if (CurrentUserId() is not int adminId) return Unauthorized();

            if (!OrderStatusExtensions.TryParseName(request.Status, out var target))
            {
                ModelState.AddModelError("Status", "Status must be Pending, Confirmed or Cancelled.");
                return ValidationProblem(ModelState);
            }

            if (target == OrderStatus.Cancelled)
                return await ToActionResultAsync(await CancelCoreAsync(id, adminId, true));

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                _context.ChangeTracker.Clear();
                var order = await _context.Orders.Include(o => o.Items).Include(o => o.User)
                    .FirstOrDefaultAsync(o => o.Id == id);
                if (order == null) return NotFound();
                if (target == OrderStatus.Pending)
                    return ConflictProblem("Invalid status transition.");
                if (order.Status != OrderStatus.Pending)
                    return ConflictProblem("Only a Pending order can be confirmed.");

                order.Status = OrderStatus.Confirmed;
                order.UpdatedAtUtc = DateTime.UtcNow;
                order.ConcurrencyStamp = Guid.NewGuid();
                try
                {
                    await _context.SaveChangesAsync();
                    await _notifications.OrderStatusChangedAsync(order);
                    return Ok(order.ToResponse());
                }
                catch (DbUpdateConcurrencyException)
                {
                }
            }

            _context.ChangeTracker.Clear();
            return ConflictProblem("The order could not be updated because it changed. Please try again.");
        }
    }
}
