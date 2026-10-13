namespace AIpet.Core;

[Flags]
public enum WindowRecovery { None = 0, Show = 1, RestoreTopmost = 2, LogCloaked = 4 }
public static class WindowGuard
{
    public static WindowRecovery Decide(bool intendedVisible, bool osVisible, bool minimized, bool topmost, bool cloaked)
    {
        if (!intendedVisible) return WindowRecovery.None;
        // Desktop following runs first; do not force-show a window that is still cloaked.
        if (cloaked) return WindowRecovery.LogCloaked;
        return (!osVisible || minimized ? WindowRecovery.Show : WindowRecovery.None)
            | (!topmost ? WindowRecovery.RestoreTopmost : WindowRecovery.None);
    }
}
