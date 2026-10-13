using System.Runtime.InteropServices;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
namespace AIpet.App;

// The sole boundary between physical Windows pixels (downward) and Core DIPs (upward).
public sealed class ScreenCoordinates
{
    public double Scale { get; }
    public Rect Frame { get; }
    public Rect WorkArea { get; }
    private ScreenCoordinates(Forms.Screen screen)
    {
        var center = new NativePoint { X = screen.Bounds.Left + screen.Bounds.Width / 2, Y = screen.Bounds.Top + screen.Bounds.Height / 2 };
        var monitor = MonitorFromPoint(center, 2);
        Scale = GetDpiForMonitor(monitor, 0, out var x, out _) == 0 ? x / 96.0 : 1;
        Frame = FromPixels(screen.Bounds); WorkArea = FromPixels(screen.WorkingArea);
    }
    public static MonitorDiagnostic[] Monitors() => Forms.Screen.AllScreens.Select(s => new MonitorDiagnostic(
        Pixels(s.Bounds), Pixels(s.WorkingArea), new ScreenCoordinates(s).Scale)).ToArray();
    private static Rect Pixels(System.Drawing.Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
    public Rect SpritePixels(PetPlacement placement, Rect actualWindow)
    {
        var sprite = PetLayout.VisibleSprite(placement.GlobalSprite);
        return new(actualWindow.X + (sprite.X - placement.WindowFrame.X) * Scale,
            actualWindow.Y + (placement.WindowFrame.MaxY - sprite.MaxY) * Scale, sprite.Width * Scale, sprite.Height * Scale);
    }
    public static ScreenCoordinates Nearest(System.Drawing.Point physical) => new(Forms.Screen.FromPoint(physical));
    public static ScreenCoordinates Primary => new(Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]);
    public static System.Drawing.Point CursorPixels => Forms.Cursor.Position;
    public Point Cursor => FromPixels(CursorPixels);
    public Point FromPixels(System.Drawing.Point p) => new(p.X / Scale, -p.Y / Scale);
    public System.Drawing.Point ToPixels(Point p) => new((int)Math.Round(p.X * Scale), (int)Math.Round(-p.Y * Scale));
    private Rect FromPixels(System.Drawing.Rectangle r) => new(r.Left / Scale, -r.Bottom / Scale, r.Width / Scale, r.Height / Scale);
    public static System.Windows.Rect Local(Rect r, double height) => new(r.X, height - r.MaxY, r.Width, r.Height);
    public static Point LocalPoint(System.Windows.Point p, double height) => new(p.X, height - p.Y);
    public void Place(Window window, Rect frame)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(frame.X * Scale), (int)Math.Round(-frame.MaxY * Scale),
            (int)Math.Round(frame.Width * Scale), (int)Math.Round(frame.Height * Scale), 0x0010 | 0x0004);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
