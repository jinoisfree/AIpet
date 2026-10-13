namespace AIpet.Core;

public enum WindowRecreation { None, Recreate, LimitReached }

// Caller supplies monotonic elapsed seconds; failed creations also consume an attempt.
public sealed class WindowRecreationGuard
{
    private readonly Queue<double> attempts = new();
    private double lastAttempt = double.NegativeInfinity;
    public int Count(double now) { while (attempts.TryPeek(out var t) && now - t >= 60) attempts.Dequeue(); return attempts.Count; }
    public int AttemptsInWindow => attempts.Count;
    public WindowRecreation Decide(bool windowExists, bool intendedVisible, bool quitting, double now, bool force = false)
    {
        while (attempts.TryPeek(out var oldest) && now - oldest >= 60) attempts.Dequeue();
        if (windowExists || !intendedVisible || quitting) return WindowRecreation.None;
        if (!force && attempts.Count >= 5 && now - lastAttempt < 10) return WindowRecreation.LimitReached;
        lastAttempt = now; attempts.Enqueue(now);
        return WindowRecreation.Recreate;
    }
}
