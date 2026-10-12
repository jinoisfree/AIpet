using System.Text.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
namespace AIpet.Core;

// Bounded incremental JSONL reader. Raw bytes survive only until the incomplete line is finished.
public sealed class TranscriptTail
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public string Path { get; }
    public TranscriptDecoder Decoder { get; private set; }
    public long Offset { get; private set; }
    private DateTime creationTime, lastWriteTime;
    private FileIdentity? identity;
    private byte[]? boundaryHash;
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
            var observedCreation = info.CreationTimeUtc;
            var observedWrite = info.LastWriteTimeUtc;
            // Unchanged metadata avoids opening idle files. Equal size alone is insufficient.
            if (size == Offset && observedCreation == creationTime && observedWrite == lastWriteTime)
            { LastError = null; return []; }
            using var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            size = file.Length;
            var currentIdentity = GetIdentity(file.SafeFileHandle);
            var replaced = size < Offset || observedCreation != creationTime
                || identity is { } previous && currentIdentity is { } current && previous != current;
            if (!replaced && boundaryHash is not null)
                replaced = !CryptographicOperations.FixedTimeEquals(boundaryHash, HashBoundary(file, Offset));
            if (replaced)
            {
                Offset = 0; pending = []; discardUntilNewline = false; boundaryHash = null;
                Decoder = new(Decoder.Provider, Decoder.SessionID);
            }
            creationTime = observedCreation; identity = currentIdentity;
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
            boundaryHash = Offset > 0 ? HashBoundary(file, Offset) : null;
            lastWriteTime = observedWrite;
            LastError = null; return items;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { LastError = "기록 읽기 실패"; return []; }
    }
    private static byte[] HashBoundary(FileStream file, long offset)
    {
        Span<byte> bytes = stackalloc byte[(int)Math.Min(64, offset)];
        file.Seek(offset - bytes.Length, SeekOrigin.Begin);
        var count = file.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        return SHA256.HashData(bytes[..count]);
    }
    private static FileIdentity? GetIdentity(SafeFileHandle handle)
    {
        if (OperatingSystem.IsWindows() && GetFileInformationByHandle(handle, out var info))
            return new(info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow);
        return null;
    }
    private readonly record struct FileIdentity(uint Volume, uint IndexHigh, uint IndexLow);
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerialNumber, SizeHigh, SizeLow, LinkCount, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);
    private WorkItem? Consume(ReadOnlyMemory<byte> bytes)
    {
        try { using var doc = JsonDocument.Parse(bytes); return Decoder.Consume(doc.RootElement); }
        catch (JsonException) { return null; }
    }
}
