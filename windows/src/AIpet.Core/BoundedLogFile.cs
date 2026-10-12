using System.Text;
namespace AIpet.Core;

public sealed class BoundedLogFile(string filePath, int maxBytes)
{
    public string FilePath { get; } = filePath;
    private readonly object gate = new();
    public void Append(string entry)
    {
        try
        {
            if (maxBytes <= 0) return;
            var bytes = new byte[Math.Min(maxBytes, Encoding.UTF8.GetByteCount(entry))];
            Encoding.UTF8.GetEncoder().Convert(entry.AsSpan(), bytes, true, out _, out var length, out _);
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
                using var stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
                if (stream.Length + length > maxBytes) stream.SetLength(0);
                stream.Seek(0, SeekOrigin.End); stream.Write(bytes, 0, length);
            }
        }
        catch { /* Logging must not introduce another failure. */ }
    }
}
