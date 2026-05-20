using System.Net.Mail;

namespace ChiaMail.Services;

public interface ISmtpClient : IDisposable
{
    Task SendMailAsync(MailMessage message, CancellationToken ct);
    void SendAsyncCancel();
}
