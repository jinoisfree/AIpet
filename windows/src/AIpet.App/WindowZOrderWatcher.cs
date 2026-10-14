using System.Runtime.InteropServices;
namespace AIpet.App;

// Register and dispose on the window's UI thread. Out-of-context callbacks use that
// thread too; they only request a later check, never change Z order from inside a hook.
internal sealed class WindowZOrderWatcher : IDisposable
{
    private const uint Foreground = 0x0003, ObjectShow = 0x8002, ObjectReorder = 0x8004;
    private readonly WinEventProc callback;
    private readonly IntPtr foregroundHook, showHook, reorderHook;
    private readonly Action requestCheck;
    private bool disposed;

    public WindowZOrderWatcher(Action requestCheck, WindowDiagnostics diagnostics)
    {
        this.requestCheck = requestCheck;
        callback = OnWindowEvent; // Keep the native callback alive until every hook is removed.
        foregroundHook = Register(Foreground);
        showHook = Register(ObjectShow);
        reorderHook = Register(ObjectReorder);
        diagnostics.Record("ZORDER_HOOKS", $"foreground={foregroundHook != IntPtr.Zero}; shown={showHook != IntPtr.Zero}; reorder={reorderHook != IntPtr.Zero}");
    }

    private IntPtr Register(uint eventType) => SetWinEventHook(eventType, eventType, IntPtr.Zero, callback,
        0, 0, 0x0000 | 0x0002); // WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS

    private void OnWindowEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (disposed || window == IntPtr.Zero || objectId != 0 || childId != 0) return; // OBJID_WINDOW / CHILDID_SELF
        if (GetAncestor(window, 2) != window) return; // GA_ROOT: ignore child controls.
        requestCheck();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var hook in new[] { foregroundHook, showHook, reorderHook })
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
        GC.KeepAlive(callback);
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventProc callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
