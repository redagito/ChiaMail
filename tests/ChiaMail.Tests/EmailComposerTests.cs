using System.IO;
using ChiaMail.Services;

namespace ChiaMail.Tests;

public sealed class EmailComposerTests
{
    // ─── BuildHtmlBody ─────────────────────────────────────────

    [Fact]
    public void BuildHtmlBody_PlainText_WrapsInHtmlTemplate()
    {
        var result = EmailComposer.BuildHtmlBody("Hello World", isHtml: false);

        Assert.StartsWith("<!DOCTYPE html>", result);
        Assert.Contains("<body", result);
        Assert.Contains("Hello World", result);
        Assert.Contains("</body>", result);
        Assert.Contains("</html>", result);
    }

    [Fact]
    public void BuildHtmlBody_PlainTextWithNewlines_ConvertsToBr()
    {
        var result = EmailComposer.BuildHtmlBody("Line1\r\nLine2\nLine3", isHtml: false);

        Assert.Contains("Line1<br/>", result);
        Assert.Contains("<br/>Line2<br/>", result);
        Assert.Contains("<br/>Line3", result);
    }

    [Fact]
    public void BuildHtmlBody_PlainTextSpecialChars_EscapesHtml()
    {
        var result = EmailComposer.BuildHtmlBody("<b>bold</b> & \"quotes\"", isHtml: false);

        Assert.DoesNotContain("<b>", result);
        Assert.Contains("&lt;b&gt;", result);
        Assert.Contains("&amp;", result);
    }

    [Fact]
    public void BuildHtmlBody_HtmlMode_DoesNotEscape()
    {
        var result = EmailComposer.BuildHtmlBody("<b>bold</b>", isHtml: true);

        Assert.Contains("<b>bold</b>", result);
    }

    [Fact]
    public void BuildHtmlBody_HtmlAlreadyWrapped_DoesNotReWrap()
    {
        var input = "<!DOCTYPE html><html><body><p>Hi</p></body></html>";
        var result = EmailComposer.BuildHtmlBody(input, isHtml: true);

        Assert.Equal(input, result);
    }

    [Fact]
    public void BuildHtmlBody_HtmlWithHtmlTag_DoesNotReWrap()
    {
        var input = "<html><body><p>Hi</p></body></html>";
        var result = EmailComposer.BuildHtmlBody(input, isHtml: true);

        Assert.Equal(input, result);
    }

    [Fact]
    public void BuildHtmlBody_WithLogoPath_AppendsImageBeforeBodyClose()
    {
        var tempLogo = Path.GetTempFileName() + ".png";
        try
        {
            File.WriteAllBytes(tempLogo, [0x89, 0x50, 0x4E, 0x47]); // PNG header
            var result = EmailComposer.BuildHtmlBody("Hello", isHtml: false, logoPath: tempLogo);

            Assert.Contains("<img src=\"cid:logoImage\"", result);
            Assert.Contains("</body>", result);
            // Image should be before </body>
            var imgIdx = result.IndexOf("cid:logoImage", StringComparison.Ordinal);
            var bodyEndIdx = result.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            Assert.True(imgIdx < bodyEndIdx, "Logo img tag should be before </body>");
        }
        finally
        {
            if (File.Exists(tempLogo)) File.Delete(tempLogo);
        }
    }

