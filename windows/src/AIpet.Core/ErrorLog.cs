using System.Text;
namespace AIpet.Core;

public sealed class ErrorLog(string? path = null)
{
    public const int MaxBytes = 1024 * 1024;
    public string FilePath => path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIpet", "error.log");
    private readonly object gate = new();
    // Do not serialize Exception.Data, inner objects, transcripts or tool payloads.
    public void Append(Exception exception, DateTimeOffset? now = null)
    {
        try
        {
            static string Bound(string? text) => text is null ? "" : text[..Math.Min(text.Length, 16000)];
            var entry = $"[{(now ?? DateTimeOffset.Now):O}] {exception.GetType().FullName}\n{Bound(exception.Message)}\n{Bound(exception.StackTrace)}\n\n";
            var bytes = Encoding.UTF8.GetBytes(entry);
            lock (gate)
            {
                var file = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
                using var stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
                if (stream.Length + bytes.Length > MaxBytes) stream.SetLength(0);
                stream.Seek(0, SeekOrigin.End); stream.Write(bytes);
            }
        }
        catch { /* Diagnostics must never introduce a second failure. */ }
    }
}
public sealed class RepeatedExceptionGuard
{
    private (Type Type, string Message, string? Stack)? previous;
    private int count;
    public bool ShouldStop(Exception exception)
    {
        var key = (exception.GetType(), exception.Message, exception.StackTrace);
        count = previous == key ? count + 1 : 1; previous = key;
        return count >= 10;
    }
}
