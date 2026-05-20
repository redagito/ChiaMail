using System.IO;
using ChiaMail.Services;

namespace ChiaMail.Tests;

public sealed class FileLoggerTests : IDisposable
{
    private readonly string _tempDir;

    public FileLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ChiaMailFileLogger", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Constructor_CreatesEmptyFile()
    {
        var path = Path.Combine(_tempDir, "test.log");
        using var logger = new FileLogger(path);

        Assert.True(File.Exists(path));
        Assert.Equal(0, new FileInfo(path).Length);
    }

    [Fact]
    public void Constructor_ClearsExistingContent()
    {
        var path = Path.Combine(_tempDir, "test.log");
        File.WriteAllText(path, "old content");

        using var logger = new FileLogger(path);

        Assert.Equal(0, new FileInfo(path).Length);
    }

    private static string ReadFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    private static string[] ReadFileLines(string path)
    {
        var content = ReadFile(path);
        return content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void Write_AppendsEntryWithTimestamp()
    {
        var path = Path.Combine(_tempDir, "test.log");
        using var logger = new FileLogger(path);

        logger.Write("Hello World");

        var content = ReadFile(path);
        Assert.Contains("Hello World", content);
        // Should start with a timestamp in [YYYY-MM-DD HH:MM:SS] format
        Assert.StartsWith("[", content);
        Assert.Matches(@"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]", ReadFileLines(path)[0]);
    }

    [Fact]
    public void Write_MultipleEntries_AllAppended()
    {
        var path = Path.Combine(_tempDir, "test.log");
        using var logger = new FileLogger(path);

        logger.Write("First");
        logger.Write("Second");

        var lines = ReadFileLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("First", lines[0]);
        Assert.Contains("Second", lines[1]);
    }

    [Fact]
    public void Write_EmptyString_DoesNotThrow()
    {
        var path = Path.Combine(_tempDir, "test.log");
        using var logger = new FileLogger(path);

        logger.Write("");
        logger.Write("   ");

        var lines = ReadFileLines(path);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void Write_AfterDispose_DoesNotThrow()
    {
        var path = Path.Combine(_tempDir, "test.log");
        var logger = new FileLogger(path);
        logger.Dispose();

        logger.Write("After dispose");
    }

    [Fact]
    public void Constructor_NullPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FileLogger(null!));
    }

    [Fact]
    public void Constructor_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FileLogger(""));
    }
}