    [Fact]
    public void BuildHtmlBody_NullBody_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => EmailComposer.BuildHtmlBody(null!, isHtml: false));
    }

    [Fact]
    public void BuildHtmlBody_EmptyBody_StillWorks()
    {
        var result = EmailComposer.BuildHtmlBody("", isHtml: false);
        Assert.Contains("<body", result);
    }

    // ─── GetMimeType ───────────────────────────────────────────

    [Theory]
    [InlineData("logo.jpg", "image/jpeg")]
    [InlineData("logo.jpeg", "image/jpeg")]
    [InlineData("logo.png", "image/png")]
    [InlineData("logo.gif", "image/gif")]
    [InlineData("logo.bmp", "image/bmp")]
    [InlineData("logo.webp", "image/webp")]
    [InlineData("logo.svg", "image/svg+xml")]
    [InlineData("logo.ico", "image/x-icon")]
    [InlineData("logo.tiff", "image/tiff")]
    [InlineData("logo.tif", "image/tiff")]
    [InlineData("LOGO.PNG", "image/png")]
    public void GetMimeType_KnownExtensions_ReturnsCorrectType(string filename, string expected)
    {
        Assert.Equal(expected, EmailComposer.GetMimeType(filename));
    }

    [Fact]
    public void GetMimeType_NullPath_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => EmailComposer.GetMimeType(null!));
    }

    [Fact]
    public void GetMimeType_EmptyPath_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => EmailComposer.GetMimeType(""));
    }

    [Fact]
    public void GetMimeType_UnknownExtension_ThrowsNotSupportedException()
    {
        var ex = Assert.Throws<NotSupportedException>(() => EmailComposer.GetMimeType("logo.pdf"));
        Assert.Contains("Unsupported", ex.Message);
    }

    [Fact]
    public void GetMimeType_NoExtension_ThrowsNotSupportedException()
    {
        var ex = Assert.Throws<NotSupportedException>(() => EmailComposer.GetMimeType("logo"));
        Assert.Contains("Unsupported", ex.Message);
    }

    // ─── IsSupportedImage ──────────────────────────────────────

    [Fact]
    public void IsSupportedImage_NullPath_ReturnsFalse()
    {
        Assert.False(EmailComposer.IsSupportedImage(null!));
    }

    [Fact]
    public void IsSupportedImage_EmptyPath_ReturnsFalse()
    {
        Assert.False(EmailComposer.IsSupportedImage(""));
    }

    [Fact]
    public void IsSupportedImage_NonExistentFile_ReturnsFalse()
    {
        Assert.False(EmailComposer.IsSupportedImage(@"C:\nonexistent\logo.png"));
    }

    [Fact]
    public void IsSupportedImage_ValidExtensionNoFile_ReturnsFalse()
    {
        Assert.False(EmailComposer.IsSupportedImage(@"C:\nonexistent.png"));
    }

    // ─── Additional edge cases ─────────────────────────────────

    [Fact]
    public void BuildHtmlBody_StartsWithHtmlTag_DoesNotReWrap()
    {
        var input = "<html><body><p>No body here</p></body></html>";
        var result = EmailComposer.BuildHtmlBody(input, isHtml: true);

        Assert.Equal(input, result);
    }

    [Fact]
    public void BuildHtmlBody_StartsWithHtmlTagMissingBody_DoesNotReWrap()
    {
        var input = "<html><p>No body tag</p></html>";
        var result = EmailComposer.BuildHtmlBody(input, isHtml: true);

        Assert.Equal(input, result);
    }

    [Fact]
    public void BuildHtmlBody_BodyContainsClosingBodyText_HtmlEncodedSoLogoInsertedCorrectly()
    {
        var tempLogo = Path.GetTempFileName() + ".png";
        try
        {
            File.WriteAllBytes(tempLogo, [0x89, 0x50, 0x4E, 0x47]);
            // </body> in input gets HTML-escaped to &lt;/body&gt; in plain-text mode
            var result = EmailComposer.BuildHtmlBody("See </body> in text", isHtml: false, logoPath: tempLogo);

            // Only the template's </body> exists (input is escaped)
            Assert.Contains("&lt;/body&gt;", result);
            var count = 0;
            int idx = -1;
            while ((idx = result.IndexOf("</body>", idx + 1, StringComparison.OrdinalIgnoreCase)) >= 0)
                count++;
            Assert.Equal(1, count);
            Assert.True(result.LastIndexOf("cid:logoImage", StringComparison.Ordinal) <
                        result.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(tempLogo)) File.Delete(tempLogo);
        }
    }

    [Fact]
    public void BuildHtmlBody_LogoPathNull_NoImageTag()
    {
        var result = EmailComposer.BuildHtmlBody("Hello", isHtml: false, logoPath: null);
        Assert.DoesNotContain("cid:logoImage", result);
    }

    [Fact]
    public void BuildHtmlBody_LogoPathEmptyString_NoImageTag()
    {
        var result = EmailComposer.BuildHtmlBody("Hello", isHtml: false, logoPath: "");
        Assert.DoesNotContain("cid:logoImage", result);
    }

    [Fact]
    public void BuildHtmlBody_HtmlModePreWrappedWithLogo_LogoBeforeBodyClose()
    {
        var tempLogo = Path.GetTempFileName() + ".png";
        try
        {
            File.WriteAllBytes(tempLogo, [0x89, 0x50, 0x4E, 0x47]);
            var input = "<!DOCTYPE html><html><body><p>Hello</p></body></html>";
            var result = EmailComposer.BuildHtmlBody(input, isHtml: true, logoPath: tempLogo);

            Assert.Contains("cid:logoImage", result);
            var imgIdx = result.IndexOf("cid:logoImage", StringComparison.Ordinal);
            var bodyEndIdx = result.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            Assert.True(imgIdx < bodyEndIdx);
        }
        finally
        {
            if (File.Exists(tempLogo)) File.Delete(tempLogo);
        }
    }

    [Fact]
    public void BuildHtmlBody_VeryLongBody_DoesNotThrow()
    {
        var longBody = new string('x', 100_000);
        var result = EmailComposer.BuildHtmlBody(longBody, isHtml: false);
        Assert.Contains(longBody, result);
    }

    [Fact]
    public void BuildHtmlBody_BodyWithOnlyWhitespace_StillWraps()
    {
        var result = EmailComposer.BuildHtmlBody("   ", isHtml: false);
        Assert.Contains("<body", result);
    }

    [Fact]
    public void GetMimeType_MultipleDots_PicksLastExtension()
    {
        Assert.Equal("image/png", EmailComposer.GetMimeType("file.tar.png"));
    }

    [Fact]
    public void IsSupportedImage_ValidExistingFile_ReturnsTrue()
    {
        var tempLogo = Path.GetTempFileName() + ".png";
        try
        {
            File.WriteAllBytes(tempLogo, [0x89, 0x50, 0x4E, 0x47]);
            Assert.True(EmailComposer.IsSupportedImage(tempLogo));
        }
        finally
        {
            if (File.Exists(tempLogo)) File.Delete(tempLogo);
        }
    }

    // ─── GetLogoImageTag ─────────────────────────────────────

    [Fact]
    public void GetLogoImageTag_ReturnsValidImgTag()
    {
        var result = EmailComposer.GetLogoImageTag();
        Assert.StartsWith("<img", result);
        Assert.Contains("cid:logoImage", result);
    }

    // ─── BuildHtmlBody with pre-existing cid:logoImage ────────

    [Fact]
    public void BuildHtmlBody_BodyAlreadyHasCidLogo_DoesNotDuplicate()
    {
        var bodyWithLogo = $"<p>Hello</p>{EmailComposer.GetLogoImageTag()}";
        // Build with a real logo file so logoPath is valid
        var tempLogo = Path.GetTempFileName() + ".png";
        try
        {
            File.WriteAllBytes(tempLogo, [0x89, 0x50, 0x4E, 0x47]);
            var result = EmailComposer.BuildHtmlBody(bodyWithLogo, isHtml: true, logoPath: tempLogo);

            // Should only appear once
            var first = result.IndexOf("cid:logoImage", StringComparison.Ordinal);
            var last = result.LastIndexOf("cid:logoImage", StringComparison.Ordinal);
            Assert.Equal(first, last);
        }
        finally
        {
            if (File.Exists(tempLogo)) File.Delete(tempLogo);
        }
    }

    [Fact]
    public void BuildHtmlBody_PlainTextWithLogoPlaceholder_ImgTagNotEscaped()
    {
        var tempLogo = Path.GetTempFileName() + ".png";
        try
        {
            File.WriteAllBytes(tempLogo, [0x89, 0x50, 0x4E, 0x47]);
            var result = EmailComposer.BuildHtmlBody(
                "Hello, logo: {Logo}", isHtml: false, logoPath: tempLogo);

            // The img tag must NOT be HTML-escaped (would render as literal text)
            Assert.Contains("<img src=\"cid:logoImage\"", result);
            // The {Logo} text should be gone
            Assert.DoesNotContain("{Logo}", result);
            // Only one cid:logoImage (replaced, not duplicated by auto-append)
            var first = result.IndexOf("cid:logoImage", StringComparison.Ordinal);
            var last = result.LastIndexOf("cid:logoImage", StringComparison.Ordinal);
            Assert.Equal(first, last);
        }
        finally
        {
            if (File.Exists(tempLogo)) File.Delete(tempLogo);
        }
    }

    [Fact]
    public void BuildHtmlBody_MultipleLogoPlaceholders_AllReplaced()
    {
        var tempLogo = Path.GetTempFileName() + ".png";
        try
        {
            File.WriteAllBytes(tempLogo, [0x89, 0x50, 0x4E, 0x47]);
            var body = "{Logo} top {Logo} middle {Logo} bottom";
            var result = EmailComposer.BuildHtmlBody(body, isHtml: false, logoPath: tempLogo);

            Assert.DoesNotContain("{Logo}", result);
            Assert.Equal(3, CountOccurrences(result, "cid:logoImage"));
        }
        finally
        {
            if (File.Exists(tempLogo)) File.Delete(tempLogo);
        }
    }

    [Fact]
    public void BuildHtmlBody_HtmlModeNoBodyCloseWithLogo_AppendsAtEnd()
    {
        var tempLogo = Path.GetTempFileName() + ".png";
        try
        {
            File.WriteAllBytes(tempLogo, [0x89, 0x50, 0x4E, 0x47]);
            // Starts with <html> so no re-wrap, and has no </body>
            var result = EmailComposer.BuildHtmlBody(
                "<html><p>Hello</p>", isHtml: true, logoPath: tempLogo);

            Assert.Contains("cid:logoImage", result);
            // Logo is appended at the very end (no </body> to insert before)
            Assert.Contains("<br/><img src=\"cid:logoImage\"", result);
        }
        finally
        {
            if (File.Exists(tempLogo)) File.Delete(tempLogo);
        }
    }

    [Fact]
    public void BuildHtmlBody_HtmlModeNoHtmlTagNoBodyCloseWithLogo_WrapsThenAutoAppends()
    {
        var tempLogo = Path.GetTempFileName() + ".png";
        try
        {
            File.WriteAllBytes(tempLogo, [0x89, 0x50, 0x4E, 0x47]);
            // No <html> prefix, gets wrapped, then logo inserted before </body>
            var result = EmailComposer.BuildHtmlBody(
                "<p>Hello</p>", isHtml: true, logoPath: tempLogo);

            Assert.Contains("<html>", result);
            Assert.Contains("cid:logoImage", result);
            // Logo should be before </body>
            Assert.True(result.IndexOf("cid:logoImage", StringComparison.Ordinal) <
                        result.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(tempLogo)) File.Delete(tempLogo);
        }
    }

    [Fact]
    public void BuildHtmlBody_LogoPathNullWithLogoPlaceholder_PlaceholderRemoved()
    {
        var result = EmailComposer.BuildHtmlBody("Logo: {Logo}", isHtml: false, logoPath: null);

        Assert.DoesNotContain("{Logo}", result);
        Assert.DoesNotContain("cid:logoImage", result);
    }

    [Fact]
    public void GetLogoImageTag_ReturnsValidFormat()
    {
        var tag = EmailComposer.GetLogoImageTag();
        Assert.StartsWith("<img", tag);
        Assert.Contains("src=\"cid:logoImage\"", tag);
        Assert.Contains("alt=\"Logo\"", tag);
    }

    [Fact]
    public void IsSupportedImage_UnsupportedExtensionOnExistingFile_ReturnsFalse()
    {
        var temp = Path.GetTempFileName() + ".pdf";
        try
        {
            File.WriteAllBytes(temp, [0x25, 0x50, 0x44, 0x46]);
            Assert.False(EmailComposer.IsSupportedImage(temp));
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    [Fact]
    public void BuildHtmlBody_PlainTextWithAmpersand_EscapesCorrectly()
    {
        var result = EmailComposer.BuildHtmlBody("A & B < C", isHtml: false);
        Assert.Contains("A &amp; B &lt; C", result);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0, idx = -1;
        while ((idx = text.IndexOf(value, idx + 1, StringComparison.Ordinal)) >= 0)
            count++;
        return count;
    }
}
