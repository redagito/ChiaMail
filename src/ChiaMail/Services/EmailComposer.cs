using System.IO;
using System.Net;
using System.Net.Mime;

namespace ChiaMail.Services;

public static class EmailComposer
{
    public static string GetLogoImageTag()
    {
        return "<img src=\"cid:logoImage\" alt=\"Logo\" style=\"max-width:240px;height:auto;margin-top:20px;\"/>";
    }

    public static string BuildHtmlBody(string body, bool isHtml, string? logoPath = null)
    {
        if (body is null)
            throw new ArgumentNullException(nameof(body));

        string htmlInner;

        if (isHtml)
        {
            htmlInner = body;
        }
        else
        {
            var escaped = WebUtility.HtmlEncode(body);
            htmlInner = escaped.Replace("\r\n", "<br/>").Replace("\n", "<br/>");
        }

        if (!htmlInner.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) &&
            !htmlInner.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            htmlInner =
                $"<!DOCTYPE html>{Environment.NewLine}<html>{Environment.NewLine}<head><meta charset=\"UTF-8\"></head>{Environment.NewLine}" +
                $"<body style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:14px;color:#202124;line-height:1.6;margin:20px;\">{Environment.NewLine}" +
                $"{htmlInner}{Environment.NewLine}</body>{Environment.NewLine}</html>";
        }

        // Replace {Logo} placeholder — happens after escaping so the img tag
        // is never HTML-escaped (would produce literal tag text instead of rendering)
        if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
        {
            htmlInner = htmlInner.Replace("{Logo}", GetLogoImageTag());
        }
        else
        {
            htmlInner = htmlInner.Replace("{Logo}", "");
        }

        // Auto-append logo before </body> only if {Logo} wasn't placed above
        if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath) && !htmlInner.Contains("cid:logoImage"))
        {
            int bodyEndIdx = htmlInner.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyEndIdx >= 0)
            {
                htmlInner = htmlInner.Insert(bodyEndIdx, $"<br/>{GetLogoImageTag()}{Environment.NewLine}");
            }
            else
            {
                htmlInner += $"{Environment.NewLine}<br/>{GetLogoImageTag()}";
            }
        }

        return htmlInner;
    }

    public static string GetMimeType(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentNullException(nameof(path));

        var ext = Path.GetExtension(path)?.ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => MediaTypeNames.Image.Jpeg,
            ".png" => MediaTypeNames.Image.Png,
            ".gif" => MediaTypeNames.Image.Gif,
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".tiff" or ".tif" => "image/tiff",
            _ => throw new NotSupportedException(
                $"Unsupported image format '{ext ?? "(no extension)"}'. Supported: .jpg, .png, .gif, .bmp, .webp, .svg, .ico, .tiff")
        };
    }

    public static bool IsSupportedImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            GetMimeType(path);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
