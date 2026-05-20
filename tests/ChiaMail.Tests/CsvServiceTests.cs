using System.IO;
using ChiaMail.Services;

namespace ChiaMail.Tests;

public sealed class CsvServiceTests : IDisposable
{
    private readonly string _tempDir;

    public CsvServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ChiaMailTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteCsv(string content)
    {
        var path = Path.Combine(_tempDir, "test.csv");
        File.WriteAllText(path, content);
        return path;
    }

    // ─── Happy path ───────────────────────────────────────────

    [Fact]
    public void Parse_BasicCsv_ReturnsRecipients()
    {
        var path = WriteCsv("Email,FirstName,LastName\r\nalice@test.com,Alice,Johnson\r\nbob@test.com,Bob,Smith");
        var (recipients, headers, rawRows, errors) = CsvService.Parse(path);

        Assert.Equal(2, recipients.Count);
        Assert.Equal("alice@test.com", recipients[0].Email);
        Assert.Equal("Alice", recipients[0].FirstName);
        Assert.Equal("Johnson", recipients[0].LastName);
        Assert.Empty(errors);
    }

    [Fact]
    public void Parse_WithBom_HandlesCorrectly()
    {
        var path = WriteCsv("\uFEFFEmail,FirstName,LastName\r\nalice@test.com,Alice,Johnson");
        var (recipients, _, _, errors) = CsvService.Parse(path);

        Assert.Single(recipients);
        Assert.Equal("alice@test.com", recipients[0].Email);
        Assert.Empty(errors);
    }

    [Fact]
    public void Parse_UnixLineEndings_ParsesCorrectly()
    {
        var path = WriteCsv("Email,FirstName,LastName\nalice@test.com,Alice,Johnson\nbob@test.com,Bob,Smith");
        var (recipients, _, _, _) = CsvService.Parse(path);

        Assert.Equal(2, recipients.Count);
    }

    [Fact]
    public void Parse_QuotedFields_HandlesCommas()
    {
        var path = WriteCsv("Email,FirstName,LastName\r\n\"alice@test.com\",\"Alice,M.\",Johnson\r\nbob@test.com,Bob,Smith");
        var (recipients, _, _, errors) = CsvService.Parse(path);

        Assert.Equal(2, recipients.Count);
        Assert.Equal("Alice,M.", recipients[0].FirstName);
        Assert.Empty(errors);
    }

    [Fact]
    public void Parse_QuotedFieldWithDoubleQuote_HandlesEscape()
    {
        var path = WriteCsv("Email,FirstName,LastName\r\nalice@test.com,\"Alice\"\"Ace\"\"\",Johnson");
        var (recipients, _, _, _) = CsvService.Parse(path);

        Assert.Single(recipients);
        Assert.Equal("Alice\"Ace\"", recipients[0].FirstName);
    }

    [Fact]
    public void Parse_AlternateColumnNames_DetectsCorrectly()
    {
        var path = WriteCsv("EMAIL,FIRST_NAME,LAST NAME\r\nalice@test.com,Alice,Johnson");
        var (recipients, _, _, _) = CsvService.Parse(path);

        Assert.Single(recipients);
        Assert.Equal("alice@test.com", recipients[0].Email);
        Assert.Equal("Alice", recipients[0].FirstName);
        Assert.Equal("Johnson", recipients[0].LastName);
    }

    [Fact]
    public void Parse_ExtraColumns_IgnoresUnknown()
    {
        var path = WriteCsv("Email,FirstName,LastName,Phone,City\r\nalice@test.com,Alice,Johnson,555-0100,NYC");
        var (recipients, headers, _, _) = CsvService.Parse(path);

        Assert.Single(recipients);
        Assert.Equal(5, headers.Length);
        Assert.Equal("alice@test.com", recipients[0].Email);
    }

    [Fact]
    public void Parse_MissingOptionalColumns_SetsEmptyDefaults()
    {
        var path = WriteCsv("Email\r\nalice@test.com\r\nbob@test.com");
        var (recipients, _, _, _) = CsvService.Parse(path);

        Assert.Equal(2, recipients.Count);
        Assert.Equal("", recipients[0].FirstName);
        Assert.Equal("", recipients[0].LastName);
    }

    [Fact]
    public void Parse_TrailingEmptyLines_SkipsGracefully()
    {
        var path = WriteCsv("Email,FirstName,LastName\r\nalice@test.com,Alice,Johnson\r\n\r\n\r\n");
        var (recipients, _, _, _) = CsvService.Parse(path);

        Assert.Single(recipients);
    }

