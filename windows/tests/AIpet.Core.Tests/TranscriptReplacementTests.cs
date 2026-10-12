using System.Text.Json;
using AIpet.Core;
namespace AIpet.Core.Tests;

public sealed class TranscriptReplacementTests : TestSupport
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static JsonElement Event(string type, int padding = 200) => Record("event_msg",
        new { padding = new string('x', padding), type, turn_id = "one" }, At);

    [Theory]
    [InlineData(-32, false)]
    [InlineData(0, false)]
    [InlineData(32, false)]
    [InlineData(-32, true)]
    [InlineData(0, true)]
    [InlineData(32, true)]
    public void SameCreationTimeReplacementRestarts(int sizeDelta, bool overwriteInPlace)
    {
        var path = Path.Combine(Root, "session.jsonl");
        var original = Line(Event("task_started"));
        File.WriteAllBytes(path, original);
        var creation = File.GetCreationTimeUtc(path);
        var write = File.GetLastWriteTimeUtc(path);
        var tail = new TranscriptTail(path, Provider.Codex);
        Assert.Equal(WorkState.Running, Assert.Single(tail.Read()).State);
        // task_complete is one byte longer; compensate to control the exact file size.
        var replacement = Line(Event("task_complete", 199 + sizeDelta));
        Assert.Equal(original.Length + sizeDelta, replacement.Length);
        if (overwriteInPlace) File.WriteAllBytes(path, replacement);
        else AtomicWrite(path, replacement);
        File.SetCreationTimeUtc(path, creation);
        File.SetLastWriteTimeUtc(path, write.AddSeconds(2));
        Assert.Equal(creation, File.GetCreationTimeUtc(path));
        Assert.Equal(WorkState.Responded, Assert.Single(tail.Read()).State);
        Assert.Equal(replacement.Length, tail.Offset);
        Assert.Empty(tail.Read());
        Assert.Null(tail.LastError);
    }

    [Fact] public void AppendPreservesOffsetAndPartialLineWithoutDuplicates()
    {
        var path = Path.Combine(Root, "append.jsonl");
        File.WriteAllBytes(path, Line(Event("task_started")));
        var tail = new TranscriptTail(path, Provider.Codex);
        Assert.Equal(WorkState.Running, Assert.Single(tail.Read()).State);
        var offset = tail.Offset;
        // A metadata-only change must not replay previous events.
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));
        Assert.Empty(tail.Read()); Assert.Equal(offset, tail.Offset);
        var next = Line(Event("task_complete"));
        using (var stream = new FileStream(path, FileMode.Append)) stream.Write(next[..^3]);
        Assert.Empty(tail.Read()); Assert.Empty(tail.Read());
        using (var stream = new FileStream(path, FileMode.Append)) stream.Write(next[^3..]);
        Assert.Equal(WorkState.Responded, Assert.Single(tail.Read()).State);
        Assert.Equal(offset + next.Length, tail.Offset);
        Assert.Empty(tail.Read()); Assert.Null(tail.LastError);
    }
}
