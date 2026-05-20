using System.Windows;
using System.IO;
using Microsoft.Web.WebView2.Wpf;

namespace ChiaMail;

public partial class PreviewWindow : Window
{
    private readonly string? _logoPath;

    public PreviewWindow(string from, string to, string subject, string body,
        bool isHtml, string? logoPath, int attachmentCount)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;

        FromText.Text = from;
        ToText.Text = to;
        SubjectText.Text = subject;
        ModeText.Text = isHtml ? "HTML" : "Plain Text";
        _logoPath = logoPath;

        bool hasLogo = !string.IsNullOrEmpty(logoPath);
        if (hasLogo)
        {
            LogoIndicator.Text = "✓ Logo included";
            LogoIndicator.Visibility = Visibility.Visible;
        }

        if (attachmentCount > 0)
        {
            AttachmentIndicator.Text = $"{attachmentCount} attachment(s)";
            AttachmentIndicator.Visibility = Visibility.Visible;
        }

        Loaded += async (_, _) => await InitializeWebViewAsync(body, isHtml);
    }

    private async Task InitializeWebViewAsync(string body, bool isHtml)
    {
        try
        {
            var userDataFolder = Path.Combine(Path.GetTempPath(), "ChiaMail_WebView2");
            var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null, userDataFolder: userDataFolder);
            await BodyPreviewWebView.EnsureCoreWebView2Async(environment);

            var htmlContent = BuildPreviewHtml(body, isHtml);
            BodyPreviewWebView.NavigateToString(htmlContent);
        }
        catch (Exception ex)
        {
            BodyPreviewWebView.NavigateToString(
                $"<html><body style='font-family:sans-serif;color:red;'>" +
                $"<p>Error rendering preview:</p>" +
                $"<pre>{System.Net.WebUtility.HtmlEncode(ex.Message)}</pre>" +
                $"</body></html>");
        }
    }

    private string BuildPreviewHtml(string body, bool isHtml)
    {
        var htmlBody = body;
        if (!isHtml)
        {
            htmlBody = System.Net.WebUtility.HtmlEncode(body)
                .Replace("\r\n", "<br/>").Replace("\n", "<br/>");
            htmlBody = $"<body style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:14px;color:#202124;line-height:1.6;margin:20px;\">{htmlBody}</body>";
        }

        // Replace cid:logoImage with file:// URI if logo exists
        if (!string.IsNullOrEmpty(_logoPath) && File.Exists(_logoPath))
        {
            var logoUri = new Uri(_logoPath).AbsoluteUri;
            htmlBody = htmlBody.Replace("cid:logoImage", logoUri);
        }
        else
        {
            htmlBody = htmlBody.Replace("<img src=\"cid:logoImage\"", "<img src=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==\"");
        }

        return $"<!DOCTYPE html><html>{htmlBody}</html>";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