    [Fact]
    public void Parse_WithSpacesInFields_TrimsCorrectly()
    {
        var path = WriteCsv("Email,FirstName,LastName\r\n  alice@test.com  ,  Alice  ,  Johnson  ");
        var (recipients, _, _, _) = CsvService.Parse(path);

        Assert.Single(recipients);
        Assert.Equal("alice@test.com", recipients[0].Email);
        Assert.Equal("Alice", recipients[0].FirstName);
        Assert.Equal("Johnson", recipients[0].LastName);
    }

    // ─── Fail-fast: input validation ───────────────────────────

    [Fact]
    public void Parse_NullPath_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CsvService.Parse(null!));
        Assert.Contains("File path cannot be empty", ex.Message);
    }

    [Fact]
    public void Parse_EmptyPath_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CsvService.Parse(""));
        Assert.Contains("File path cannot be empty", ex.Message);
    }

    [Fact]
    public void Parse_WhitespacePath_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CsvService.Parse("   "));
        Assert.Contains("File path cannot be empty", ex.Message);
    }

    [Fact]
    public void Parse_NonExistentFile_ThrowsFileNotFoundException()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => CsvService.Parse(@"C:\does_not_exist.csv"));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void Parse_EmptyFile_ThrowsInvalidDataException()
    {
        var path = WriteCsv("");
        var ex = Assert.Throws<InvalidDataException>(() => CsvService.Parse(path));
        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public void Parse_FileWithOnlyHeaders_ThrowsInvalidDataException()
    {
        var path = WriteCsv("Email,FirstName,LastName");
        var ex = Assert.Throws<InvalidDataException>(() => CsvService.Parse(path));
        Assert.Contains("must contain", ex.Message);
    }

    [Fact]
    public void Parse_MissingEmailColumn_ThrowsInvalidDataException()
    {
        var path = WriteCsv("Name,Age\r\nAlice,30");
        var ex = Assert.Throws<InvalidDataException>(() => CsvService.Parse(path));
        Assert.Contains("Email", ex.Message);
    }

    [Fact]
    public void Parse_DuplicateHeaders_ThrowsInvalidDataException()
    {
        var path = WriteCsv("Email,Email,FirstName\r\nalice@test.com,alt@test.com,Alice");
        var ex = Assert.Throws<InvalidDataException>(() => CsvService.Parse(path));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public void Parse_EmptyEmailRow_SkippedWithError()
    {
        var path = WriteCsv("Email,FirstName,LastName\r\n,Alice,Johnson\r\nbob@test.com,Bob,Smith");
        var (recipients, _, _, errors) = CsvService.Parse(path);

        Assert.Single(recipients);
        Assert.Contains("empty", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_InvalidEmail_ReportsError()
    {
        var path = WriteCsv("Email,FirstName,LastName\r\nnot-an-email,Alice,Johnson");
        var ex = Assert.Throws<InvalidDataException>(() => CsvService.Parse(path));
        Assert.Contains("valid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_DuplicateEmails_ReportsError()
    {
        var path = WriteCsv("Email,FirstName,LastName\r\nalice@test.com,Alice,Johnson\r\nalice@test.com,Alice,Smith");
        var (recipients, _, _, errors) = CsvService.Parse(path);

        Assert.Single(recipients);
        Assert.Contains("Duplicate", errors[0]);
    }

    [Fact]
    public void Parse_TooManyRecipients_Throws()
    {
        var lines = new List<string> { "Email,FirstName,LastName" };
        for (int i = 0; i < CsvService.MaxRecipients + 1; i++)
            lines.Add($"user{i}@test.com,First{i},Last{i}");

        var path = WriteCsv(string.Join("\r\n", lines));
        var ex = Assert.Throws<InvalidDataException>(() => CsvService.Parse(path));
        Assert.Contains("Maximum", ex.Message);
    }

    [Fact]
    public void Parse_AllRowsInvalid_ThrowsWithSummary()
    {
        var path = WriteCsv("Email\r\nnot-email-1\r\nnot-email-2");
        var ex = Assert.Throws<InvalidDataException>(() => CsvService.Parse(path));
        Assert.Contains("valid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── IsValidEmail ──────────────────────────────────────────

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("user.name+tag@example.co.uk", true)]
    [InlineData("x@y.com", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("not-an-email", false)]
    [InlineData("@example.com", false)]
    [InlineData("user@", false)]
    [InlineData("user@.com", false)]
    [InlineData("a@b.c", true)]
    public void IsValidEmail_VariousInputs_ReturnsExpected(string? email, bool expected)
    {
        Assert.Equal(expected, CsvService.IsValidEmail(email!));
    }

    [Fact]
    public void IsValidEmail_LongEmail_ReturnsFalse()
    {
        var longEmail = new string('a', 250) + "@b.com";
        Assert.False(CsvService.IsValidEmail(longEmail));
    }

    // ─── ParseCsvLine (internal) ───────────────────────────────

    [Fact]
    public void ParseCsvLine_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CsvService.ParseCsvLine(null!));
    }

    [Fact]
    public void ParseCsvLine_EmptyInput_ReturnsEmptyArray()
    {
        var result = CsvService.ParseCsvLine("");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseCsvLine_SingleField_ReturnsOneElement()
    {
        var result = CsvService.ParseCsvLine("hello");
        Assert.Equal(["hello"], result);
    }

    [Fact]
    public void ParseCsvLine_MultipleFields_SplitsCorrectly()
    {
        var result = CsvService.ParseCsvLine("a,b,c");
        Assert.Equal(["a", "b", "c"], result);
    }

    // ─── Additional edge cases ─────────────────────────────────

    [Fact]
    public void Parse_BomOnlyNoData_Throws()
    {
        var path = WriteCsv("\uFEFFEmail,FirstName,LastName");
        var ex = Assert.Throws<InvalidDataException>(() => CsvService.Parse(path));
        Assert.Contains("must contain", ex.Message);
    }

    [Fact]
    public void Parse_UnicodeInFields_ParsesCorrectly()
    {
        var path = WriteCsv("Email,FirstName,LastName\r\nuser@test.com,Иван, Петров\r\nuser2@test.com,李,四");
        var (recipients, _, _, _) = CsvService.Parse(path);

        Assert.Equal(2, recipients.Count);
        Assert.Equal("Иван", recipients[0].FirstName);
        Assert.Equal("Петров", recipients[0].LastName);
    }

    [Fact]
    public void Parse_AllWhitespaceDataRows_Skipped()
    {
        var path = WriteCsv("Email,FirstName,LastName\r\n   ,  ,  \r\nbob@test.com,Bob,Smith");
        var (recipients, _, _, errors) = CsvService.Parse(path);

        // All-whitespace row is treated as empty line and skipped entirely
        Assert.Single(recipients);
        Assert.Empty(errors);
    }

    [Fact]
    public void Parse_EmailWithSpacesOnly_ReportsError()
    {
        var path = WriteCsv("Email,FirstName\r\n   ,John\r\nbob@test.com,Bob");
        var (recipients, _, _, errors) = CsvService.Parse(path);

        Assert.Single(recipients);
        Assert.Contains("empty", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    // ─── ParseCsvLine additional internals ────────────────────

    [Fact]
    public void ParseCsvLine_OnlyQuotes_ReturnsEmptyField()
    {
        var result = CsvService.ParseCsvLine("\"\"");
        Assert.Equal([""], result);
    }

    [Fact]
    public void ParseCsvLine_TrailingComma_ReturnsEmptyLastField()
    {
        var result = CsvService.ParseCsvLine("a,");
        Assert.Equal(["a", ""], result);
    }

    [Fact]
    public void ParseCsvLine_LeadingComma_ReturnsEmptyFirstField()
    {
        var result = CsvService.ParseCsvLine(",b");
        Assert.Equal(["", "b"], result);
    }

    [Fact]
    public void ParseCsvLine_UnclosedQuote_TreatsAsRegularField()
    {
        var result = CsvService.ParseCsvLine("a,\"unclosed,b");
        Assert.Equal(["a", "unclosed,b"], result);
    }

    [Fact]
    public void ParseCsvLine_ConsecutiveCommas_ReturnsEmptyFields()
    {
        var result = CsvService.ParseCsvLine("a,,c");
        Assert.Equal(["a", "", "c"], result);
    }

    // ─── Max file size ─────────────────────────────────────────

    [Fact]
    public void Parse_FileExceedsMaxSize_Throws()
    {
        var path = WriteCsv(new string('x', (int)CsvService.MaxFileSizeBytes + 1));
        var ex = Assert.Throws<InvalidDataException>(() => CsvService.Parse(path));
        Assert.Contains("too large", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
