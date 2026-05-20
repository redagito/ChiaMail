using System.Net;
using System.Net.Mail;

namespace ChiaMail.Services;

internal sealed class SmtpClientWrapper : ISmtpClient
{
    private readonly SmtpClient _inner;

    public SmtpClientWrapper(string host, int port, bool enableSsl, ICredentialsByHost credentials)
    {
        _inner = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            Credentials = credentials,
            Timeout = 10000
        };
    }

    public Task SendMailAsync(MailMessage message, CancellationToken ct) => _inner.SendMailAsync(message, ct);
    public void SendAsyncCancel() => _inner.SendAsyncCancel();
    public void Dispose() => _inner.Dispose();
}
