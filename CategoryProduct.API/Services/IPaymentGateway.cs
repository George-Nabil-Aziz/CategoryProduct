namespace CategoryProduct.API.Services
{
    public interface IPaymentGateway
    {
        Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken = default);
        Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default);
    }

    public sealed record ChargeRequest(int OrderId, decimal Amount, string Currency, string IdempotencyKey, string PaymentToken);
    public sealed record ChargeResult(bool Approved, string? ProviderReference, string? FailureCode);
    public sealed record RefundRequest(string ProviderReference, decimal Amount, string Currency, string IdempotencyKey);
    public sealed record RefundResult(bool Succeeded, string? RefundReference, string? FailureCode);
}
