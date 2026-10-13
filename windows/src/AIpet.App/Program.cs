using System.Runtime.InteropServices;
namespace AIpet.App;
public static class Program
{
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
    internal static readonly ErrorLog Errors = new();
    [STAThread]
    public static int Main(string[] args)
    {
        // Hooks bypass GUI/error handlers and remain silent even on unexpected failures.
        if (args.Contains("--hook"))
        {
            try { CommandEntry.TryRun(args); } catch { }
            return 0;
        }
        if (args.Contains("--snapshot") || args.Contains("--usage-snapshot"))
        {
            try
            {
                var output = GetStdHandle(-11);
                if (output == IntPtr.Zero || output == new IntPtr(-1)) AttachConsole(uint.MaxValue);
                CommandEntry.TryRun(args, out var code, new DiagnosticOutput(() => {
                    var handle = GetStdHandle(-11);
                    if (handle == IntPtr.Zero || handle == new IntPtr(-1)) throw new IOException();
                    return Console.OpenStandardOutput();
                }));
                return code;
            }
            catch { return 1; }
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => {
            if (e.ExceptionObject is Exception error) Errors.Append(error);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => {
            foreach (var error in e.Exception.Flatten().InnerExceptions) Errors.Append(error);
            e.SetObserved();
        };
        try { return Run(args); }
        catch (Exception error) { Errors.Append(error); return 1; }
    }
    private static int Run(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\AIpet.Windows.DesktopPet", out var created);
        if (!created) return 0;
        try { return new PetApplication().Run(); }
        finally { mutex.ReleaseMutex(); }
    }
}
