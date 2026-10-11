using System.Text.Json;
using AIpet.Core;
namespace AIpet.Core.Tests;

public abstract class TestSupport : IDisposable
{
    protected readonly string Root = Path.Combine(Path.GetTempPath(), "aipet-tests-" + Guid.NewGuid().ToString("N"));
    protected TestSupport() => Directory.CreateDirectory(Root);
    public void Dispose() { Directory.Delete(Root, recursive: true); GC.SuppressFinalize(this); }
    protected static JsonElement J(object value) => JsonSerializer.SerializeToElement(value);
    protected static JsonElement Record(string type, object payload, DateTimeOffset at) => J(new { type, timestamp = at.ToString("O"), payload });
    protected static void Check(bool condition, string message = "Swift precondition") => Assert.True(condition, message);
    protected static byte[] Line(JsonElement record) => System.Text.Encoding.UTF8.GetBytes(record.GetRawText() + "\n");
    protected static void Append(string path, JsonElement record) { using var file = new FileStream(path, FileMode.Append); file.Write(Line(record)); }
    protected static void AtomicWrite(string path, byte[] bytes) { var temp = path + ".tmp"; File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
}
