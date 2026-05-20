using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using ChiaMail.Models;

namespace ChiaMail.Services;

public class MailProgress
{
    public int Current { get; set; }
    public int Total { get; set; }
    public string? CurrentRecipient { get; set; }
    public string? LastResult { get; set; }
    public bool IsError { get; set; }
}

public class MailService : IDisposable
{
    /// <summary>Gmail's maximum total attachment size per message (25 MB).</summary>
    public const long MaxAttachmentSizeBytes = 25 * 1024 * 1024;

    private readonly ISmtpClient _client;
    private readonly string _fromEmail;
    private bool _disposed;

    private static readonly Regex PlaceholderPattern = new(@"\{(\w+)\}", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedPlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "FirstName", "LastName", "Logo"
    };

    public MailService(string fromEmail, string appPassword)
        : this(fromEmail, new SmtpClientWrapper("smtp.gmail.com", 587, true, new NetworkCredential(fromEmail, appPassword)))
    {
    }

    internal MailService(string fromEmail, ISmtpClient client)
    {
        if (string.IsNullOrWhiteSpace(fromEmail))
            throw new ArgumentNullException(nameof(fromEmail));
        if (!CsvService.IsValidEmail(fromEmail))
            throw new ArgumentException($"'{fromEmail}' is not a valid email address.", nameof(fromEmail));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _fromEmail = fromEmail;
    }

