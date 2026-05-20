using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net.Mail;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using ChiaMail.Models;
using ChiaMail.Services;
using Microsoft.Win32;

namespace ChiaMail;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<string> _statusLog = new();
    private readonly ObservableCollection<FileInfo> _attachments = new();
    private readonly FileLogger _fileLogger;
    private List<Recipient> _recipients = new();
    private CancellationTokenSource? _cts;
    private bool _isSending;

    public MainWindow()
    {
        _fileLogger = new FileLogger(Path.Combine(AppContext.BaseDirectory, "ChiaMail.log"));

        InitializeComponent();
        StatusLogListBox.ItemsSource = _statusLog;
        AttachmentListBox.ItemsSource = _attachments;

        _statusLog.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems)
                    _fileLogger.Write(item?.ToString() ?? "");
            }
        };

        UpdateSendButtonState();
        CancelButton.Visibility = Visibility.Collapsed;

        FromEmailTextBox.TextChanged += (_, _) => UpdateSendButtonState();
        AppPasswordBox.PasswordChanged += (_, _) => UpdateSendButtonState();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                e.Handled = true;
                TakeScreenshot(null, null!);
            }
        };
    }

    private void TakeScreenshot(object? sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Screenshot",
                Filter = "PNG Image (*.png)|*.png",
                DefaultExt = ".png",
                FileName = $"ChiaMail_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };

            if (dialog.ShowDialog() != true) return;

            var dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? new System.Windows.Media.Matrix(1, 0, 0, 1, 0, 0);
            var dpiX = dpiScale.M11 * 96.0;
            var dpiY = dpiScale.M22 * 96.0;

            var bitmap = new RenderTargetBitmap(
                (int)ActualWidth, (int)ActualHeight,
                dpiX, dpiY,
                System.Windows.Media.PixelFormats.Default);

            bitmap.Render(this);

            using var fs = new FileStream(dialog.FileName, FileMode.Create);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(fs);

            _statusLog.Add($"✓ Screenshot saved: {dialog.FileName}");
            ScrollStatusLogToBottom();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save screenshot:{Environment.NewLine}{ex.Message}",
                "Screenshot Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        var helpPath = Path.Combine(AppContext.BaseDirectory, "docs", "index.html");

        if (File.Exists(helpPath))
        {
            Process.Start(new ProcessStartInfo(helpPath) { UseShellExecute = true });
        }
        else
        {
            MessageBox.Show($"User guide not found at:{Environment.NewLine}{helpPath}",
                "Help Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void BrowseCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select CSV File",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
            LoadCsvFile(dialog.FileName);
    }

    private void LoadCsvFile(string path)
    {
        try
        {
            var (recipients, headers, rawRows, errors) = CsvService.Parse(path);
            _recipients = recipients;
            CsvPathTextBox.Text = path;

            var table = new DataTable();
            foreach (var h in headers)
                table.Columns.Add(h);

            foreach (var row in rawRows)
            {
                var dr = table.NewRow();
                for (int i = 0; i < row.Length && i < headers.Length; i++)
                    dr[i] = row[i];
                table.Rows.Add(dr);
            }

            CsvPreviewGrid.ItemsSource = table.DefaultView;
            CsvPreviewBorder.Visibility = Visibility.Visible;
            CsvEmptyHint.Visibility = Visibility.Collapsed;
            RecipientCountText.Text = $"{recipients.Count} valid recipient(s) loaded";
            RecipientCountText.Visibility = Visibility.Visible;

            if (errors.Count > 0)
            {
                var errorSummary = string.Join(Environment.NewLine, errors.Take(5));
                if (errors.Count > 5)
                    errorSummary += $"{Environment.NewLine}({errors.Count - 5} more warning(s) not shown)";
                MessageBox.Show($"CSV loaded with {errors.Count} issue(s):{Environment.NewLine}{Environment.NewLine}{errorSummary}",
                    "CSV Warnings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (FileNotFoundException ex)
        {
            MessageBox.Show($"File not found: {ex.FileName}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ClearCsvPreview();
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show("Access denied. The file may be in use or permissions are restricted.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ClearCsvPreview();
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show($"Invalid CSV data:{Environment.NewLine}{ex.Message}",
                "CSV Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ClearCsvPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unexpected error loading CSV:{Environment.NewLine}{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ClearCsvPreview();
        }

        UpdateSendButtonState();
    }

    private void ClearCsvPreview()
    {
        _recipients.Clear();
        CsvPreviewGrid.ItemsSource = null;
        CsvPreviewBorder.Visibility = Visibility.Collapsed;
        CsvEmptyHint.Visibility = Visibility.Visible;
        RecipientCountText.Visibility = Visibility.Collapsed;
        CsvPathTextBox.Text = string.Empty;
    }

    private void BrowseLogo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Logo Image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
            LoadLogo(dialog.FileName);
    }

    private void LoadLogo(string path)
    {
        if (!EmailComposer.IsSupportedImage(path))
        {
            MessageBox.Show($"Unsupported image format.{Environment.NewLine}Supported: PNG, JPG, GIF, BMP, WebP, SVG",
                "Invalid Image", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LogoPathTextBox.Text = path;
        LogoPreviewBorder.Visibility = Visibility.Visible;

        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            LogoPreviewImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            LogoPreviewBorder.Visibility = Visibility.Collapsed;
            LogoPathTextBox.Text = string.Empty;
            MessageBox.Show($"Failed to load logo image:{Environment.NewLine}{ex.Message}",
                "Image Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddAttachments_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Files to Attach",
            Multiselect = true,
            Filter = "All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var path in dialog.FileNames)
            {
                var fi = new FileInfo(path);
                if (!_attachments.Any(a => a.FullName.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    _attachments.Add(fi);
            }
            UpdateAttachmentVisibility();
        }
    }

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is FileInfo fi)
        {
            _attachments.Remove(fi);
            UpdateAttachmentVisibility();
        }
    }

    private void UpdateAttachmentVisibility()
    {
        AttachmentListBox.Visibility = _attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DelayTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    private void DelayTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(DataFormats.Text))
        {
            var text = e.DataObject.GetData(DataFormats.Text) as string;
            if (text is null || !int.TryParse(text.Trim(), out _))
                e.CancelCommand();
        }
        else
        {
            e.CancelCommand();
        }
    }

    private void PreviewEmail_Click(object sender, RoutedEventArgs e)
    {
        if (_recipients.Count == 0)
        {
            MessageBox.Show("Load a CSV file with recipients first.", "Preview",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var email = FromEmailTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(email) || !CsvService.IsValidEmail(email))
        {
            MessageBox.Show("Enter a valid Gmail email address first.", "Preview",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var recipient = _recipients[0];
        var resolvedSubject = MailService.ReplacePlaceholders(SubjectTextBox.Text.Trim(), recipient);
        var resolvedBody = MailService.ReplacePlaceholders(BodyTextBox.Text, recipient);

        // Remove {Logo} from subject, replace in body
        resolvedSubject = resolvedSubject.Replace("{Logo}", "");

        if (resolvedBody.Contains("{Logo}"))
        {
            if (IncludeLogoCheckBox.IsChecked == true && !string.IsNullOrEmpty(LogoPathTextBox.Text))
                resolvedBody = resolvedBody.Replace("{Logo}", EmailComposer.GetLogoImageTag());
            else
                resolvedBody = resolvedBody.Replace("{Logo}", "");
        }

        var isHtml = HtmlRadio.IsChecked == true;
        var hasLogo = IncludeLogoCheckBox.IsChecked == true && !string.IsNullOrEmpty(LogoPathTextBox.Text);
        int attachmentCount = _attachments.Count;

        var preview = new PreviewWindow(
            email, recipient.Email,
            resolvedSubject, resolvedBody,
               isHtml, 
               (IncludeLogoCheckBox.IsChecked == true ? LogoPathTextBox.Text : null),
               attachmentCount);
        preview.ShowDialog();
    }

    private void InsertPlaceholder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            var text = $"{{{tag}}}";

            if (Keyboard.FocusedElement == SubjectTextBox)
                InsertIntoTextBox(SubjectTextBox, text);
            else
                InsertIntoTextBox(BodyTextBox, text);
        }
    }

    private static void InsertIntoTextBox(TextBox textBox, string text)
    {
        var idx = textBox.SelectionStart;
        textBox.Text = textBox.Text.Insert(idx, text);
        textBox.SelectionStart = idx + text.Length;
        textBox.Focus();
    }

    private void BodyMode_Changed(object sender, RoutedEventArgs e)
    {
        if (BodyTextBox is null) return;
        BodyTextBox.ToolTip = HtmlRadio.IsChecked == true
            ? "Paste your HTML email here... Use {FirstName} and {LastName} as placeholders."
            : "Write your email body here... Use {FirstName} and {LastName} as placeholders.";
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var email = FromEmailTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(email))
        {
            MessageBox.Show("Please enter your Gmail email address.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!CsvService.IsValidEmail(email))
        {
            MessageBox.Show($"'{email}' is not a valid email address.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(AppPasswordBox.Password))
        {
            MessageBox.Show("Please enter your Gmail app password.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!MailService.IsValidAppPassword(AppPasswordBox.Password))
        {
            MessageBox.Show("Please enter a valid Gmail App Password. It should be 16 letters/digits long; spaces are allowed.",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TestButton.IsEnabled = false;
        TestButton.Content = "Testing...";

        try
        {
            if (SendTestMailCheckBox.IsChecked == true)
            {
                using var svc = new MailService(email, AppPasswordBox.Password);
                await svc.TestConnectionAsync(CancellationToken.None);
                MessageBox.Show("Connection successful! Check your Gmail inbox for a test message.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                await MailService.CheckSmtpServerAsync(CancellationToken.None);
                MessageBox.Show("Gmail SMTP server is reachable. Your credentials were not tested — " +
                    "tick \"Send test email\" to verify authentication.",
                    "Server Reachable", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (SmtpException ex)
        {
            var detail = BuildErrorDetail(ex);
            _statusLog.Add(detail);
            ScrollStatusLogToBottom();
            MessageBox.Show($"SMTP error:{Environment.NewLine}{detail}{Environment.NewLine}{Environment.NewLine}" +
                "Make sure:{Environment.NewLine}" +
                "• 2FA is enabled on your Google account{Environment.NewLine}" +
                "• You're using an App Password (16 chars, no spaces){Environment.NewLine}" +
                "• The email address is correct",
                "SMTP Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            var detail = BuildErrorDetail(ex);
            _statusLog.Add(detail);
            ScrollStatusLogToBottom();
            MessageBox.Show($"Connection failed:{Environment.NewLine}{detail}{Environment.NewLine}{Environment.NewLine}" +
                "Make sure:{Environment.NewLine}" +
                "• 2FA is enabled on your Google account{Environment.NewLine}" +
                "• You're using an App Password (not your regular password){Environment.NewLine}" +
                "• The email address is correct",
                "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestButton.IsEnabled = true;
            TestButton.Content = "Test Connection";
        }
    }

    private async void TestSelf_Click(object sender, RoutedEventArgs e)
    {
        if (_isSending) return;

        var email = FromEmailTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(email) || !CsvService.IsValidEmail(email))
        {
            MessageBox.Show("Enter a valid Gmail email address first.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(AppPasswordBox.Password))
        {
            MessageBox.Show("Enter your Gmail app password first.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!MailService.IsValidAppPassword(AppPasswordBox.Password))
        {
            MessageBox.Show("Please enter a valid Gmail App Password. It should be 16 letters/digits long; spaces are allowed.",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(SubjectTextBox.Text.Trim()) && string.IsNullOrWhiteSpace(BodyTextBox.Text))
        {
            MessageBox.Show("Enter a subject and/or body before sending a test.",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TestSelfButton.IsEnabled = false;
        TestSelfButton.Content = "Sending...";

        try
        {
            _statusLog.Add($"⏳ Sending test email to {email}...");
            ScrollStatusLogToBottom();

            var self = new List<Recipient> { new() { Email = email } };
            var logoPath = IncludeLogoCheckBox.IsChecked == true ? LogoPathTextBox.Text : null;
            var attachmentPaths = _attachments.Select(a => a.FullName).ToList();

            using var svc = new MailService(email, AppPasswordBox.Password);
            await svc.SendBulkAsync(
                self,
                SubjectTextBox.Text.Trim(),
                BodyTextBox.Text,
                HtmlRadio.IsChecked == true,
                logoPath,
                null,
                CancellationToken.None,
                delaySeconds: 0,
                attachmentPaths: attachmentPaths);

            _statusLog.Add($"✓ Test email sent to {email}");
            MessageBox.Show($"Test email sent to {email}.{Environment.NewLine}Check your inbox (and spam folder).",
                "Test Sent", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            var detail = BuildErrorDetail(ex);
            _statusLog.Add($"✗ Test failed: {detail}");
            MessageBox.Show($"Failed to send test email:{Environment.NewLine}{detail}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScrollStatusLogToBottom();
            TestSelfButton.IsEnabled = true;
            TestSelfButton.Content = "Send Test to Self";
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_isSending) return;

        _isSending = true;
        SendButton.IsEnabled = false;

        var validationError = ValidateSendInputs();
        if (validationError is not null)
        {
            MessageBox.Show(validationError, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            _isSending = false;
            SendButton.IsEnabled = true;
            return;
        }

        var email = FromEmailTextBox.Text.Trim();
        var password = AppPasswordBox.Password;
        var subject = SubjectTextBox.Text.Trim();
        var body = BodyTextBox.Text;
        var confirmDetails = BuildConfirmationDetails(email, subject);

        var confirm = MessageBox.Show(confirmDetails, "Confirm Send",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            _isSending = false;
            SendButton.IsEnabled = true;
            return;
        }

        _cts = new CancellationTokenSource();
        _statusLog.Clear();
        SendButton.Content = "Sending...";
        TestSelfButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        CancelButton.IsEnabled = true;
        CancelButton.Content = "Cancel";
        ProgressText.Visibility = Visibility.Visible;
        SendProgressBar.Value = 0;

        if (!int.TryParse(DelayTextBox.Text.Trim(), out var delaySeconds) || delaySeconds < 0)
        {
            MessageBox.Show("Delay must be a non-negative whole number (seconds).",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            _isSending = false;
            SendButton.Content = "Send All Emails";
            SendButton.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
            ProgressText.Visibility = Visibility.Collapsed;
            return;
        }

        var progress = new Progress<MailProgress>(OnProgress);
        MailService? svc = null;

        try
        {
            // Pre-flight: verify SMTP server is reachable before attempting bulk send
            _statusLog.Add("⏳ Checking SMTP server connectivity...");
            ScrollStatusLogToBottom();
            await MailService.CheckSmtpServerAsync(_cts.Token);
            _statusLog.Add("✓ SMTP server reachable");

            svc = new MailService(email, password);
            var logoPath = IncludeLogoCheckBox.IsChecked == true ? LogoPathTextBox.Text : null;
            var attachmentPaths = _attachments.Select(a => a.FullName).ToList();

            await svc.SendBulkAsync(
                _recipients,
                subject,
                body,
                HtmlRadio.IsChecked == true,
                logoPath,
                progress,
                _cts.Token,
                delaySeconds: delaySeconds,
                attachmentPaths: attachmentPaths);

            _statusLog.Add("━━━ All done ━━━");
        }
        catch (OperationCanceledException)
        {
            _statusLog.Add("━━━ Cancelled by user ━━━");
        }
        catch (SmtpException ex)
        {
            _statusLog.Add($"━━━ SMTP Error: {ex.Message} ━━━");
        }
        catch (Exception ex)
        {
            _statusLog.Add($"━━━ Error: {ex.Message} ━━━");
            MessageBox.Show($"An unexpected error occurred:{Environment.NewLine}{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            svc?.Dispose();
            _isSending = false;
            _cts?.Dispose();
            _cts = null;
            SendButton.Content = "Send All Emails";
            SendButton.IsEnabled = true;
            TestSelfButton.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
            UpdateSendButtonState();
            ScrollStatusLogToBottom();
        }
    }

    private string? ValidateSendInputs()
    {
        var email = FromEmailTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(email))
            return "Please enter your Gmail email address.";

        if (!CsvService.IsValidEmail(email))
            return $"'{email}' is not a valid email address.";

        if (string.IsNullOrWhiteSpace(AppPasswordBox.Password))
            return "Please enter your Gmail app password.";

        if (!MailService.IsValidAppPassword(AppPasswordBox.Password))
            return "Please enter a valid Gmail App Password. It should be 16 letters/digits long; spaces are allowed.";

        if (_recipients.Count == 0)
            return "Please load a CSV file with valid recipients.";

        if (string.IsNullOrWhiteSpace(SubjectTextBox.Text))
            return "Please enter an email subject.";

        if (string.IsNullOrWhiteSpace(BodyTextBox.Text))
        {
            var result = MessageBox.Show("The email body is empty. Send anyway?",
                "Empty Body", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return "Cancelled by user.";
        }

        return null;
    }

    private string BuildConfirmationDetails(string email, string subject)
    {
        var details = $"Send {_recipients.Count} email(s)?{Environment.NewLine}{Environment.NewLine}" +
                      $"From: {email}{Environment.NewLine}" +
                      $"Subject: {subject}{Environment.NewLine}" +
                      $"Mode: {(HtmlRadio.IsChecked == true ? "HTML" : "Plain Text")}{Environment.NewLine}" +
                      $"Recipients: {_recipients.Count}{Environment.NewLine}" +
                      $"Logo: {(IncludeLogoCheckBox.IsChecked == true && !string.IsNullOrEmpty(LogoPathTextBox.Text) ? "Yes" : "No")}{Environment.NewLine}" +
                      $"Attachments: {(_attachments.Count > 0 ? $"{_attachments.Count} file(s)" : "None")}{Environment.NewLine}" +
                      $"Delay: {DelayTextBox.Text.Trim()} sec between emails";

        return details;
    }

    private void OnProgress(MailProgress p)
    {
        Dispatcher.Invoke(() =>
        {
            SendProgressBar.Maximum = p.Total;
            SendProgressBar.Value = p.Current;

            if (p.Total > 0)
            {
                var pct = (int)((double)p.Current / p.Total * 100);
                ProgressText.Text = $"{p.Current} / {p.Total}  ({pct}%)";
            }

            _statusLog.Add(p.LastResult ?? string.Empty);
            ScrollStatusLogToBottom();
        });
    }

    private static string BuildErrorDetail(Exception ex)
    {
        var parts = new List<string> { ex.Message };
        if (ex is SmtpException smtp)
            parts.Add($"StatusCode: {smtp.StatusCode}");
        if (ex.InnerException != null)
            parts.Add($"Inner: {ex.InnerException.Message}");
        return string.Join(" | ", parts);
    }

    private void ScrollStatusLogToBottom()
    {
        if (StatusLogListBox.Items.Count > 0)
            StatusLogListBox.ScrollIntoView(StatusLogListBox.Items[^1]);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        CancelButton.IsEnabled = false;
        CancelButton.Content = "Cancelling...";
    }

    private void UpdateSendButtonState()
    {
        var hasCredentials = !string.IsNullOrWhiteSpace(FromEmailTextBox.Text)
                          && AppPasswordBox.Password.Length > 0;

        SendButton.IsEnabled = !_isSending
            && _recipients.Count > 0
            && hasCredentials;

        TestSelfButton.IsEnabled = !_isSending
            && hasCredentials;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isSending)
        {
            var result = MessageBox.Show("Sending is in progress. Exit anyway?",
                "Confirm Exit", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _cts?.Cancel();
        }
        base.OnClosing(e);
    }
}
