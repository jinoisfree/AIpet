using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
namespace AIpet.Core;

public sealed record UsagePaths(string Home, string Roaming, string Local, string? ClaudeConfig, string SearchPath)
{
    public static UsagePaths Default() => new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"), Environment.GetEnvironmentVariable("PATH") ?? "");
    public string Account => Path.Combine(ClaudeConfig ?? Home, ".claude.json");
    public string Credentials => Path.Combine(ClaudeConfig ?? Path.Combine(Home, ".claude"), ".credentials.json");
    public IEnumerable<string> CodexCandidates(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)) yield return configured;
        foreach (var directory in SearchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var absolute = directory.Trim('"');
            if (!Path.IsPathFullyQualified(absolute)) continue;
            foreach (var name in new[] { "codex.exe", "codex.cmd" }) yield return Path.Combine(absolute, name);
        }
        yield return Path.Combine(Roaming, "npm", "codex.cmd");
        yield return Path.Combine(Local, "Programs", "Codex", "codex.exe");
        yield return Path.Combine(Local, "Programs", "Codex", "bin", "codex.exe");
    }
    public IEnumerable<string> HistoryCandidates()
    {
        yield return Path.Combine(Roaming, "Claude", "plan-usage-history.json");
        string[] packages;
        try { packages = Directory.GetDirectories(Path.Combine(Local, "Packages"), "Claude_*"); }
        catch { yield break; }
        foreach (var package in packages.OrderBy(p => p, StringComparer.Ordinal))
            yield return Path.Combine(package, "LocalCache", "Roaming", "Claude", "plan-usage-history.json");
    }
}
public interface IUsageSources
{
    string CodexExecutable => "none";
    Task<(ServiceUsage? Value, string Reason)> Codex(string? configured);
    ServiceUsage? History();
    Task<(ServiceUsage? Value, DirectUsageStatus Status)> Direct(Func<bool> allowed);
}
public sealed class UsageSources : IUsageSources
{
    public string CodexExecutable { get; private set; } = "none";
    private readonly Func<IEnumerable<string>> storeRoots;
    private readonly string codexRoot;
    private readonly UsagePaths paths;
    private readonly Func<string, Task<(JsonNode? Account, JsonNode? Limits)>> runner;
    private readonly Func<string, Task<(int Status, string Data)>> request;
    public UsageSources(UsagePaths? paths = null,
        Func<string, Task<(JsonNode? Account, JsonNode? Limits)>>? runner = null,
        Func<string, Task<(int Status, string Data)>>? request = null, Func<IEnumerable<string>>? storeRoots = null)
    { this.paths = paths ?? UsagePaths.Default(); codexRoot = Path.Combine((paths is null ? Environment.GetEnvironmentVariable("CODEX_HOME") : null) ?? Path.Combine(this.paths.Home, ".codex"), "sessions"); this.storeRoots = storeRoots ?? CodexStorePackages.Paths; this.runner = runner ?? CodexUsageProcess.Query; this.request = request ?? UsageHttp.Request; }
    private static string? Read(string path, int limit)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > limit) return null;
        using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = file.Read(buffer, 0, Math.Min(buffer.Length, limit + 1 - (int)bytes.Length))) > 0)
        { bytes.Write(buffer, 0, count); if (bytes.Length > limit) return null; }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
    private UsageAccount Account()
    {
        try { return UsageParser.ClaudeAccount(UsageParser.Object(Read(paths.Account, 1024 * 1024) ?? "")); }
        catch { return new(); }
    }
    public ServiceUsage? History()
    {
        try
        {
            var file = paths.HistoryCandidates().FirstOrDefault(File.Exists);
            return file is null ? null : UsageParser.History(UsageParser.Object(Read(file, 8 * 1024 * 1024) ?? ""), Account());
        }
        catch { return null; }
    }
    public async Task<(ServiceUsage? Value, string Reason)> Codex(string? configured)
    {
        try
        {
            CodexExecutable = "none";
            var path = paths.CodexCandidates(configured).FirstOrDefault(File.Exists);
            if (path is not null) CodexExecutable = path == configured ? "configured" : path == Path.Combine(paths.Roaming, "npm", "codex.cmd") ? "npm" : "path";
            else { path = CodexStorePackages.Executables(storeRoots).FirstOrDefault(); if (path is not null) CodexExecutable = "store"; }
            if (path is null) return (null, "Codex 를 찾을 수 없음");
            var result = await runner(path).ConfigureAwait(false);
            var value = UsageParser.Codex(result.Account, result.Limits, DateTimeOffset.UtcNow);
            return (value, value is null ? "Codex 사용량 조회 실패" : "");
        }
        catch (Exception error) when (error is CodexStartException or UnauthorizedAccessException) { return (null, "Codex 앱 실행 불가"); }
        catch { return (null, "Codex 연결 실패"); }
    }
    public async Task<(ServiceUsage? Value, DirectUsageStatus Status)> Direct(Func<bool> allowed)
    {
        try
        {
            if (!allowed()) return (null, DirectUsageStatus.Disabled);
            var credentials = UsageParser.Credentials(Read(paths.Credentials, 1024 * 1024) ?? "");
            if (credentials is null) return (null, DirectUsageStatus.Missing);
            if (credentials.Status(DateTimeOffset.UtcNow) is { } status) return (null, status);
            var account = Account();
            if (!allowed()) return (null, DirectUsageStatus.Disabled);
            var result = await request(credentials.Token).ConfigureAwait(false);
            return UsageParser.Direct(result.Status, result.Data, account, DateTimeOffset.UtcNow);
        }
        catch (UnauthorizedAccessException) { return (null, DirectUsageStatus.Denied); }
        catch (FileNotFoundException) { return (null, DirectUsageStatus.Missing); }
        catch (DirectoryNotFoundException) { return (null, DirectUsageStatus.Missing); }
        catch { return (null, DirectUsageStatus.Connection); }
    }
    public async Task<UsageState> Snapshot(string? configured = null)
    {
        var codex = await Codex(configured).ConfigureAwait(false);
        var monitor = new StatusMonitor(codexRoot: codexRoot,
            claudeRoot: Path.Combine(paths.ClaudeConfig ?? Path.Combine(paths.Home, ".claude"), "projects"), eventRoot: Path.Combine(paths.Local, "AIpet", "events"));
        monitor.Poll(forceDiscovery: true);
        return new() { Codex = codex.Value, CodexRecord = monitor.LatestCodexUsage, CodexReason = codex.Reason, CodexExecutable = CodexExecutable, History = History() };
    }
}
public sealed class CodexStartException : Exception { public CodexStartException() : base("Codex 앱 실행 불가") { } }
public static class CodexUsageProcess
{
    public static ProcessStartInfo StartInfo(string executable)
    {
        var info = new ProcessStartInfo { CreateNoWindow = true, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        if (executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            // cmd reparses its command string. Reject expansion/control characters rather than execute an ambiguous path.
            if (executable.IndexOfAny(['"', '%', '!', '\r', '\n']) >= 0) throw new ArgumentException("Unsupported executable path.");
            info.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            info.Arguments = "/d /s /c \"\"" + executable + "\" app-server --listen stdio://\"";
        }
        else { info.FileName = executable; info.ArgumentList.Add("app-server"); info.ArgumentList.Add("--listen"); info.ArgumentList.Add("stdio://"); }
        return info;
    }
    public static async Task<(JsonNode? Account, JsonNode? Limits)> Query(string executable)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var cancel = timeout.Token;
        using var process = new Process { StartInfo = StartInfo(executable) }; Task? drain = null;
        try
        {
            try { if (!process.Start()) throw new CodexStartException(); } catch { throw new CodexStartException(); }
            // Drain stderr without retaining it. Never forward child output to error.log.
            drain = process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancel);
            async Task Send(object message) { await process.StandardInput.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(message).AsMemory(), cancel).ConfigureAwait(false); await process.StandardInput.FlushAsync(cancel).ConfigureAwait(false); }
            await Send(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "aipet", version = "1.0.11" } } }).ConfigureAwait(false);
            var output = process.StandardOutput.BaseStream; var buffer = new byte[8192]; using var line = new MemoryStream();
            var total = 0; var initialized = false; JsonNode? account = null, limits = null;
            while (true)
            {
                var count = await output.ReadAsync(buffer, cancel).ConfigureAwait(false); if (count == 0) break;
                total += count; if (total > 1024 * 1024) return (null, null);
                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] != 10) { line.WriteByte(buffer[i]); continue; }
                    var message = UsageParser.Object(Encoding.UTF8.GetString(line.ToArray())); line.SetLength(0);
                    var id = UsageParser.Number(UsageParser.Get(message, "id"));
                    if (id == 1 && !initialized)
                    {
                        if (UsageParser.Get(message, "error") is not null) return (null, null);
                        await Send(new { method = "initialized", @params = new { } }).ConfigureAwait(false);
                        await Send(new { id = 2, method = "account/read", @params = new { } }).ConfigureAwait(false);
                        await Send(new { id = 3, method = "account/rateLimits/read", @params = new { } }).ConfigureAwait(false);
                        initialized = true;
                    }
                    else if (id == 2) account = message;
                    else if (id == 3) limits = message;
                }
                if (account is not null && limits is not null) return (account, limits);
            }
            return (null, null);
        }
        catch (CodexStartException) { throw; }
        catch { return (null, null); }
        finally
        {
            timeout.Cancel();
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync().ConfigureAwait(false); } catch { }
            if (drain is not null) { try { await drain.ConfigureAwait(false); } catch { } }
        }
    }
}
public static class UsageHttp
{
    public static async Task<(int Status, string Data)> Request(string token)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        return await Request(client, token).ConfigureAwait(false);
    }
    // Injectable client for synthetic transport tests; production always uses the restrictive handler above.
    public static async Task<(int Status, string Data)> Request(HttpClient client, string token)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("anthropic-beta", "oauth-2025-04-20"); request.Headers.Accept.Add(new("application/json"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status != 200) return (status, "");
            const int limit = 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) return (0, "");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            { if (bytes.Length + count > limit) return (0, ""); bytes.Write(buffer, 0, count); }
            return (status, Encoding.UTF8.GetString(bytes.ToArray()));
        }
        catch { return (0, ""); }
    }
}
