namespace Ike.Api.Services;

public interface IEmailProvider
{
    string ProviderName { get; }

    Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default);
}
