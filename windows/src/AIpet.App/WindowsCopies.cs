using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
namespace AIpet.App;

internal sealed class WindowsCopies : ICopyEnvironment
{
    public int SelfId => Environment.ProcessId;
    public string CurrentPath => Resolve(Environment.ProcessPath!);
    public string Resolve(string path)
    {
        try
        {
            using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new StringBuilder(32768);
            var count = GetFinalPathNameByHandle(file, buffer, (uint)buffer.Capacity, 0);
            if (count > 0 && count < buffer.Capacity) return buffer.ToString();
        }
        catch { }
        return Path.GetFullPath(path);
    }
    public CopyVersion? Version(string path)
    {
        try { var v = FileVersionInfo.GetVersionInfo(path); return new(v.ProductName, v.OriginalFilename, v.InternalName, v.FileVersion); }
        catch { return null; }
    }
    private static bool Owned(Process process)
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            if (process.SessionId != self.SessionId || !OpenProcessToken(process.Handle, 8, out var token)) return false;
            try { using var identity = new WindowsIdentity(token); using var current = WindowsIdentity.GetCurrent(); return identity.User == current.User; }
            finally { CloseHandle(token); }
        }
        catch { return false; }
    }
    public IEnumerable<CopyProcess> Processes()
    {
        var result = new List<CopyProcess>();
        foreach (var process in Process.GetProcessesByName("AIpet"))
        {
            using (process)
            {
                try { if (process.Id != SelfId && Owned(process) && process.MainModule?.FileName is { } path) result.Add(new(process.Id, Resolve(path), true)); }
                catch { }
            }
        }
        return result;
    }
    public bool Stop(CopyProcess item, TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(item.Id);
            // Recheck ownership and executable on the open process before any termination.
            if (p.Id == SelfId || !Owned(p) || p.MainModule?.FileName is not { } path || !CopyIdentity.SamePath(Resolve(path), item.Path) || !CopyIdentity.IsPet(Version(path))) return false;
            var clock = Stopwatch.StartNew();
            p.CloseMainWindow();
            if (p.WaitForExit((int)Math.Min(1000, timeout.TotalMilliseconds))) return true;
            p.Kill();
            return p.WaitForExit((int)Math.Max(0, (timeout - clock.Elapsed).TotalMilliseconds));
        }
        catch { return false; }
    }
    public bool Exists(string path) => File.Exists(path);
    public void Delete(string path)
    {
        if (CopyIdentity.SamePath(Resolve(path), CurrentPath) || !CopyIdentity.IsPet(Version(path))) return;
        File.Delete(path);
    }
    public static void Retire()
    {
        try
        {
            var log = new BoundedLogFile(Path.Combine(DiagnosticOutput.DefaultRoot, "install.log"), 256 * 1024);
            new CopyRetirement(new WindowsCopies(), new(CopyLocations.DefaultPath), result => log.Append($"{DateTimeOffset.UtcNow:O} {result}\n")).Retire();
        }
        catch { }
    }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
