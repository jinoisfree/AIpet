namespace AIpet.Core;

// Monotonic seconds. React promptly to window events while bounding scans during
// event bursts; the periodic check still covers missed events or unavailable hooks.
public sealed class WindowGuardSchedule
{
    private double lastCheck = double.NegativeInfinity;
    private bool requested;

    public void RequestCheck() => requested = true;

    public bool CheckDue(double now)
    {
        if (now - lastCheck < (requested ? 0.1 : 1)) return false;
        lastCheck = now;
        requested = false;
        return true;
    }
}
