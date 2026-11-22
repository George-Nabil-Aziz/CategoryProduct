using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CategoryProduct.API.Services
{
    public enum MovementStatus { Ok, NotFound, InsufficientStock, Conflict }

    public record MovementResult(MovementStatus Status, StockMovement? Movement);

    public class InventoryService
    {
        private const int MaxAttempts = 3;
        private readonly ApplicationDbContext _context;
        private readonly InventoryOptions _options;
        private readonly ILogger<InventoryService> _logger;

        public InventoryService(ApplicationDbContext context, IOptions<InventoryOptions> options,
            ILogger<InventoryService> logger)
        {
            _context = context;
            _options = options.Value;
            _logger = logger;
        }

        public int Threshold => _options.Effective;

        /// <summary>
        /// Validates and applies a stock change to the tracked product and adds the StockMovement
        /// to the context, WITHOUT saving. Callers (e.g. checkout) may stage several and save once.
        /// Status is Ok, NotFound or InsufficientStock (below zero or int overflow).
        /// </summary>
        public async Task<MovementResult> StageMovementAsync(int productId, int quantityChange,
            StockMovementReason reason, string? note, int? userId, CancellationToken ct = default)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
            if (product == null)
                return new MovementResult(MovementStatus.NotFound, null);

            var after = (long)product.Quantity + quantityChange;
            if (after < 0 || after > int.MaxValue)
                return new MovementResult(MovementStatus.InsufficientStock, null);

            product.Quantity = (int)after;
            product.ConcurrencyStamp = Guid.NewGuid();
            var movement = new StockMovement
            {
                Product = product,
                ProductId = product.Id,
                QuantityChange = quantityChange,
                QuantityAfter = product.Quantity,
                Reason = reason,
                Note = note,
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.StockMovements.Add(movement);
            return new MovementResult(MovementStatus.Ok, movement);
        }

        public async Task<MovementResult> ApplyMovementAsync(int productId, int quantityChange,
            StockMovementReason reason, string? note, int? userId, CancellationToken ct = default)
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var result = await StageMovementAsync(productId, quantityChange, reason, note, userId, ct);
                if (result.Status != MovementStatus.Ok)
                    return result;

                try
                {
                    await _context.SaveChangesAsync(ct);
                    LogIfLowStock(result.Movement!);
                    return result;
                }
                catch (DbUpdateConcurrencyException)
                {
                    _context.ChangeTracker.Clear();
                }
            }
            return new MovementResult(MovementStatus.Conflict, null);
        }

        /// <summary>Logs a warning when a decrease left the product below the threshold. Call after a successful save.</summary>
        public void LogIfLowStock(StockMovement movement)
        {
            if (movement.QuantityChange < 0 && movement.QuantityAfter < Threshold)
                _logger.LogWarning("Low stock: product {ProductId} quantity {Quantity} is below threshold {Threshold}",
                    movement.ProductId, movement.QuantityAfter, Threshold);
        }
    }
}
