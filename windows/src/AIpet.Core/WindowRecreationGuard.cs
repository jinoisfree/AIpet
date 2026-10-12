namespace AIpet.Core;

public enum WindowRecreation { None, Recreate, LimitReached }

// Caller supplies monotonic elapsed seconds; failed creations also consume an attempt.
public sealed class WindowRecreationGuard
{
    private readonly Queue<double> attempts = new();
    public int AttemptsInWindow => attempts.Count;
    public WindowRecreation Decide(bool windowExists, bool intendedVisible, bool quitting, double now)
    {
        while (attempts.TryPeek(out var oldest) && now - oldest >= 60) attempts.Dequeue();
        if (windowExists || !intendedVisible || quitting) return WindowRecreation.None;
        if (attempts.Count >= 5) return WindowRecreation.LimitReached;
        attempts.Enqueue(now);
        return WindowRecreation.Recreate;
    }
}
