using System.Runtime.InteropServices;
namespace AIpet.Core;

public static class CodexStorePackages
{
    private const string Family = "OpenAI.Codex_2p2nqsd0c76g0";
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint length, IntPtr buffer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagePathByFullName(string fullName, ref uint length, IntPtr path);
    public static IEnumerable<string> Paths()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var result = new List<string>(); IntPtr names = IntPtr.Zero, buffer = IntPtr.Zero;
        try
        {
            uint count = 0, length = 0;
            if (GetPackagesByPackageFamily(Family, ref count, IntPtr.Zero, ref length, IntPtr.Zero) != 122 || count is 0 or > 256 || length is 0 or > 1048576) return result;
            names = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size)); buffer = Marshal.AllocHGlobal(checked((int)length * 2));
            if (GetPackagesByPackageFamily(Family, ref count, names, ref length, buffer) != 0) return result;
            for (var i = 0; i < count; i++)
            {
                var fullName = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, i * IntPtr.Size)); if (fullName is null) continue;
                uint size = 0;
                if (GetPackagePathByFullName(fullName, ref size, IntPtr.Zero) != 122 || size is 0 or > 32768) continue;
                var path = Marshal.AllocHGlobal(checked((int)size * 2));
                try { if (GetPackagePathByFullName(fullName, ref size, path) == 0 && Marshal.PtrToStringUni(path) is { } value && Path.IsPathFullyQualified(value)) result.Add(value); }
                finally { Marshal.FreeHGlobal(path); }
            }
        }
        catch { }
        finally { if (names != IntPtr.Zero) Marshal.FreeHGlobal(names); if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
        return result;
    }
    public static IEnumerable<string> Executables(Func<IEnumerable<string>> packagePaths)
    {
        var result = new List<string>();
        try
        {
            foreach (var package in packagePaths())
            {
                if (!Path.IsPathFullyQualified(package)) continue;
                try
                {
                    var resources = Path.Combine(package, "app", "resources");
                    var direct = Path.Combine(resources, "codex.exe");
                    if (File.Exists(direct)) result.Add(direct);
                    foreach (var directory in Directory.EnumerateDirectories(resources).OrderBy(p => p, StringComparer.Ordinal))
                    {
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                        var candidate = Path.Combine(directory, "codex.exe"); if (File.Exists(candidate)) result.Add(candidate);
                    }
                }
                catch { }
            }
        }
        catch { }
        return result;
    }
}
