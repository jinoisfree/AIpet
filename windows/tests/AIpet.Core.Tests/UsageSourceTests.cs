using System.Net;
using System.Text.Json.Nodes;
using Xunit;
namespace AIpet.Core.Tests;

public sealed class UsageSourceTests : TestSupport
{
    private UsagePaths Paths => new(Path.Combine(Root, "home"), Path.Combine(Root, "roaming"), Path.Combine(Root, "local"), null, Path.Combine(Root, "path"));
    private static void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
    [Fact]
    public void CodexIgnoresRelativeConfiguredAndPathEntries()
    {
        var absolute = Path.Combine(Root, "absolute");
        var entries = new[] { "", ".", "relative", "..", "\"" + absolute + "\"" };
        var paths = Paths with { SearchPath = string.Join(Path.PathSeparator, entries) };
        var candidates = paths.CodexCandidates("relative-codex.exe").ToArray();
        Assert.Equal(Path.Combine(absolute, "codex.exe"), candidates[0]);
        Assert.Equal(Path.Combine(absolute, "codex.cmd"), candidates[1]);
        Assert.All(candidates, candidate => Assert.True(Path.IsPathFullyQualified(candidate)));
        Assert.DoesNotContain("relative-codex.exe", candidates);
        var configured = Path.Combine(Root, "configured.exe");
        Assert.Equal(configured, paths.CodexCandidates(configured).First());
        if (OperatingSystem.IsWindows())
        {
            paths = paths with { SearchPath = @"C:relative;\rooted;" + absolute };
            Assert.Equal(Path.Combine(absolute, "codex.exe"), paths.CodexCandidates(@"C:codex.exe").First());
        }
    }
    [Fact]
    public async Task CandidateOrderInjectedRunnerAndSnapshotNeverReadCredentials()
    {
        var paths = Paths; var configured = Path.Combine(Root, "chosen.exe"); Write(configured, "synthetic");
        var candidates = paths.CodexCandidates(configured).ToArray();
        Assert.Equal(configured, candidates[0]); Assert.EndsWith("codex.exe", candidates[1]); Assert.EndsWith("codex.cmd", candidates[2]);
        Assert.Contains(Path.Combine(paths.Roaming, "npm", "codex.cmd"), candidates);
        Assert.Contains(Path.Combine(paths.Local, "Programs", "Codex", "codex.exe"), candidates);
        var called = 0;
        var sources = new UsageSources(paths, runner: file => {
            Assert.Equal(configured, file); called++;
            return Task.FromResult((UsageParser.Object("""{"result":{"account":{"email":"account@example.com"}}}"""),
                UsageParser.Object("""{"result":{"rateLimits":{"primary":{"usedPercent":28,"windowDurationMins":300}}}}""")));
        }, request: _ => throw new InvalidOperationException("Direct query must not run"));
        // A directory here would fail if the snapshot tried opening the credential file.
        Directory.CreateDirectory(paths.Credentials);
        var result = await sources.Snapshot(configured);
        Assert.Equal(1, called); Assert.Equal("72%", result.Codex?.Display(DateTimeOffset.UtcNow));
        var json = JsonData.Sorted(result.Diagnostic(DateTimeOffset.UtcNow));
        Assert.DoesNotContain("account@example.com", json); Assert.Contains("ac***", json);
        Assert.Equal(DirectUsageStatus.Disabled, result.DirectStatus);
    }
    [Fact]
    public async Task MissingExecutableAndTransportFailureAreGeneric()
    {
        Assert.Equal("Codex 를 찾을 수 없음", (await new UsageSources(Paths).Codex(null)).Reason);
        var file = Path.Combine(Root, "broken.exe"); Write(file, "");
        var result = await new UsageSources(Paths, runner: _ => throw new IOException("synthetic-secret@example.com")).Codex(file);
        Assert.Null(result.Value); Assert.Equal("Codex 연결 실패", result.Reason);
    }
    [Fact]
    public void HistoryFallbackOrganizationAndReadLimit()
    {
        var paths = Paths with { ClaudeConfig = Path.Combine(Root, "config") };
        Write(paths.Account, """{"oauthAccount":{"emailAddress":"account@example.com","organizationName":"Example","organizationUuid":"org-a"}}""");
        var history = Path.Combine(paths.Local, "Packages", "Claude_example", "LocalCache", "Roaming", "Claude", "plan-usage-history.json");
        Write(history, """{"samples":[{"t":1800000000000,"org":"org-a","u":{"fh":28,"sd":52}}]}""");
        var sources = new UsageSources(paths);
        Assert.Equal("account@example.com", sources.History()?.Account.Label);
        var primary = Path.Combine(paths.Roaming, "Claude", "plan-usage-history.json");
        Write(primary, """{"samples":[{"t":1800000000000,"org":"other","u":{"fh":28}}]}""");
        Assert.Equal("계정 확인 불가", sources.History()?.Account.Label);
        using (var file = File.OpenWrite(primary)) file.SetLength(8 * 1024 * 1024 + 1);
        Assert.Null(sources.History());
    }
    [Fact]
    public async Task DirectSyntheticCredentialsExpiryDenialAndOptOut()
    {
        var paths = Paths with { ClaudeConfig = Path.Combine(Root, "config") }; var requests = 0;
        var sources = new UsageSources(paths, request: token => {
            requests++; Assert.Equal("synthetic-token", token);
            return Task.FromResult((200, """{"five_hour":{"utilization":28,"resets_at":"2099-01-01T00:00:00Z"}}"""));
        });
        Assert.Equal(DirectUsageStatus.Disabled, (await sources.Direct(() => false)).Status);
        Assert.Equal(DirectUsageStatus.Missing, (await sources.Direct(() => true)).Status);
        Write(paths.Credentials, """{"claudeAiOauth":{"accessToken":"synthetic-token","expiresAt":1}}""");
        Assert.Equal(DirectUsageStatus.Expired, (await sources.Direct(() => true)).Status); Assert.Equal(0, requests);
        Write(paths.Credentials, """{"claudeAiOauth":{"accessToken":"synthetic-token"}}""");
        Assert.Equal(DirectUsageStatus.Success, (await sources.Direct(() => true)).Status); Assert.Equal(1, requests);
        var checks = 0; Assert.Equal(DirectUsageStatus.Disabled, (await sources.Direct(() => ++checks == 1)).Status); Assert.Equal(1, requests);
        var denied = new UsageSources(paths, request: _ => throw new UnauthorizedAccessException("synthetic-token"));
        Assert.Equal(DirectUsageStatus.Denied, (await denied.Direct(() => true)).Status);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    [Fact]
    public async Task HttpHeadersStatusAndBoundedBody()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(request => {
            calls++; Assert.Equal("https://api.anthropic.com/api/oauth/usage", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Assert.Equal("synthetic-token", request.Headers.Authorization.Parameter);
            Assert.Contains("oauth-2025-04-20", request.Headers.GetValues("anthropic-beta")); Assert.Equal("application/json", request.Headers.Accept.Single().MediaType);
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }));
        Assert.Equal((200, "{}"), await UsageHttp.Request(client, "synthetic-token")); Assert.Equal(1, calls);
        using var limited = new HttpClient(new Handler(_ => new(HttpStatusCode.TooManyRequests) { Content = new StringContent("must not retain") }));
        Assert.Equal((429, ""), await UsageHttp.Request(limited, "synthetic-token"));
        using var redirect = new HttpClient(new Handler(_ => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://example.com") } }));
        Assert.Equal((302, ""), await UsageHttp.Request(redirect, "synthetic-token"));
        using var large = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 1024 * 1024 + 1)) }));
        Assert.Equal((0, ""), await UsageHttp.Request(large, "synthetic-token"));
        using var unknownLength = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekable(new byte[1024 * 1024 + 1])) }));
        Assert.Equal((0, ""), await UsageHttp.Request(unknownLength, "synthetic-token"));
    }
    private sealed class NonSeekable(byte[] bytes) : MemoryStream(bytes)
    { public override bool CanSeek => false; }
    [Fact]
    public async Task ImmediateExitProcessAndSafeCmdArguments()
    {
        var info = CodexUsageProcess.StartInfo(Path.Combine(Root, "space folder", "codex.cmd"));
        Assert.EndsWith("cmd.exe", info.FileName); Assert.Contains("/d /s /c \"\"", info.Arguments); Assert.True(info.CreateNoWindow); Assert.False(info.UseShellExecute);
        Assert.Throws<ArgumentException>(() => CodexUsageProcess.StartInfo("bad%PATH%.cmd"));
        // Native immediate-exit command on either test OS; no installed Codex process or credentials.
        var executable = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe") : "/usr/bin/true";
        var result = await CodexUsageProcess.Query(executable); Assert.Null(result.Account); Assert.Null(result.Limits);
    }
    [Fact]
    public async Task SyntheticServerExercisesRpcAndOutputLimit()
    {
        var directory = Path.Combine(Root, "server with spaces"); Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "codex.cmd" : "codex");
        var account = """{"id":2,"result":{"account":{"email":"synthetic@example.com","planType":"example"}}}""";
        var limits = """{"id":3,"result":{"rateLimits":{"primary":{"usedPercent":28,"windowDurationMins":300},"secondary":null}}}""";
        var script = OperatingSystem.IsWindows()
            ? "@echo off\r\npowershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0server.ps1\"\r\n"
            : "#!/bin/sh\nread -r init\nprintf '%s\\n' '{\"id\":1,\"result\":{}}'\nread -r notification\nread -r account\nread -r limits\nprintf '%s\\n' '" + account + "' '" + limits + "'\n";
        if (OperatingSystem.IsWindows())
        {
            // Console.ReadLine preserves buffered lines; cmd set /p can discard piped input.
            var server = "[Console]::In.ReadLine() | Out-Null\r\n"
                + "[Console]::Out.WriteLine('{\"id\":1,\"result\":{}}')\r\n[Console]::Out.Flush()\r\n"
                + "1..3 | ForEach-Object { [Console]::In.ReadLine() | Out-Null }\r\n"
                + "[Console]::Out.WriteLine('" + account + "')\r\n"
                + "[Console]::Out.WriteLine('" + limits + "')\r\n[Console]::Out.Flush()\r\n";
            File.WriteAllText(Path.Combine(directory, "server.ps1"), server);
        }
        File.WriteAllText(executable, script);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var result = await CodexUsageProcess.Query(executable);
        Assert.Equal("72%", UsageParser.Codex(result.Account, result.Limits, DateTimeOffset.UtcNow)?.Display(DateTimeOffset.UtcNow));
        var large = Path.Combine(directory, "large.txt"); File.WriteAllText(large, new string('x', 1024 * 1024 + 1));
        File.WriteAllText(executable, OperatingSystem.IsWindows() ? "@echo off\r\ntype \"" + large + "\"\r\n" : "#!/bin/sh\ncat '" + large + "'\n");
        result = await CodexUsageProcess.Query(executable); Assert.Null(result.Account); Assert.Null(result.Limits);
    }
    [Fact]
    public void SettingsPersistOptInAndCodexPathWithDefaultOff()
    {
        var store = new SettingsStore(Path.Combine(Root, "settings.json")); Assert.False(store.Load().ClaudeUsageDirectEnabled);
        store.Save(new(ClaudeUsageDirectEnabled: true, CodexPath: @"C:\example\codex.cmd"));
        Assert.True(store.Load().ClaudeUsageDirectEnabled); Assert.Equal(@"C:\example\codex.cmd", store.Load().CodexPath);
    }
}
