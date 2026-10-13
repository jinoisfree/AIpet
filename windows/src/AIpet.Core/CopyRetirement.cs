using System.Text.Json;
namespace AIpet.Core;

public sealed record CopyVersion(string? Product, string? OriginalName, string? InternalName, string? Version);
public sealed record CopyProcess(int Id, string Path, bool SameUserAndSession);
public static class CopyIdentity
{
    public static bool IsPet(CopyVersion? info) => info?.Product == "AIpet" &&
        new[] { info.OriginalName, info.InternalName }.Any(n => n is "AIpet.dll" or "AIpet.exe" or "AIpet");
    // Windows spelling normalization, also testable without Windows. Runtime resolves junctions first.
    public static string PathKey(string path)
    {
        var text = path.Replace('\\', '/');
        if (text.StartsWith("//?/UNC/", StringComparison.OrdinalIgnoreCase)) text = "//" + text[8..];
        else if (text.StartsWith("//?/")) text = text[4..];
        if (!(text.StartsWith('/') || text.Length > 2 && text[1] == ':' && text[2] == '/')) return "";
        var parts = new List<string>();
        foreach (var part in text.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count > 1) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(part);
        }
        return (text.StartsWith("//") ? "//" : text.StartsWith('/') ? "/" : "") + string.Join('/', parts);
    }
    public static bool SamePath(string first, string second) => PathKey(first) is { Length: > 0 } key && key.Equals(PathKey(second), StringComparison.OrdinalIgnoreCase);
    public static bool ShouldTakeOver(string currentPath, CopyVersion current, CopyProcess other, CopyVersion? version, int self) =>
        other.Id != self && other.SameUserAndSession && IsPet(version) &&
        (!SamePath(currentPath, other.Path) || current.Version != version!.Version);
}
public interface ICopyEnvironment
{
    int SelfId { get; }
    string CurrentPath { get; }
    IEnumerable<CopyProcess> Processes();
    string Resolve(string path);
    CopyVersion? Version(string path);
    bool Stop(CopyProcess process, TimeSpan timeout);
    bool Exists(string path);
    void Delete(string path);
}
public sealed class CopyLocations(string path)
{
    public static string DefaultPath => Path.Combine(DiagnosticOutput.DefaultRoot, "locations.json");
    public string[] Read()
    {
        try { if (new FileInfo(path).Length > 128 * 1024) return []; return (JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? []).Where(p => !string.IsNullOrEmpty(p)).TakeLast(20).ToArray(); }
        catch { return []; }
    }
    public void Add(string location) => Write(Read().Where(p => !CopyIdentity.SamePath(p, location)).Append(location));
    public void Write(IEnumerable<string> locations)
    {
        string? temp = null;
        try
        {
            var list = locations.Where(p => CopyIdentity.PathKey(p).Length > 0).GroupBy(CopyIdentity.PathKey, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).TakeLast(20).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(list)); File.Move(temp, path, true);
        }
        catch { }
        finally { if (temp is not null) { try { File.Delete(temp); } catch { } } }
    }
}
public sealed class CopyRetirement(ICopyEnvironment environment, CopyLocations locations, Action<string> log)
{
    private void Note(string result) { try { log(result); } catch { } }
    public bool TakeOver()
    {
        var changed = false;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var own = environment.Version(environment.CurrentPath); if (!CopyIdentity.IsPet(own)) return false;
            foreach (var process in environment.Processes())
            {
                try
                {
                    if (!CopyIdentity.ShouldTakeOver(environment.CurrentPath, own!, process, environment.Version(process.Path), environment.SelfId)) continue;
                    var remaining = TimeSpan.FromSeconds(10) - clock.Elapsed;
                    if (remaining <= TimeSpan.Zero) break;
                    var stopped = environment.Stop(process, remaining); changed |= stopped;
                    Note(stopped ? "takeover_stopped" : "takeover_failed");
                }
                catch { Note("takeover_failed"); }
            }
        }
        catch { Note("enumeration_failed"); }
        return changed;
    }
    public void Retire()
    {
        var candidates = new List<string>(locations.Read());
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            foreach (var process in environment.Processes())
            {
                try
                {
                    if (process.Id == environment.SelfId || !process.SameUserAndSession || !CopyIdentity.IsPet(environment.Version(process.Path))) continue;
                    var remaining = TimeSpan.FromSeconds(10) - clock.Elapsed;
                    if (remaining > TimeSpan.Zero && environment.Stop(process, remaining)) { candidates.Add(process.Path); Note("stopped"); }
                    else { blocked.Add(CopyIdentity.PathKey(environment.Resolve(process.Path))); Note("stop_failed"); }
                }
                catch { blocked.Add(CopyIdentity.PathKey(process.Path)); Note("stop_failed"); }
            }
        }
        catch { Note("enumeration_failed"); }
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var path = environment.Resolve(candidate);
                if (CopyIdentity.SamePath(path, environment.CurrentPath) || blocked.Contains(CopyIdentity.PathKey(path))) continue;
                if (!Path.GetFileName(path).Equals("AIpet.exe", StringComparison.OrdinalIgnoreCase) || !environment.Exists(path) || !CopyIdentity.IsPet(environment.Version(path))) continue;
                environment.Delete(path); Note("deleted_AIpet.exe");
            }
            catch { Note("delete_failed_AIpet.exe"); }
        }
        locations.Write(candidates.Where(p => { try { return environment.Exists(p); } catch { return true; } }));
    }
}
