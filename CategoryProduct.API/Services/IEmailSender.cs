namespace CategoryProduct.API.Services
{
    /// <summary>Generic email transport. Knows nothing about orders or users.</summary>
    public interface IEmailSender
    {
        Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
    }
}