    /// <summary>Lightweight SMTP reachability check — connects, reads banner, disconnects. No email sent.</summary>
    public static async Task CheckSmtpServerAsync(CancellationToken ct, string host = "smtp.gmail.com", int port = 587)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, cts.Token);

            using var ns = tcp.GetStream();
            using var reader = new StreamReader(ns, Encoding.ASCII);
            var banner = await reader.ReadLineAsync(cts.Token);

            if (string.IsNullOrEmpty(banner) || !banner.StartsWith("220"))
                throw new SmtpException($"SMTP server at {host}:{port} did not return a valid greeting. Response: {banner ?? "(none)"}");
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Could not reach SMTP server {host}:{port} within 10 seconds. Check your internet connection.");
        }
    }

    public static bool IsValidAppPassword(string? appPassword)
    {
        if (string.IsNullOrWhiteSpace(appPassword))
            return false;

        var normalized = appPassword.Replace(" ", string.Empty);
        if (normalized.Length != 16)
            return false;

        return normalized.All(char.IsLetterOrDigit);
    }

    /// <summary>Sends a test email to self to verify authentication and delivery.</summary>
    public async Task TestConnectionAsync(CancellationToken ct)
    {
        using var msg = new MailMessage(
            _fromEmail, _fromEmail,
            "ChiaMail Test",
            "Your Gmail SMTP connection is working.");

        await _client.SendMailAsync(msg, ct);
    }

    public async Task SendBulkAsync(
        List<Recipient> recipients,
        string subjectTemplate,
        string bodyTemplate,
        bool isHtml,
        string? logoPath,
        IProgress<MailProgress>? progress,
        CancellationToken ct,
        int delaySeconds = 5,
        List<string>? attachmentPaths = null)
    {
        if (recipients is null)
            throw new ArgumentNullException(nameof(recipients));
        if (recipients.Count == 0)
            throw new ArgumentException("Recipients list is empty.", nameof(recipients));
        if (recipients.Count > CsvService.MaxRecipients)
            throw new ArgumentException($"Too many recipients ({recipients.Count}). Maximum is {CsvService.MaxRecipients}.", nameof(recipients));
        if (string.IsNullOrWhiteSpace(subjectTemplate))
            throw new ArgumentException("Subject cannot be empty.", nameof(subjectTemplate));
        if (string.IsNullOrWhiteSpace(bodyTemplate))
            throw new ArgumentException("Body cannot be empty.", nameof(bodyTemplate));
        if (delaySeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(delaySeconds), "Delay cannot be negative.");

        // Validate logo
        if (!string.IsNullOrEmpty(logoPath))
        {
            if (!File.Exists(logoPath))
                throw new FileNotFoundException("Logo file not found.", logoPath);
            if (!EmailComposer.IsSupportedImage(logoPath))
                throw new NotSupportedException($"Logo file '{Path.GetFileName(logoPath)}' is not a supported image format.");
        }

        // Validate attachment files
        if (attachmentPaths is { Count: > 0 })
        {
            long totalSize = 0;

            for (int i = 0; i < attachmentPaths.Count; i++)
            {
                var ap = attachmentPaths[i];

                if (string.IsNullOrWhiteSpace(ap))
                    throw new ArgumentException($"Attachment path at index {i} is empty.", nameof(attachmentPaths));

                var fileInfo = new FileInfo(ap);
                if (!fileInfo.Exists)
                    throw new FileNotFoundException($"Attachment file not found: {ap}", ap);

                totalSize += fileInfo.Length;

                if (totalSize > MaxAttachmentSizeBytes)
                {
                    var totalMb = totalSize / (1024.0 * 1024.0);
                    throw new ArgumentException(
                        $"Total attachment size ({totalMb:F1} MB) exceeds Gmail's {MaxAttachmentSizeBytes / 1024 / 1024} MB limit.");
                }
            }
        }

        // Validate all recipient emails upfront
        var invalidEmails = recipients
            .Where(r => !CsvService.IsValidEmail(r.Email))
            .Select(r => r.Email)
            .ToList();

        if (invalidEmails.Count > 0)
            throw new ArgumentException(
                $"Invalid recipient email(s): {string.Join(", ", invalidEmails.Take(5))}{(invalidEmails.Count > 5 ? $" and {invalidEmails.Count - 5} more" : "")}.",
                nameof(recipients));

        // Warn about unknown placeholders
        var unknownPlaceholders = FindUnknownPlaceholders(subjectTemplate, bodyTemplate);
        if (unknownPlaceholders.Count > 0)
        {
            progress?.Report(new MailProgress
            {
                Current = 0,
                Total = recipients.Count,
                CurrentRecipient = null,
                LastResult = $"⚠ Warning: Unknown placeholders found: {string.Join(", ", unknownPlaceholders)}. Intended?",
                IsError = false
            });
        }

        for (int i = 0; i < recipients.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var r = recipients[i];

            try
            {
                var subject = ReplacePlaceholders(subjectTemplate, r);
                var body = ReplacePlaceholders(bodyTemplate, r);

                // {Logo} not valid in subject line — remove it silently
                subject = subject.Replace("{Logo}", "");

                using var message = new MailMessage();
                message.From = new MailAddress(_fromEmail);
                message.To.Add(r.Email);
                message.Subject = subject;

                if (isHtml || !string.IsNullOrEmpty(logoPath))
                {
                    var htmlBody = EmailComposer.BuildHtmlBody(body, isHtml, logoPath);
                    var htmlView = AlternateView.CreateAlternateViewFromString(
                        htmlBody, Encoding.UTF8, MediaTypeNames.Text.Html);

                    if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath!))
                    {
                        var mime = EmailComposer.GetMimeType(logoPath!);
                        var logo = new LinkedResource(logoPath, mime)
                        {
                            ContentId = "logoImage",
                            TransferEncoding = TransferEncoding.Base64
                        };
                        htmlView.LinkedResources.Add(logo);
                    }

                    message.AlternateViews.Add(htmlView);
                }
                else
                {
                    message.Body = body;
                    message.IsBodyHtml = false;
                }

                if (attachmentPaths is { Count: > 0 })
                {
                    foreach (var ap in attachmentPaths)
                    {
                        message.Attachments.Add(new Attachment(ap));
                    }
                }

                await _client.SendMailAsync(message, ct);

                progress?.Report(new MailProgress
                {
                    Current = i + 1,
                    Total = recipients.Count,
                    CurrentRecipient = r.Email,
                    LastResult = $"✓ Sent to {r.Email}",
                    IsError = false
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SmtpFailedRecipientException ex)
            {
                progress?.Report(new MailProgress
                {
                    Current = i + 1,
                    Total = recipients.Count,
                    CurrentRecipient = r.Email,
                    LastResult = $"✗ {r.Email}: Recipient rejected - {ex.Message}",
                    IsError = true
                });
            }
            catch (SmtpException ex)
            {
                progress?.Report(new MailProgress
                {
                    Current = i + 1,
                    Total = recipients.Count,
                    CurrentRecipient = r.Email,
                    LastResult = $"✗ {r.Email}: SMTP error - {ex.Message}",
                    IsError = true
                });
            }
            catch (Exception ex)
            {
                progress?.Report(new MailProgress
                {
                    Current = i + 1,
                    Total = recipients.Count,
                    CurrentRecipient = r.Email,
                    LastResult = $"✗ {r.Email}: {ex.Message}",
                    IsError = true
                });
            }

            if (i < recipients.Count - 1)
                await Task.Delay(delaySeconds * 1000, ct);
        }
    }

    internal static string ReplacePlaceholders(string template, Recipient recipient)
    {
        return template
            .Replace("{FirstName}", recipient.FirstName)
            .Replace("{LastName}", recipient.LastName);
    }

    private static List<string> FindUnknownPlaceholders(params string[] templates)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var template in templates)
        {
            var matches = PlaceholderPattern.Matches(template);
            foreach (Match match in matches)
            {
                var name = match.Groups[1].Value;
                if (!AllowedPlaceholders.Contains(name))
                    found.Add($"{{{name}}}");
            }
        }

        return found.OrderBy(x => x).ToList();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _client.Dispose();
            _disposed = true;
        }
    }

    public void CancelPendingRequests()
    {
        _client.SendAsyncCancel();
    }
}
