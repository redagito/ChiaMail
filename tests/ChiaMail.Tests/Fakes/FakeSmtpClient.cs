using System.IO;
using System.Net.Mail;
using ChiaMail.Services;

namespace ChiaMail.Tests.Fakes;

public sealed class SentMessage
{
    public required List<string> To { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public required bool IsBodyHtml { get; init; }
    public required List<string> LinkedResourceContentIds { get; init; }
    public required List<string> AttachmentPaths { get; init; }
}

public sealed class FakeSmtpClient : ISmtpClient
{
    public List<SentMessage> SentMessages { get; } = [];
    public bool ThrowOnSend { get; set; }
    public Exception? SendException { get; set; }
    public int? ThrowOnSendAfterCount { get; set; }
    private int _sendCalls;

    public Task SendMailAsync(MailMessage message, CancellationToken ct)
    {
        _sendCalls++;

        if (ThrowOnSendAfterCount.HasValue && _sendCalls >= ThrowOnSendAfterCount.Value)
        {
            var ex = SendException ?? new InvalidOperationException("Simulated SMTP failure on call " + _sendCalls);
            throw ex;
        }

        if (ThrowOnSend)
        {
            var ex = SendException ?? new InvalidOperationException("Simulated SMTP failure");
            throw ex;
        }

        // Read body from AlternateViews if present (HTML mails use them)
        string body = message.Body;
        if (message.AlternateViews.Count > 0 && message.AlternateViews[0].ContentStream is { } stream)
        {
            stream.Position = 0;
            using var reader = new StreamReader(stream);
            body = reader.ReadToEnd();
        }

        SentMessages.Add(new SentMessage
        {
            To = message.To.Select(a => a.Address).ToList(),
            Subject = message.Subject,
            Body = body,
            IsBodyHtml = message.IsBodyHtml,
            LinkedResourceContentIds = message.AlternateViews
                .SelectMany(av => av.LinkedResources.Select(lr => lr.ContentId))
                .ToList(),
            AttachmentPaths = message.Attachments.Select(a => a.Name ?? "").ToList()
        });

        return Task.CompletedTask;
    }

    public void SendAsyncCancel() { }

    public void Dispose() { }
}
