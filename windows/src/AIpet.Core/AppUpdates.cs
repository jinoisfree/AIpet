using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace AIpet.Core;

public sealed record AppVersion(int Major, int Minor, int Patch, int? Preview = null) : IComparable<AppVersion>
{
    public static AppVersion? Parse(string text)
    {
        var match = Regex.Match(text.Split('+')[0], @"\A(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-preview\.(0|[1-9]\d*))?\z");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var major)
            || !int.TryParse(match.Groups[2].Value, out var minor) || !int.TryParse(match.Groups[3].Value, out var patch)) return null;
        int? preview = null;
        if (match.Groups[4].Success)
        {
            if (!int.TryParse(match.Groups[4].Value, out var number)) return null;
            preview = number;
        }
        return new(major, minor, patch, preview);
    }
    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;
        foreach (var order in new[] { Major.CompareTo(other.Major), Minor.CompareTo(other.Minor), Patch.CompareTo(other.Patch) })
            if (order != 0) return order;
        if (Preview == other.Preview) return 0;
        if (Preview is null) return 1;
        return other.Preview is null ? -1 : Preview.Value.CompareTo(other.Preview.Value);
    }
    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Preview is { } preview ? $"-preview.{preview}" : "");
}

public sealed record AppUpdate(AppVersion Version, string Tag, Uri DownloadUrl, long Size, string Sha256)
{
    public const string Repository = "jinoisfree/AIpet", Installer = "AIpet-Setup-win-x64.exe";
    public const long MaxDownloadBytes = 256L * 1024 * 1024;
    public static readonly Uri ReleasesUrl = new($"https://api.github.com/repos/{Repository}/releases?per_page=100");
    public bool IsValid => Tag == "windows-v" + Version && Size is > 0 and <= MaxDownloadBytes
        && Regex.IsMatch(Sha256, @"\A[0-9a-fA-F]{64}\z")
        && DownloadUrl.AbsoluteUri == $"https://github.com/{Repository}/releases/download/{Tag}/{Installer}";

    public static AppUpdate? Latest(JsonElement releases, AppVersion current)
    {
        if (releases.ValueKind != JsonValueKind.Array) throw new InvalidDataException("릴리스 응답을 확인할 수 없습니다.");
        AppUpdate? best = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
                || !DateTimeOffset.TryParse(release.String("published_at"), out _)) continue;
            var tag = release.String("tag_name");
            if (tag is null || !tag.StartsWith("windows-v", StringComparison.Ordinal)
                || AppVersion.Parse(tag[9..]) is not { } version || version.CompareTo(current) <= 0
                || best is not null && version.CompareTo(best.Version) <= 0) continue;
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            var installers = assets.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object && a.String("name") == Installer).ToArray();
            if (installers.Length != 1) continue;
            var asset = installers[0];
            if (asset.String("state") != "uploaded" || !asset.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var bytes)
                || asset.String("digest") is not { } digest || !digest.StartsWith("sha256:", StringComparison.Ordinal)
                || !Uri.TryCreate(asset.String("browser_download_url"), UriKind.Absolute, out var url)) continue;
            var candidate = new AppUpdate(version, tag, url, bytes, digest[7..]);
            if (candidate.IsValid) best = candidate;
        }
        return best;
    }
}

public sealed record UpdatePreferences(bool Automatic = true, DateTimeOffset? LastCheck = null,
    string? DeferredVersion = null, DateTimeOffset? DeferredUntil = null)
{
    public bool CheckDue(DateTimeOffset now) => Automatic && (LastCheck is null || now < LastCheck || now - LastCheck >= TimeSpan.FromHours(6));
    public bool ShouldPrompt(AppVersion version, DateTimeOffset now) => DeferredVersion != version.ToString() || DeferredUntil is null || now >= DeferredUntil;
    public UpdatePreferences Defer(AppVersion version, DateTimeOffset now) => this with { DeferredVersion = version.ToString(), DeferredUntil = now.AddDays(1) };
}

public sealed class UpdatePreferencesStore(string path)
{
    public UpdatePreferences Load()
    {
        try { return JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(path), JsonData.Options) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(UpdatePreferences value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonData.Sorted(value, true)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class GitHubUpdateClient : IDisposable
{
    private readonly HttpClient client;
    private readonly bool ownsClient;
    public GitHubUpdateClient(HttpClient? client = null)
    {
        ownsClient = client is null;
        this.client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }
    private static HttpRequestMessage Request(Uri url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("AIpet-Updater/1.0");
        if (url.Host == "api.github.com")
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        }
        return request;
    }
    public async Task<AppUpdate?> Check(AppVersion current, CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = Request(AppUpdate.ReleasesUrl);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        const int limit = 8 * 1024 * 1024;
        if (response.Content.Headers.ContentLength is > limit) throw new InvalidDataException("릴리스 응답이 너무 큽니다.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var data = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - (int)data.Length)), timeout.Token).ConfigureAwait(false);
            if (count == 0) break;
            data.Write(buffer, 0, count);
            if (data.Length > limit) throw new InvalidDataException("릴리스 응답이 너무 큽니다.");
        }
        using var document = JsonDocument.Parse(data.ToArray());
        return AppUpdate.Latest(document.RootElement, current);
    }
    private static bool IsAssetHost(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
    private async Task<HttpResponseMessage> DownloadResponse(Uri url, CancellationToken cancellation)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (!IsAssetHost(url)) throw new InvalidDataException("업데이트 다운로드 주소를 확인할 수 없습니다.");
            using var request = Request(url);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK) return response;
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var next = response.Headers.Location;
                response.Dispose();
                if (next is null) break;
                url = next.IsAbsoluteUri ? next : new Uri(url, next);
                continue;
            }
            response.Dispose();
            throw new IOException("업데이트 파일을 다운로드할 수 없습니다.");
        }
        throw new IOException("업데이트 다운로드 주소를 확인할 수 없습니다.");
    }
    public async Task<string> Download(AppUpdate update, string cacheRoot, IProgress<int>? progress = null, CancellationToken cancellation = default)
    {
        if (!update.IsValid) throw new InvalidDataException("업데이트 정보를 확인할 수 없습니다.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var directory = Path.Combine(cacheRoot, "download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, AppUpdate.Installer);
        try
        {
            using var response = await DownloadResponse(update.DownloadUrl, timeout.Token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is { } length && length != update.Size) throw new InvalidDataException("다운로드 크기가 일치하지 않습니다.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var file = new FileStream(path + ".part", FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536]; long total = 0; int reported = -1;
                while (true)
                {
                    var count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    total += count;
                    if (total > update.Size) throw new InvalidDataException("다운로드 크기가 일치하지 않습니다.");
                    hash.AppendData(buffer, 0, count);
                    await file.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                    var percent = (int)(total * 100 / update.Size);
                    if (reported != percent) { reported = percent; progress?.Report(percent); }
                }
                if (total != update.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(update.Sha256)))
                    throw new InvalidDataException("업데이트 파일 검증에 실패했습니다. 기존 앱은 변경하지 않았습니다.");
            }
            timeout.Token.ThrowIfCancellationRequested();
            File.Move(path + ".part", path);
            return path;
        }
        catch
        {
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
    public void Dispose() { if (ownsClient) client.Dispose(); }
}
