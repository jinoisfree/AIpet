using System.Diagnostics;
using System.Runtime.InteropServices;
namespace AIpet.App;

internal sealed class WindowDiagnostics
{
    private readonly WindowLog log = new();
    public void Record(string name, string flags)
    {
        string? processName = null;
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            if (pid != 0) { using var process = Process.GetProcessById((int)pid); processName = process.ProcessName; }
        }
        catch { }
        log.Append(name, flags, processName);
    }
    public void Observe(int message, IntPtr wParam, IntPtr lParam)
    {
        var parameters = $"wParam=0x{wParam.ToInt64():X}, lParam=0x{lParam.ToInt64():X}";
        switch (message)
        {
            case 0x0010: Record("WM_CLOSE", parameters); break;
            case 0x0018: Record("WM_SHOWWINDOW", parameters); break;
            case 0x0002: Record("WM_DESTROY", parameters); break;
            case 0x0112:
                var command = wParam.ToInt64() & 0xFFF0;
                if (command is 0xF060 or 0xF020) Record("WM_SYSCOMMAND", parameters + $", command=0x{command:X}");
                break;
            case 0x0046: case 0x0047:
                if (lParam == IntPtr.Zero) break;
                var position = Marshal.PtrToStructure<WindowPosition>(lParam);
                if ((position.Flags & 0x0080) != 0 || (position.Flags & 0x0004) == 0)
                    Record(message == 0x0046 ? "WM_WINDOWPOSCHANGING" : "WM_WINDOWPOSCHANGED",
                        $"flags=0x{position.Flags:X}, insertAfter=0x{position.InsertAfter.ToInt64():X}");
                break;
        }
    }
    public static WindowStateSnapshot Read(IntPtr handle)
    {
        var result = DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(uint));
        return new(IsWindowVisible(handle), IsIconic(handle), (GetWindowLongPtr(handle, -20).ToInt64() & 0x8) != 0,
            result == 0 ? cloaked : 0, result);
    }
    public static void ShowWithoutActivation(IntPtr handle) => ShowWindow(handle, 4);
    public static void RestoreTopmost(IntPtr handle) => SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    public readonly record struct WindowStateSnapshot(bool Visible, bool Minimized, bool Topmost, uint Cloaked, int DwmResult)
    {
        public override string ToString() => $"visible={Visible}, minimized={Minimized}, topmost={Topmost}, cloaked=0x{Cloaked:X}, dwmResult=0x{DwmResult:X}";
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition { public IntPtr Handle, InsertAfter; public int X, Y, Width, Height; public uint Flags; }
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle, uint attribute, out uint value, int size);
}
