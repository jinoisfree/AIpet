namespace AIpet.Core;
// Core coordinates increase upward. A UI adapter must convert at the display boundary.
public readonly record struct Point(double X, double Y);
public readonly record struct Size(double Width, double Height);
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public Rect(Point origin, Size size) : this(origin.X, origin.Y, size.Width, size.Height) { }
    public double MinX => X;
    public double MinY => Y;
    public double MaxX => X + Width;
    public double MaxY => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;
    public Point Origin => new(X, Y);
    public Size Size => new(Width, Height);
    public Rect Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };
    public Rect Inset(double dx, double dy) => new(X + dx, Y + dy, Width - 2 * dx, Height - 2 * dy);
    public bool Contains(Point p) => p.X >= MinX && p.X < MaxX && p.Y >= MinY && p.Y < MaxY;
    public bool Contains(Rect r) => r.MinX >= MinX && r.MaxX <= MaxX && r.MinY >= MinY && r.MaxY <= MaxY;
    public bool Intersects(Rect r) => r.MinX < MaxX && r.MaxX > MinX && r.MinY < MaxY && r.MaxY > MinY;
    public Rect Union(Rect r) => new(Math.Min(MinX, r.MinX), Math.Min(MinY, r.MinY), Math.Max(MaxX, r.MaxX) - Math.Min(MinX, r.MinX), Math.Max(MaxY, r.MaxY) - Math.Min(MinY, r.MinY));
}
