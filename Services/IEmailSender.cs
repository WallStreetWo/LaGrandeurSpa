namespace LaGrandeurSpa.Web.Services;

public interface IEmailSender
{
    bool IsEnabled { get; }

    Task SendAsync(
        string toEmail,
        string subject,
        string body,
        CancellationToken cancellationToken = default);
}
