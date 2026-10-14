namespace AIpet.Core;

[Flags]
public enum WindowRecovery { None = 0, Show = 1, RestoreTopmost = 2, LogCloaked = 4 }
public static class WindowGuard
{
    public static WindowRecovery Decide(bool intendedVisible, bool osVisible, bool minimized, bool topmost, bool cloaked, bool covered = false)
    {
        if (!intendedVisible) return WindowRecovery.None;
        // Desktop following runs first; do not force-show a window that is still cloaked.
        if (cloaked) return WindowRecovery.LogCloaked;
        return (!osVisible || minimized ? WindowRecovery.Show : WindowRecovery.None)
            | (!topmost || covered ? WindowRecovery.RestoreTopmost : WindowRecovery.None);
    }
    // Called only for windows above the pet in Z order, with physical pixel rectangles.
    public static bool CoversPet(Rect pet, Rect other, bool visible, bool minimized, bool cloaked, bool sameProcess)
        => visible && !minimized && !cloaked && !sameProcess
            && other.Width > 0 && other.Height > 0 && pet.Intersects(other);
}
