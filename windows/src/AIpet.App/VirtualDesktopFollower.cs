using System.Runtime.InteropServices;
namespace AIpet.App;

// Created, called and released on the WPF STA thread. Only the app's own pet HWND is moved.
internal sealed class VirtualDesktopFollower(WindowDiagnostics diagnostics) : IDisposable
{
    private IVirtualDesktopManager? manager;
    private bool attempted, errorLogged;
    private void Failure(int result)
    {
        if (errorLogged) return;
        errorLogged = true; diagnostics.Record("DESKTOP_ERROR", $"hresult=0x{result:X}");
    }
    private IVirtualDesktopManager? Manager()
    {
        if (attempted) return manager;
        attempted = true;
        try { manager = (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a"), true)!)!; }
        catch (Exception e) { Failure(e.HResult); }
        return manager;
    }
    public bool? OnCurrent(IntPtr handle)
    {
        try
        {
            if (handle == IntPtr.Zero || Manager() is not { } api) return null;
            var hr = api.IsWindowOnCurrentVirtualDesktop(handle, out var current);
            if (hr < 0) { Failure(hr); return null; }
            return current;
        }
        catch (Exception e) { Failure(e.HResult); return null; }
    }
    public bool Follow(IntPtr handle, bool intended)
    {
        if (!intended || !VisibilityGuard.FollowDesktop(intended, OnCurrent(handle))) return false;
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == handle || manager is null) return false;
            var hr = manager.GetWindowDesktopId(foreground, out var desktop);
            if (hr < 0) { Failure(hr); return false; }
            if (desktop == Guid.Empty) return false;
            hr = manager.MoveWindowToDesktop(handle, ref desktop);
            diagnostics.Record("DESKTOP_FOLLOW", $"success={hr >= 0}; hresult=0x{hr:X}");
            if (hr < 0) Failure(hr);
            return hr >= 0;
        }
        catch (Exception e) { Failure(e.HResult); return false; }
    }
    public void Dispose()
    {
        if (manager is not null) { try { Marshal.FinalReleaseComObject(manager); } catch { } manager = null; }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr window, [MarshalAs(UnmanagedType.Bool)] out bool onCurrent);
        [PreserveSig] int GetWindowDesktopId(IntPtr window, out Guid desktop);
        [PreserveSig] int MoveWindowToDesktop(IntPtr window, [In] ref Guid desktop);
    }
}
