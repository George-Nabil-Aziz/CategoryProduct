using CategoryProduct.API.Dtos;
using CategoryProduct.API.Models;

namespace CategoryProduct.API.Infrastructure
{
    public static class PaymentMappingExtensions
    {
        /// <summary>Never exposes the idempotency key (and no token exists to expose).</summary>
        public static PaymentResponseDto ToResponse(this Payment p) =>
            new(p.Id, p.OrderId, p.Amount, p.Currency, p.Status.ToString(), p.ProviderReference, p.FailureReason,
                p.CreatedAtUtc, p.UpdatedAtUtc);
    }
}
