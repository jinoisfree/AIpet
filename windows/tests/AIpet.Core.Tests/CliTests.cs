using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIpet.Core;
namespace AIpet.Core.Tests;

public sealed class CliTests : TestSupport
{
    private async Task<(int Code, string Output, string Error)> Run(string input, params string[] args)
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var start = new ProcessStartInfo(host) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "AIpet.Cli.dll"));
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["CODEX_HOME"] = Path.Combine(Root, "codex");
        start.Environment["CLAUDE_CONFIG_DIR"] = Path.Combine(Root, "claude");
        // Isolate the OS-specific LocalApplicationData default for --snapshot too.
        start.Environment["XDG_DATA_HOME"] = Root;
        start.Environment["LOCALAPPDATA"] = Root;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(input); process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await stdout, await stderr);
    }
    [Fact]
    public async Task HookHasNoDecisionOrPrivateContent()
    {
        var result = await Run(JsonSerializer.Serialize(new { session_id = "../../escape", hook_event_name = "PermissionRequest", cwd = "/work/test-project", tool_input = new { command = "PRIVATE_SECRET" } }), "--hook", "claude", "--event-root", Root);
        Assert.Equal((0, "", ""), result);
        var file = Assert.Single(Directory.GetFiles(Root, "*.json")); var output = File.ReadAllText(file);
        Assert.DoesNotContain("PRIVATE_SECRET", output);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal("waiting", doc.RootElement.String("state"));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("claude:../../escape"))).ToLowerInvariant() + ".json", Path.GetFileName(file));
        Assert.Equal(new[] { "event", "project", "provider", "sessionID", "state", "turnID", "updatedAt", "version" }, doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Empty(Directory.GetFiles(Root, "*.tmp"));
    }
    [Fact]
    public async Task MalformedInputIsSilent()
    {
        Assert.Equal((0, "", ""), await Run("{broken", "--hook", "codex", "--event-root", Root));
        Assert.Empty(Directory.GetFiles(Root));
    }
    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public async Task WindowsCwdAndAtomicReplacement(string provider)
    {
        var payload = JsonSerializer.Serialize(new { session_id = "s1", hook_event_name = "PermissionRequest", cwd = @"C:\Users\a\proj" });
        Assert.Equal((0, "", ""), await Run(payload, "--hook", provider, "--event-root", Root));
        Assert.Equal((0, "", ""), await Run(payload, "--hook", provider, "--event-root", Root));
        using var doc = JsonDocument.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(Root, "*.json"))));
        Assert.Equal("waiting", doc.RootElement.String("state")); Assert.Equal("proj", doc.RootElement.String("project"));
        Assert.Equal(provider, doc.RootElement.String("provider")); Assert.Equal(1, doc.RootElement.Get("version").GetInt32());
    }
    [Fact]
    public async Task LargeInputAndWriteFailureAreSilent()
    {
        // Invalid JSON at the byte bound; the rest is not read or persisted.
        Assert.Equal((0, "", ""), await Run(new string(' ', HookBridge.MaxInputBytes), "--hook", "codex", "--event-root", Root));
        var file = Path.Combine(Root, "file"); File.WriteAllText(file, "existing");
        Assert.Equal((0, "", ""), await Run("{\"session_id\":\"s\",\"hook_event_name\":\"Stop\"}", "--hook", "codex", "--event-root", file));
        Assert.Equal("existing", File.ReadAllText(file));
    }
    [Fact]
    public async Task SnapshotIsIndentedSortedAndContainsOnlyState()
    {
        var sessions = Path.Combine(Root, "codex", "sessions"); Directory.CreateDirectory(sessions);
        File.WriteAllBytes(Path.Combine(sessions, "s.jsonl"), Line(Record("event_msg", new { type = "task_started", turn_id = "t", prompt = "PRIVATE" }, DateTimeOffset.UtcNow.AddSeconds(-1))));
        var result = await Run("", "--snapshot"); Assert.Equal(0, result.Code); Assert.Equal("", result.Error);
        Assert.Contains("\n  ", result.Output); Assert.DoesNotContain("PRIVATE", result.Output);
        using var doc = JsonDocument.Parse(result.Output);
        void Sorted(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = element.EnumerateObject().Select(p => p.Name).ToArray(); Assert.Equal(names.Order(StringComparer.Ordinal), names);
                foreach (var p in element.EnumerateObject()) Sorted(p.Value);
            }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Sorted(item);
        }
        Sorted(doc.RootElement);
        var task = Assert.Single(doc.RootElement.Get("tasks").Array()); Assert.Equal("running", task.String("state"));
    }
}
