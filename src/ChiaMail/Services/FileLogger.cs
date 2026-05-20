using System.IO;
using System.Text;

namespace ChiaMail.Services;

public sealed class FileLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    public FileLogger(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(fs, Encoding.UTF8);
    }

    public void Write(string message)
    {
        if (_disposed) return;

        lock (_writer)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _writer.WriteLine($"[{timestamp}] {message}");
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _writer.Dispose();
        }
    }
}
