using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LaGrandeurSpa.Web.Services;

public sealed class MailKitEmailSender(
    IOptions<SmtpSettings> options,
    ILogger<MailKitEmailSender> logger) : IEmailSender
{
    private readonly SmtpSettings smtp = options.Value;

    public bool IsEnabled =>
        smtp.Enabled &&
        !string.IsNullOrWhiteSpace(smtp.Host) &&
        smtp.Port is > 0 and <= 65535 &&
        !string.IsNullOrWhiteSpace(smtp.FromEmail);

    public async Task SendAsync(
        string toEmail,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(toEmail))
            return;

        var message = new MimeMessage();

        message.From.Add(
            new MailboxAddress(string.IsNullOrWhiteSpace(smtp.FromName) ? "La Grandeur Spa" : smtp.FromName.Trim(), smtp.FromEmail.Trim()));

        message.To.Add(MailboxAddress.Parse(toEmail.Trim()));
        message.Subject = subject;

        message.Body = new BodyBuilder
        {
            HtmlBody = body
        }.ToMessageBody();

        using var client = new SmtpClient();

        var security = smtp.UseStartTls
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.Auto;

        await client.ConnectAsync(
            smtp.Host,
            smtp.Port,
            security,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(smtp.Username))
        {
            await client.AuthenticateAsync(
                smtp.Username,
                smtp.Password,
                cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        logger.LogInformation(
            "Email sent successfully to {Email}. Subject: {Subject}",
            toEmail,
            subject);
    }
}
