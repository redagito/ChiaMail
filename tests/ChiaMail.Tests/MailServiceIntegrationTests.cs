using System.IO;
using System.Net.Mail;
using ChiaMail.Models;
using ChiaMail.Services;
using ChiaMail.Tests.Fakes;

namespace ChiaMail.Tests;

public sealed class MailServiceIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeSmtpClient _fakeClient;
    private readonly MailService _svc;

    public MailServiceIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ChiaMailIntegration", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _fakeClient = new FakeSmtpClient();
        _svc = new MailService("sender@gmail.com", (ISmtpClient)_fakeClient);
    }

    public void Dispose()
    {
        _svc.Dispose();
        _fakeClient.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteCsv(string content)
    {
        var path = Path.Combine(_tempDir, "test.csv");
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateLogo()
    {
        var path = Path.Combine(_tempDir, "logo.png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        return path;
    }

    // ─── Full pipeline: CSV → compose → send ──────────────────

    [Fact]
    public async Task SendBulkAsync_PlainTextWithPlaceholders_SendsResolvedEmails()
    {
        var csv = WriteCsv("Email,FirstName,LastName\r\nalice@test.com,Alice,Johnson\r\nbob@test.com,Bob,Smith");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        var progressItems = new List<MailProgress>();

        await _svc.SendBulkAsync(
            recipients, "Hello {FirstName}", "Hi {FirstName} {LastName}",
            isHtml: false, logoPath: null,
            progress: new SyncProgress<MailProgress>(p => progressItems.Add(p)),
            CancellationToken.None, delaySeconds: 0);

        Assert.Equal(2, _fakeClient.SentMessages.Count);

        var msg1 = _fakeClient.SentMessages[0];
        Assert.Equal("alice@test.com", msg1.To[0]);
        Assert.Equal("Hello Alice", msg1.Subject);
        Assert.Contains("Hi Alice Johnson", msg1.Body);
        Assert.False(msg1.IsBodyHtml);

        var msg2 = _fakeClient.SentMessages[1];
        Assert.Equal("bob@test.com", msg2.To[0]);
        Assert.Equal("Hello Bob", msg2.Subject);
        Assert.Contains("Hi Bob Smith", msg2.Body);

        Assert.Equal(2, progressItems.Count);
        Assert.Equal("✓ Sent to alice@test.com", progressItems[0].LastResult);
        Assert.Equal("✓ Sent to bob@test.com", progressItems[1].LastResult);
    }

    [Fact]
    public async Task SendBulkAsync_HtmlWithLogo_EmbedsLogoInEveryMessage()
    {
        var csv = WriteCsv("Email,FirstName\r\nuser@test.com,Test");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        var logoPath = CreateLogo();

        await _svc.SendBulkAsync(
            recipients, "Hi {FirstName}", "<p>Hello {FirstName}</p>",
            isHtml: true, logoPath: logoPath, null, CancellationToken.None);

        var msg = Assert.Single(_fakeClient.SentMessages);
        Assert.Contains("logoImage", msg.LinkedResourceContentIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendBulkAsync_ProgressReportsCorrectCounts()
    {
        var csv = WriteCsv("Email\r\na@t.com\r\nb@t.com\r\nc@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        var progressItems = new List<MailProgress>();

        await _svc.SendBulkAsync(
            recipients, "S", "B", isHtml: false, logoPath: null,
            progress: new SyncProgress<MailProgress>(p => progressItems.Add(p)),
            CancellationToken.None, delaySeconds: 0);

        Assert.Equal(3, progressItems.Count);
        Assert.Equal(1, progressItems[0].Current);
        Assert.Equal(3, progressItems[0].Total);
        Assert.Equal(3, progressItems[^1].Current);
    }

    // ─── Validation / edge cases ──────────────────────────────

    [Fact]
    public async Task SendBulkAsync_NullRecipients_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _svc.SendBulkAsync(null!, "S", "B", false, null, null, CancellationToken.None));
        Assert.Equal("recipients", ex.ParamName);
    }

    [Fact]
    public async Task SendBulkAsync_EmptyRecipients_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.SendBulkAsync([], "S", "B", false, null, null, CancellationToken.None));
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendBulkAsync_TooManyRecipients_Throws()
    {
        var many = Enumerable.Range(0, CsvService.MaxRecipients + 1)
            .Select(i => new Recipient { Email = $"u{i}@t.com" })
            .ToList();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.SendBulkAsync(many, "S", "B", false, null, null, CancellationToken.None));
        Assert.Contains("too many", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendBulkAsync_EmptySubject_Throws()
    {
        var list = new List<Recipient> { new() { Email = "a@t.com" } };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.SendBulkAsync(list, "", "B", false, null, null, CancellationToken.None));
        Assert.Contains("subject", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendBulkAsync_EmptyBody_Throws()
    {
        var list = new List<Recipient> { new() { Email = "a@t.com" } };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.SendBulkAsync(list, "S", "", false, null, null, CancellationToken.None));
        Assert.Contains("body", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendBulkAsync_InvalidRecipientEmails_Throws()
    {
        var list = new List<Recipient> { new() { Email = "not-an-email" } };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.SendBulkAsync(list, "S", "B", false, null, null, CancellationToken.None));
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendBulkAsync_LogoFileNotFound_Throws()
    {
        var list = new List<Recipient> { new() { Email = "a@t.com" } };
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _svc.SendBulkAsync(list, "S", "B", false, Path.Combine(_tempDir, "nope.png"), null, CancellationToken.None));
        Assert.Contains("logo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendBulkAsync_UnsupportedLogoFormat_Throws()
    {
        var list = new List<Recipient> { new() { Email = "a@t.com" } };
        var badLogo = Path.Combine(_tempDir, "logo.txt");
        File.WriteAllText(badLogo, "not an image");
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            _svc.SendBulkAsync(list, "S", "B", false, badLogo, null, CancellationToken.None));
        Assert.Contains("supported", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendBulkAsync_CancelledToken_ThrowsOperationCanceled()
    {
        var csv = WriteCsv("Email\r\na@t.com\r\nb@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _svc.SendBulkAsync(recipients, "S", "B", false, null, null, cts.Token));
        Assert.Empty(_fakeClient.SentMessages);
    }

    [Fact]
    public async Task SendBulkAsync_SmtpFailure_ReportsErrorViaProgress()
    {
        var csv = WriteCsv("Email\r\na@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        _fakeClient.ThrowOnSend = true;
        _fakeClient.SendException = new SmtpException("Server rejected");

        var progressItems = new List<MailProgress>();
        await _svc.SendBulkAsync(
            recipients, "S", "B", false, null,
            new SyncProgress<MailProgress>(p => progressItems.Add(p)), CancellationToken.None);

        var report = Assert.Single(progressItems);
        Assert.StartsWith("✗", report.LastResult);
        Assert.True(report.IsError);
    }

    // ─── Unknown placeholder warning ──────────────────────────

    [Fact]
    public async Task SendBulkAsync_UnknownPlaceholder_ReportsWarning()
    {
        var csv = WriteCsv("Email\r\na@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        var progressItems = new List<MailProgress>();

        await _svc.SendBulkAsync(
            recipients, "{Unknown}", "{AlsoUnknown}",
            false, null, new SyncProgress<MailProgress>(p => progressItems.Add(p)), CancellationToken.None);

        var warning = progressItems[0];
        Assert.Contains("Warning", warning.LastResult, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendBulkAsync_KnownPlaceholders_NoWarning()
    {
        var csv = WriteCsv("Email,FirstName,LastName\r\na@t.com,A,B");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        var progressItems = new List<MailProgress>();

        await _svc.SendBulkAsync(
            recipients, "Hi {FirstName}", "Hello {FirstName} {LastName}",
            false, null, new SyncProgress<MailProgress>(p => progressItems.Add(p)), CancellationToken.None);

        Assert.DoesNotContain(progressItems, p =>
            (p.LastResult ?? "").Contains("Warning", StringComparison.OrdinalIgnoreCase));
    }

    // ─── Constructor validation ───────────────────────────────

    [Fact]
    public void Constructor_NullEmail_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new MailService(null!, _fakeClient));
        Assert.Equal("fromEmail", ex.ParamName);
    }

    [Fact]
    public void Constructor_EmptyEmail_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new MailService("", _fakeClient));
        Assert.Equal("fromEmail", ex.ParamName);
    }

    [Fact]
    public void Constructor_InvalidEmail_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new MailService("bad", _fakeClient));
        Assert.Contains("not a valid email", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_NullClient_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new MailService("a@t.com", (ISmtpClient)null!));
        Assert.Equal("client", ex.ParamName);
    }

    [Fact]
    public void Constructor_ValidEmailAndClient_Succeeds()
    {
        using var svc = new MailService("valid@gmail.com", _fakeClient);
        Assert.NotNull(svc);
    }

    // ─── Delay between sends ─────────────────────────────────

    [Fact]
    public async Task SendBulkAsync_NegativeDelay_Throws()
    {
        var csv = WriteCsv("Email\r\na@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _svc.SendBulkAsync(recipients, "S", "B", false, null, null, default, delaySeconds: -1));
        Assert.Contains("Delay", ex.Message);
    }

    [Fact]
    public async Task SendBulkAsync_ZeroDelay_SendsWithoutThrottle()
    {
        var csv = WriteCsv("Email\r\na@t.com\r\nb@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);

        await _svc.SendBulkAsync(
            recipients, "S", "B", false, null, null, default, delaySeconds: 0);

        Assert.Equal(2, _fakeClient.SentMessages.Count);
    }

    // ─── {Logo} placeholder ──────────────────────────────────

    [Fact]
    public async Task SendBulkAsync_LogoPlaceholderInBody_ReplacedWithImgTag()
    {
        var csv = WriteCsv("Email,FirstName\r\nuser@test.com,Alice");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        var logoPath = CreateLogo();

        await _svc.SendBulkAsync(
            recipients, "Hi", "<p>Hello {FirstName}, here is our logo: {Logo}</p>",
            isHtml: true, logoPath: logoPath, null, CancellationToken.None);

        var msg = Assert.Single(_fakeClient.SentMessages);
        Assert.DoesNotContain("{Logo}", msg.Body);
        Assert.Contains("cid:logoImage", msg.Body);
        Assert.Contains("Alice", msg.Body);
    }

    [Fact]
    public async Task SendBulkAsync_LogoPlaceholderNoLogoPath_RemovesPlaceholder()
    {
        var csv = WriteCsv("Email,FirstName\r\nuser@test.com,Alice");
        var (recipients, _, _, _) = CsvService.Parse(csv);

        await _svc.SendBulkAsync(
            recipients, "Hi", "<p>Here is our logo: {Logo}</p>",
            isHtml: true, logoPath: null, null, CancellationToken.None);

        var msg = Assert.Single(_fakeClient.SentMessages);
        Assert.DoesNotContain("{Logo}", msg.Body);
    }

    [Fact]
    public async Task SendBulkAsync_NoLogoPlaceholderWithLogoPath_LogoAutoAppended()
    {
        var csv = WriteCsv("Email,FirstName\r\nuser@test.com,Alice");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        var logoPath = CreateLogo();

        await _svc.SendBulkAsync(
            recipients, "Hi", "<p>Hello {FirstName}</p>",
            isHtml: true, logoPath: logoPath, null, CancellationToken.None);

        var msg = Assert.Single(_fakeClient.SentMessages);
        Assert.DoesNotContain("{Logo}", msg.Body);
        Assert.Contains("cid:logoImage", msg.Body);
        // Logo should be near the end (auto-appended before </body>)
        var cidIdx = msg.Body.LastIndexOf("cid:logoImage", StringComparison.Ordinal);
        var bodyEndIdx = msg.Body.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        Assert.True(cidIdx >= 0);
        Assert.True(bodyEndIdx >= 0);
    }

    // ─── Attachments ─────────────────────────────────────────

    [Fact]
    public async Task SendBulkAsync_WithAttachments_AttachesFiles()
    {
        var csv = WriteCsv("Email\r\na@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        var attach1 = Path.Combine(_tempDir, "doc.pdf");
        var attach2 = Path.Combine(_tempDir, "notes.txt");
        File.WriteAllText(attach1, "pdf content");
        File.WriteAllText(attach2, "hello");

        await _svc.SendBulkAsync(
            recipients, "S", "B", false, null, null, default, delaySeconds: 0,
            attachmentPaths: [attach1, attach2]);

        var msg = Assert.Single(_fakeClient.SentMessages);
        Assert.Contains("doc.pdf", msg.AttachmentPaths);
        Assert.Contains("notes.txt", msg.AttachmentPaths);
        Assert.Equal(2, msg.AttachmentPaths.Count);
    }

    [Fact]
    public async Task SendBulkAsync_AttachmentFileNotFound_Throws()
    {
        var csv = WriteCsv("Email\r\na@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _svc.SendBulkAsync(recipients, "S", "B", false, null, null, default, delaySeconds: 0,
                attachmentPaths: [@"C:\nonexistent\nope.pdf"]));
        Assert.Contains("attachment", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendBulkAsync_EmptyAttachmentPaths_SendsWithoutAttachments()
    {
        var csv = WriteCsv("Email\r\na@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);

        await _svc.SendBulkAsync(
            recipients, "S", "B", false, null, null, default, delaySeconds: 0,
            attachmentPaths: []);

        var msg = Assert.Single(_fakeClient.SentMessages);
        Assert.Empty(msg.AttachmentPaths);
    }

    [Fact]
    public async Task SendBulkAsync_EmptyAttachmentPath_Throws()
    {
        var csv = WriteCsv("Email\r\na@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.SendBulkAsync(recipients, "S", "B", false, null, null, default, delaySeconds: 0,
                attachmentPaths: [""]));
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendBulkAsync_WhitespaceAttachmentPath_Throws()
    {
        var csv = WriteCsv("Email\r\na@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.SendBulkAsync(recipients, "S", "B", false, null, null, default, delaySeconds: 0,
                attachmentPaths: ["   "]));
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendBulkAsync_AttachmentWithSubdirs_AttachesFile()
    {
        var csv = WriteCsv("Email\r\na@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);
        var attach = Path.Combine(subDir, "doc.pdf");
        File.WriteAllText(attach, "content");

        await _svc.SendBulkAsync(
            recipients, "S", "B", false, null, null, default, delaySeconds: 0,
            attachmentPaths: [attach]);

        var msg = Assert.Single(_fakeClient.SentMessages);
        Assert.Contains("doc.pdf", msg.AttachmentPaths);
    }

    // ─── Attachment size limit ───────────────────────────────

    [Fact]
    public async Task SendBulkAsync_AttachmentTotalSizeExceedsLimit_Throws()
    {
        var csv = WriteCsv("Email\r\na@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        var largeFile = Path.Combine(_tempDir, "large.bin");
        using (var fs = new FileStream(largeFile, FileMode.Create, FileAccess.Write))
            fs.SetLength(MailService.MaxAttachmentSizeBytes);
        var smallFile = Path.Combine(_tempDir, "small.txt");
        File.WriteAllText(smallFile, "x");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.SendBulkAsync(recipients, "S", "B", false, null, null, default, delaySeconds: 0,
                attachmentPaths: [largeFile, smallFile]));
        Assert.Contains("size", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendBulkAsync_AttachmentTotalSizeAtLimit_Succeeds()
    {
        var csv = WriteCsv("Email\r\na@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        var atLimit = Path.Combine(_tempDir, "atlimit.bin");
        using (var fs = new FileStream(atLimit, FileMode.Create, FileAccess.Write))
            fs.SetLength(MailService.MaxAttachmentSizeBytes);

        await _svc.SendBulkAsync(
            recipients, "S", "B", false, null, null, default, delaySeconds: 0,
            attachmentPaths: [atLimit]);

        var msg = Assert.Single(_fakeClient.SentMessages);
        Assert.Contains("atlimit.bin", msg.AttachmentPaths.Single());
    }

    [Fact]
    public async Task SendBulkAsync_SingleAttachmentOverLimit_Throws()
    {
        var csv = WriteCsv("Email\r\na@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        var overLimit = Path.Combine(_tempDir, "over.bin");
        using (var fs = new FileStream(overLimit, FileMode.Create, FileAccess.Write))
            fs.SetLength(MailService.MaxAttachmentSizeBytes + 1);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.SendBulkAsync(recipients, "S", "B", false, null, null, default, delaySeconds: 0,
                attachmentPaths: [overLimit]));
        Assert.Contains("size", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Per-recipient exception handling ──────────────────────

    [Fact]
    public async Task SendBulkAsync_SmtpFailedRecipientException_ReportsErrorPerRecipient()
    {
        var csv = WriteCsv("Email\r\na@t.com\r\nb@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        _fakeClient.ThrowOnSendAfterCount = 1;
        _fakeClient.SendException = new SmtpFailedRecipientException(SmtpStatusCode.MailboxUnavailable, "b@t.com");

        var progressItems = new List<MailProgress>();
        await _svc.SendBulkAsync(
            recipients, "S", "B", false, null,
            new SyncProgress<MailProgress>(p => progressItems.Add(p)),
            CancellationToken.None, delaySeconds: 0);

        Assert.Equal(2, progressItems.Count);
        Assert.All(progressItems, p => Assert.True(p.IsError));
        Assert.Contains("Recipient rejected", progressItems[0].LastResult);
        Assert.Contains("Mailbox", progressItems[0].LastResult);
    }

    [Fact]
    public async Task SendBulkAsync_SmtpException_ReportsErrorPerRecipient()
    {
        var csv = WriteCsv("Email\r\na@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        _fakeClient.ThrowOnSendAfterCount = 1;
        _fakeClient.SendException = new SmtpException("Service unavailable");

        var progressItems = new List<MailProgress>();
        await _svc.SendBulkAsync(
            recipients, "S", "B", false, null,
            new SyncProgress<MailProgress>(p => progressItems.Add(p)),
            CancellationToken.None, delaySeconds: 0);

        var report = Assert.Single(progressItems);
        Assert.True(report.IsError);
        Assert.Contains("SMTP error", report.LastResult);
    }

    [Fact]
    public async Task SendBulkAsync_GenericException_ReportsError()
    {
        var csv = WriteCsv("Email\r\na@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        _fakeClient.ThrowOnSendAfterCount = 1;
        _fakeClient.SendException = new InvalidOperationException("Something went wrong");

        var progressItems = new List<MailProgress>();
        await _svc.SendBulkAsync(
            recipients, "S", "B", false, null,
            new SyncProgress<MailProgress>(p => progressItems.Add(p)),
            CancellationToken.None, delaySeconds: 0);

        var report = Assert.Single(progressItems);
        Assert.True(report.IsError);
        Assert.Contains("Something went wrong", report.LastResult);
    }

    [Fact]
    public async Task SendBulkAsync_MixedSuccessAndFailure_ReportsBoth()
    {
        var csv = WriteCsv("Email\r\na@t.com\r\nb@t.com\r\nc@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        // First succeeds, second and third fail
        _fakeClient.ThrowOnSendAfterCount = 2;
        _fakeClient.SendException = new SmtpException("Timeout");

        var progressItems = new List<MailProgress>();
        await _svc.SendBulkAsync(
            recipients, "S", "B", false, null,
            new SyncProgress<MailProgress>(p => progressItems.Add(p)),
            CancellationToken.None, delaySeconds: 0);

        Assert.Equal(3, progressItems.Count);
        Assert.False(progressItems[0].IsError);
        Assert.Contains("Sent", progressItems[0].LastResult);
        Assert.True(progressItems[1].IsError);
        Assert.True(progressItems[2].IsError);
    }

    [Fact]
    public async Task SendBulkAsync_OperationCanceledMidSend_PropagatesException()
    {
        var csv = WriteCsv("Email\r\na@t.com\r\nb@t.com");
        var (recipients, _, _, _) = CsvService.Parse(csv);
        // Throw OperationCanceledException on second send (simulates SendAsyncCancel)
        _fakeClient.ThrowOnSendAfterCount = 2;
        _fakeClient.SendException = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _svc.SendBulkAsync(recipients, "S", "B", false, null, null, CancellationToken.None, delaySeconds: 0));

        // Only the first message should have been sent
        Assert.Single(_fakeClient.SentMessages);
    }

    // ─── TestConnectionAsync via fake ──────────────────────────

    [Fact]
    public async Task TestConnectionAsync_SendsTestEmailToSelf()
    {
        await _svc.TestConnectionAsync(CancellationToken.None);

        var msg = Assert.Single(_fakeClient.SentMessages);
        Assert.Equal("sender@gmail.com", msg.To[0]);
        Assert.Equal("ChiaMail Test", msg.Subject);
    }

    [Fact]
    public async Task TestConnectionAsync_PropagatesSmtpFailure()
    {
        _fakeClient.ThrowOnSend = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.TestConnectionAsync(CancellationToken.None));
    }

    // ─── CancelPendingRequests ────────────────────────────────

    [Fact]
    public void CancelPendingRequests_CallsSendAsyncCancel()
    {
        // Should not throw — FakeSmtpClient.SendAsyncCancel is a no-op
        _svc.CancelPendingRequests();
    }

    // ─── CheckSmtpServerAsync is static and uses real TCP ─────
    // Not unit-testable without network; covered by integration/E2E

    // ─── ReplacePlaceholders ─────────────────────────────────

    [Fact]
    public void ReplacePlaceholders_ReplacesFirstName()
    {
        var recipient = new Recipient { Email = "a@t.com", FirstName = "Alice" };
        var result = MailService.ReplacePlaceholders("Hello {FirstName}", recipient);
        Assert.Equal("Hello Alice", result);
    }

    [Fact]
    public void ReplacePlaceholders_ReplacesLastName()
    {
        var recipient = new Recipient { Email = "a@t.com", LastName = "Smith" };
        var result = MailService.ReplacePlaceholders("Hello {LastName}", recipient);
        Assert.Equal("Hello Smith", result);
    }

    [Fact]
    public void ReplacePlaceholders_ReplacesBoth()
    {
        var recipient = new Recipient { Email = "a@t.com", FirstName = "John", LastName = "Doe" };
        var result = MailService.ReplacePlaceholders("Hi {FirstName} {LastName}", recipient);
        Assert.Equal("Hi John Doe", result);
    }

    [Fact]
    public void ReplacePlaceholders_NoPlaceholders_ReturnsOriginal()
    {
        var recipient = new Recipient { Email = "a@t.com" };
        var result = MailService.ReplacePlaceholders("Hello World", recipient);
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void ReplacePlaceholders_EmptyFirstName_ReplacesWithEmpty()
    {
        var recipient = new Recipient { Email = "a@t.com", FirstName = "", LastName = "Smith" };
        var result = MailService.ReplacePlaceholders("Hi {FirstName} {LastName}", recipient);
        Assert.Equal("Hi  Smith", result);
    }

    [Fact]
    public void ReplacePlaceholders_SpecialChars_Preserved()
    {
        var recipient = new Recipient { Email = "a@t.com", FirstName = "O'Brien", LastName = "Smith & Co." };
        var result = MailService.ReplacePlaceholders("Hi {FirstName} {LastName}", recipient);
        Assert.Equal("Hi O'Brien Smith & Co.", result);
    }

    [Fact]
    public void ReplacePlaceholders_ReplacesAllOccurrences()
    {
        var recipient = new Recipient { Email = "a@t.com", FirstName = "Bob" };
        var result = MailService.ReplacePlaceholders("{FirstName} says hi to {FirstName}", recipient);
        Assert.Equal("Bob says hi to Bob", result);
    }

    [Fact]
    public void ReplacePlaceholders_NullTemplate_ThrowsNullRef()
    {
        var recipient = new Recipient { Email = "a@t.com" };
        Assert.Throws<NullReferenceException>(() => MailService.ReplacePlaceholders(null!, recipient));
    }

    [Theory]
    [InlineData("abcd efgh ijkl mnop", true)]
    [InlineData("abcdefghijklmnop", true)]
    [InlineData("ABCD EFGH IJKL MNOP", true)]
    [InlineData("abcd-efgh-ijkl-mnop", false)]
    [InlineData("abcd efgh ijkl mno", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("1234567890123456", true)]
    [InlineData("1234 5678 9012 345!", false)]
    public void IsValidAppPassword_VariousInputs_ReturnsExpected(string? password, bool expected)
    {
        Assert.Equal(expected, MailService.IsValidAppPassword(password));
    }
}
