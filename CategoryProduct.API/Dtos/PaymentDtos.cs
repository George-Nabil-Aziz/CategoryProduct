namespace CategoryProduct.API.Dtos
{
    /// <summary>Body of POST api/orders/{id}/pay. No amount or currency: both come from the server.</summary>
    public record PayOrderRequestDto(string? PaymentToken, string? IdempotencyKey);

    public record PaymentResponseDto(int Id, int OrderId, decimal Amount, string Currency, string Status,
        string? ProviderReference, string? FailureReason, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
}
