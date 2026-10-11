using System.Text.Json;
namespace AIpet.Core;

// Bounded incremental JSONL reader. Raw bytes survive only until the incomplete line is finished.
public sealed class TranscriptTail
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public string Path { get; }
    public TranscriptDecoder Decoder { get; private set; }
    public long Offset { get; private set; }
    private DateTime creationTime;
    private byte[] pending = [];
    private bool discardUntilNewline;
    public string? LastError { get; private set; }
    public bool Limited { get; private set; }
    public TranscriptTail(string path, Provider provider)
    {
        Path = path;
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        Decoder = new(provider, provider == Provider.Codex && name.Length > 36 ? name[^36..] : name);
    }
    public IReadOnlyList<WorkItem> Read()
    {
        try
        {
            var info = new FileInfo(Path);
            var size = info.Length;
            if (info.CreationTimeUtc != creationTime || size < Offset)
            {
                Offset = 0; pending = []; discardUntilNewline = false;
                Decoder = new(Decoder.Provider, Decoder.SessionID); creationTime = info.CreationTimeUtc;
            }
            if (size == Offset) { LastError = null; return []; }
            using var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (Offset == 0 && size > MaxBytes)
            {
                var prefix = new byte[256 * 1024]; var length = file.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
                var end = System.Array.IndexOf(prefix, (byte)10, 0, length);
                if (end >= 0) Consume(prefix.AsMemory(0, end));
                Offset = size - MaxBytes; discardUntilNewline = true; Limited = true;
            }
            file.Seek(Offset, SeekOrigin.Begin);
            var chunk = new byte[(int)Math.Min(MaxBytes, size - Offset)]; var read = file.ReadAtLeast(chunk, chunk.Length, throwOnEndOfStream: false);
            Offset += read;
            var data = new byte[pending.Length + read]; pending.CopyTo(data, 0); chunk.AsSpan(0, read).CopyTo(data.AsSpan(pending.Length));
            var items = new List<WorkItem>(); var start = 0;
            for (var i = 0; i < data.Length; i++)
            {
                if (data[i] != 10) continue;
                if (!discardUntilNewline && i - start <= MaxBytes && Consume(data.AsMemory(start, i - start)) is { } item) items.Add(item);
                start = i + 1; discardUntilNewline = false;
            }
            pending = data[start..];
            if (pending.Length > MaxBytes) { pending = []; discardUntilNewline = true; Limited = true; }
            LastError = null; return items;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { LastError = "기록 읽기 실패"; return []; }
    }
    private WorkItem? Consume(ReadOnlyMemory<byte> bytes)
    {
        try { using var doc = JsonDocument.Parse(bytes); return Decoder.Consume(doc.RootElement); }
        catch (JsonException) { return null; }
    }
}
