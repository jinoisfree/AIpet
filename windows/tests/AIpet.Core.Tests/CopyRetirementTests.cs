using AIpet.Core;
namespace AIpet.Core.Tests;
public sealed class CopyRetirementTests : TestSupport
{
    private static CopyVersion Pet(string version = "1") => new("AIpet", "AIpet.dll", "AIpet", version);
    private sealed class Fake(string root) : ICopyEnvironment
    {
        public int SelfId => 1;
        public string CurrentPath => Path.Combine(root, "installed", "AIpet.exe");
        private readonly Dictionary<string, CopyVersion?> Files = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<CopyProcess> Running = [];
        public readonly List<int> Stopped = [];
        public readonly List<string> Deleted = [];
        public readonly HashSet<int> StopFailures = [];
        private readonly HashSet<string> DeleteFailures = new(StringComparer.OrdinalIgnoreCase);
        public void Register(string path, CopyVersion? version) => Files[Resolve(path)] = version;
        public void FailDelete(string path) => DeleteFailures.Add(Resolve(path));
        public IEnumerable<CopyProcess> Processes() => Running;
        public string Resolve(string path) => CopyIdentity.PathKey(path);
        public CopyVersion? Version(string path) => Files.GetValueOrDefault(Resolve(path));
        public bool Stop(CopyProcess process, TimeSpan timeout) { Stopped.Add(process.Id); return !StopFailures.Contains(process.Id); }
        public bool Exists(string path) => Files.ContainsKey(Resolve(path));
        public void Delete(string path) { path = Resolve(path); if (DeleteFailures.Contains(path)) throw new IOException(); Deleted.Add(path); Files.Remove(path); }
    }
    [Fact] public void IdentityRequiresBothProductAndOriginalOrInternalName()
    {
        Assert.True(CopyIdentity.IsPet(Pet()));
        Assert.True(CopyIdentity.IsPet(new("AIpet", null, "AIpet.exe", "1")));
        Assert.False(CopyIdentity.IsPet(new("Unrelated", "AIpet.exe", "AIpet", "1")));
        Assert.False(CopyIdentity.IsPet(new("AIpet", "other.exe", "other", "1")));
        Assert.False(CopyIdentity.IsPet(null));
    }
    [Fact] public void NormalizesWindowsSpellingWithoutAcceptingRelativePaths()
    {
        Assert.True(CopyIdentity.SamePath(@"C:\Apps\AIpet.exe", @"\\?\c:\Apps\.\folder\..\aipet.EXE"));
        Assert.True(CopyIdentity.SamePath(@"\\server\share\AIpet.exe", @"\\?\UNC\server\share\aipet.exe"));
        Assert.False(CopyIdentity.SamePath("AIpet.exe", "AIpet.exe"));
    }
    [Fact] public void RetirementProtectsSelfCurrentPathUnknownAndOtherUsers()
    {
        var env = new Fake(Root); var locations = new CopyLocations(Path.Combine(Root, "locations.json")); var log = new List<string>();
        var old = Path.Combine(Root, "old", "AIpet.exe"); var unknown = Path.Combine(Root, "unknown", "AIpet.exe");
        env.Register(env.CurrentPath, Pet()); env.Register(old, Pet()); env.Register(unknown, new("Other", "AIpet.exe", null, "1"));
        env.Running.AddRange([new(1, env.CurrentPath, true), new(2, old, true), new(3, unknown, true), new(4, old, false)]);
        locations.Write([env.CurrentPath.ToUpperInvariant(), old, unknown]);
        new CopyRetirement(env, locations, log.Add).Retire();
        Assert.Equal(new[] { 2 }, env.Stopped); Assert.Equal(new[] { CopyIdentity.PathKey(old) }, env.Deleted);
        Assert.True(env.Exists(env.CurrentPath)); Assert.True(env.Exists(unknown));
        Assert.DoesNotContain(old, locations.Read()); Assert.DoesNotContain(Root, string.Join("\n", log));
    }
    [Fact] public void FailuresDoNotStopOtherCleanupOrDeleteLiveFailedCopy()
    {
        var env = new Fake(Root); var locations = new CopyLocations(Path.Combine(Root, "locations.json"));
        var files = Enumerable.Range(0, 4).Select(i => Path.Combine(Root, i.ToString(), "AIpet.exe")).ToArray();
        foreach (var p in files) env.Register(p, Pet());
        env.Running.Add(new(2, files[0], true)); env.StopFailures.Add(2); env.FailDelete(files[1]);
        locations.Write(files.Append(Path.Combine(Root, "gone", "AIpet.exe")));
        new CopyRetirement(env, locations, _ => { }).Retire();
        Assert.Equal(files.Skip(2).Select(CopyIdentity.PathKey), env.Deleted); Assert.True(env.Exists(files[0])); Assert.True(env.Exists(files[1])); Assert.Equal(2, locations.Read().Length);
    }
    [Fact] public void TakeoverChangesOnlyDifferentVerifiedInstancesAndNeverDeletes()
    {
        var env = new Fake(Root); env.Register(env.CurrentPath, Pet());
        var other = Path.Combine(Root, "other", "AIpet.exe"); env.Register(other, Pet());
        env.Running.AddRange([new(1, env.CurrentPath, true), new(2, env.CurrentPath, true), new(3, other, true)]);
        var locations = new CopyLocations(Path.Combine(Root, "locations.json"));
        Assert.True(new CopyRetirement(env, locations, _ => { }).TakeOver());
        Assert.Equal(new[] { 3 }, env.Stopped); Assert.Empty(env.Deleted);
        Assert.True(CopyIdentity.ShouldTakeOver(env.CurrentPath, Pet("2"), new(2, env.CurrentPath, true), Pet("1"), 1));
        Assert.False(CopyIdentity.ShouldTakeOver(env.CurrentPath, Pet(), new(2, other, false), Pet(), 1));
    }
    [Fact] public void BackslashRegistrationSupportsMixedCaseLookupDeletionAndPruning()
    {
        var env = new Fake(Root);
        const string old = @"C:\Users\example\old\AIpet.exe";
        const string failed = @"C:\Users\example\blocked\AIpet.exe";
        env.Register(old, Pet()); env.Register(failed, Pet());
        env.FailDelete(failed.ToUpperInvariant().Replace('\\', '/'));
        Assert.True(env.Exists(old.ToUpperInvariant().Replace('\\', '/')));
        Assert.True(CopyIdentity.IsPet(env.Version(old)));
        var locations = new CopyLocations(Path.Combine(Root, "locations.json"));
        locations.Write([old, failed]);
        new CopyRetirement(env, locations, _ => { }).Retire();
        Assert.Equal(new[] { CopyIdentity.PathKey(old) }, env.Deleted);
        Assert.False(env.Exists(old)); Assert.True(env.Exists(failed));
        Assert.True(CopyIdentity.SamePath(failed, Assert.Single(locations.Read())));
    }
    [Fact] public void LocationStoreBoundsDeduplicatesAndRecoversFromCorruptFile()
    {
        var path = Path.Combine(Root, "locations.json"); var store = new CopyLocations(path);
        File.WriteAllText(path, "invalid"); Assert.Empty(store.Read());
        for (int i = 0; i < 25; i++) store.Add(Path.Combine(Root, i.ToString(), "AIpet.exe"));
        Assert.Equal(20, store.Read().Length);
        store.Add(store.Read()[0].ToUpperInvariant()); Assert.Equal(20, store.Read().Length);
        Assert.Empty(Directory.GetFiles(Root, "*.tmp"));
    }
}
