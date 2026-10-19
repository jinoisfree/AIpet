using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIpet.Core;
namespace AIpet.Core.Tests;

public sealed class AppUpdateTests : TestSupport
{
    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("synthetic installer, never executable");
    private static readonly AppVersion Current = AppVersion.Parse("1.0.11-preview.3")!;
    private static JsonObject Release(string version = "1.0.12-preview.1") => new() {
        ["tag_name"] = "windows-v" + version, ["draft"] = false, ["prerelease"] = true, ["published_at"] = "2026-10-15T00:00:00Z",
        ["assets"] = new JsonArray(new JsonObject {
            ["name"] = AppUpdate.Installer, ["state"] = "uploaded", ["size"] = Installer.Length,
            ["digest"] = "sha256:" + Convert.ToHexString(SHA256.HashData(Installer)).ToLowerInvariant(),
            ["browser_download_url"] = $"https://github.com/jinoisfree/AIpet/releases/download/windows-v{version}/{AppUpdate.Installer}"
        })
    };
    private static AppUpdate? Parse(params JsonObject[] releases) => AppUpdate.Latest(JsonSerializer.SerializeToElement(releases), Current);
    private static AppUpdate Update => Parse(Release())!;
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); }
    }
    private sealed class UnknownLength(byte[] bytes) : MemoryStream(bytes) { public override bool CanSeek => false; }
    private static HttpResponseMessage Body(byte[] bytes) => new(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLength(bytes)) };

    [Theory]
    [InlineData("1.0.12-preview.1", "1.0.11", 1)]
    [InlineData("1.0.11-preview.10", "1.0.11-preview.3", 1)]
    [InlineData("1.0.11", "1.0.11-preview.99", 1)]
    [InlineData("1.0.10", "1.0.11-preview.1", -1)]
    [InlineData("1.0.11-preview.3+commit", "1.0.11-preview.3", 0)]
    public void VersionsCompareNumerically(string first, string second, int order)
        => Assert.Equal(order, Math.Sign(AppVersion.Parse(first)!.CompareTo(AppVersion.Parse(second))));
    [Theory]
    [InlineData("1.0")] [InlineData("v1.0.12")] [InlineData("01.0.12")] [InlineData("1.0.12-preview.-1")]
    [InlineData("1.0.12-preview.01")] [InlineData("1.0.12/other")] [InlineData("99999999999.0.1")]
    public void InvalidVersionsAreRejected(string value) => Assert.Null(AppVersion.Parse(value));
    [Fact] public void PicksNewestWindowsReleaseWithoutOfferingMacDraftOrDowngrade()
    {
        var mac = Release("9.0.0"); mac["tag_name"] = "v9.0.0";
        var draft = Release("8.0.0"); draft["draft"] = true;
        Assert.Equal("1.0.12-preview.10", Parse(Release("1.0.12-preview.2"), mac, draft, Release("1.0.12-preview.10"), Release("1.0.10"))!.Version.ToString());
        Assert.Null(Parse(Release("1.0.11-preview.3"), Release("1.0.11-preview.2"), mac, draft));
    }
    [Theory]
    [InlineData("digest", "sha256:bad")] [InlineData("digest", "")] [InlineData("size", "123")]
    [InlineData("state", "new")] [InlineData("name", "other.exe")]
    [InlineData("browser_download_url", "https://evil.example/setup.exe")]
    [InlineData("browser_download_url", "http://github.com/jinoisfree/AIpet/releases/download/windows-v1.0.12-preview.1/AIpet-Setup-win-x64.exe")]
    public void IncompleteOrUntrustedAssetsAreNotInstallable(string key, string value)
    {
        var release = Release(); release["assets"]![0]![key] = value;
        Assert.Null(Parse(release));
    }
    [Fact] public void RejectsMissingMetadataDuplicateAssetsAndImpossibleSizes()
    {
        var release = Release(); release.Remove("published_at"); Assert.Null(Parse(release));
        release = Release(); release["assets"]!.AsArray().Add(release["assets"]![0]!.DeepClone()); Assert.Null(Parse(release));
        foreach (var size in new[] { 0L, -1L, AppUpdate.MaxDownloadBytes + 1 })
        { release = Release(); release["assets"]![0]!["size"] = size; Assert.Null(Parse(release)); }
    }
    [Fact] public async Task CheckOnlyReadsPublicReleaseMetadata()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request => {
            calls++; Assert.Equal(AppUpdate.ReleasesUrl, request.RequestUri); Assert.Null(request.Headers.Authorization);
            Assert.NotEmpty(request.Headers.UserAgent); Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
            return new(HttpStatusCode.OK) { Content = new StringContent(new JsonArray(Release()).ToJsonString()) };
        }));
        using var client = new GitHubUpdateClient(http);
        Assert.Equal(Update, await client.Check(Current)); Assert.Equal(1, calls);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Root));
    }
    [Fact] public async Task DownloadValidatesTheFileBeforeReturningIt()
    {
        File.WriteAllText(Path.Combine(Root, "existing.exe"), "old app");
        using var http = new HttpClient(new Handler(_ => Body(Installer)));
        using var client = new GitHubUpdateClient(http);
        var path = await client.Download(Update, Root);
        Assert.Equal(Installer, File.ReadAllBytes(path)); Assert.EndsWith(AppUpdate.Installer, path);
        Assert.False(File.Exists(path + ".part")); Assert.Equal("old app", File.ReadAllText(Path.Combine(Root, "existing.exe")));
    }
    [Theory] [InlineData("hash")] [InlineData("short")] [InlineData("long")] [InlineData("length")]
    public async Task FailedDownloadsLeaveTheOldAppAndRemovePartialFiles(string failure)
    {
        var bytes = failure == "short" ? Installer[..^1] : failure == "long" ? Installer.Concat(new byte[] { 0 }).ToArray() : Installer.ToArray();
        if (failure == "hash") bytes[0] ^= 1;
        using var http = new HttpClient(new Handler(_ => {
            var response = Body(bytes);
            if (failure == "length") response.Content.Headers.ContentLength = 123;
            return response;
        }));
        using var client = new GitHubUpdateClient(http);
        File.WriteAllText(Path.Combine(Root, "existing.exe"), "old app");
        await Assert.ThrowsAsync<InvalidDataException>(() => client.Download(Update, Root));
        Assert.Empty(Directory.GetDirectories(Root)); Assert.Equal("old app", File.ReadAllText(Path.Combine(Root, "existing.exe")));
    }
    [Fact] public async Task CancellationCleansStagingAndDoesNotInstall()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var http = new HttpClient(new Handler(_ => Body(Installer)));
        using var client = new GitHubUpdateClient(http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Download(Update, Root, cancellation: cancellation.Token));
        Assert.Empty(Directory.GetDirectories(Root));
    }
    [Theory]
    [InlineData("https://release-assets.githubusercontent.com/asset", true)]
    [InlineData("https://evil.example/asset", false)]
    [InlineData("https://github.com.evil.example/asset", false)]
    [InlineData("http://release-assets.githubusercontent.com/asset", false)]
    [InlineData("https://user@release-assets.githubusercontent.com/asset", false)]
    public async Task RedirectsStayOnGitHubHttps(string address, bool allowed)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => ++calls == 1
            ? new(HttpStatusCode.Redirect) { Headers = { Location = new(address) } } : Body(Installer)));
        using var client = new GitHubUpdateClient(http);
        if (allowed) { Assert.True(File.Exists(await client.Download(Update, Root))); Assert.Equal(2, calls); }
        else { await Assert.ThrowsAsync<InvalidDataException>(() => client.Download(Update, Root)); Assert.Equal(1, calls); Assert.Empty(Directory.GetDirectories(Root)); }
    }
    [Fact] public void AutomaticChecksAndDeferralsSurviveRestartWithoutChangingPetSettings()
    {
        var now = DateTimeOffset.UtcNow;
        var preferences = new UpdatePreferences(LastCheck: now).Defer(Update.Version, now);
        Assert.False(preferences.CheckDue(now.AddHours(5.99))); Assert.True(preferences.CheckDue(now.AddHours(6)));
        Assert.False((preferences with { Automatic = false }).CheckDue(now.AddDays(2)));
        Assert.False(preferences.ShouldPrompt(Update.Version, now.AddHours(23))); Assert.True(preferences.ShouldPrompt(Update.Version, now.AddDays(1)));
        Assert.True(preferences.ShouldPrompt(AppVersion.Parse("1.0.13")!, now));
        var store = new UpdatePreferencesStore(Path.Combine(Root, "updates.json"));
        store.Save(preferences); Assert.Equal(preferences, store.Load());
        Assert.Single(Directory.GetFiles(Root));
    }
}
