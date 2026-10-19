namespace AIpet.Core;

public static class ThoughtTrail
{
    public static IReadOnlyList<double> Diameters { get; } = Array.AsReadOnly(new[] { 15.0, 10.5, 6.5 });
    public const double Spacing = 3.5;
    public static double Length => Diameters.Sum() + Spacing * 4;
    public static Rect[] Circles(Rect bubble, Point pet, bool below)
    {
        var edge = below ? bubble.MaxY : bubble.MinY; var direction = below ? 1 : -1;
        var start = Math.Min(Math.Max(pet.X, bubble.MinX + 24), bubble.MaxX - 24);
        var reach = Math.Max(Length, Math.Abs(pet.Y - edge)); var travelled = Spacing;
        return Diameters.Select(diameter => {
            var along = travelled + diameter / 2; travelled += diameter + Spacing;
            var x = start + (pet.X - start) * along / reach - diameter / 2;
            if (diameter == Diameters[0]) x = Math.Min(Math.Max(x, bubble.MinX + 14), bubble.MaxX - 14 - diameter);
            return new Rect(x, edge + direction * along - diameter / 2, diameter, diameter);
        }).ToArray();
    }
}
public readonly record struct PetLayout(double Scale)
{
    public static Size BubbleSize => new(330, 56);
    public static Rect VisiblePet => new(54, 60, 118, 134);
    // Includes the phone at every angle; used only for screen placement.
    public static Rect PlacementPet => new(46, 59, 134, 136);
    public static Rect PlacementSprite(Rect cell)
    {
        var unit = cell.Width / 192;
        return new(cell.MinX + PlacementPet.MinX * unit, cell.MaxY - PlacementPet.MaxY * unit, PlacementPet.Width * unit, PlacementPet.Height * unit);
    }
    public const double PetMargin = 4;
    public static Rect VisibleSprite(Rect cell)
    {
        var unit = cell.Width / 192;
        return new(cell.MinX + VisiblePet.MinX * unit, cell.MaxY - VisiblePet.MaxY * unit, VisiblePet.Width * unit, VisiblePet.Height * unit);
    }
    public static Rect ConstrainedSprite(Rect sprite, Rect screen, Rect? screenFrame = null)
    {
        var safe = screen.Inset(PetMargin, PetMargin); var visible = PlacementSprite(sprite);
        var x = Math.Min(Math.Max(visible.MinX, safe.MinX), Math.Max(safe.MinX, safe.MaxX - visible.Width));
        var y = Math.Min(Math.Max(visible.MinY, safe.MinY), Math.Max(safe.MinY, (screenFrame?.MaxY ?? safe.MaxY) - visible.Height));
        return sprite.Offset(x - visible.MinX, y - visible.MinY);
    }
    public Size SpriteSize => new(192 * Scale, 208 * Scale);
    public static double Gap(double scale, bool below = false) => Math.Max(8, ThoughtTrail.Length - (below ? 208 - VisiblePet.MaxY : VisiblePet.MinY) * scale);
    public static Point TrailEnd(Rect sprite, bool below)
    {
        var unit = sprite.Height / 208;
        return new(sprite.MinX + VisiblePet.MidX * unit, sprite.MaxY - (below ? VisiblePet.MaxY : VisiblePet.MinY) * unit);
    }
    public Size WindowSize => new(Math.Max(BubbleSize.Width, SpriteSize.Width) + 24, SpriteSize.Height + BubbleSize.Height + 24 + Gap(Scale));
    public Rect SpriteRect => new((WindowSize.Width - SpriteSize.Width) / 2, 12, SpriteSize.Width, SpriteSize.Height);
    public Rect BubbleRect => new((WindowSize.Width - BubbleSize.Width) / 2, SpriteRect.MaxY + Gap(Scale), BubbleSize.Width, BubbleSize.Height);
}
public sealed record PetPlacement
{
    public Rect WindowFrame { get; }
    public Rect SpriteRect { get; }
    public Rect BubbleRect { get; }
    public bool BubbleBelow { get; }
    public Rect GlobalSprite => SpriteRect.Offset(WindowFrame.MinX, WindowFrame.MinY);
    public Rect GlobalBubble => BubbleRect.Offset(WindowFrame.MinX, WindowFrame.MinY);
    public PetPlacement(Rect globalSprite, Rect globalBubble)
    {
        WindowFrame = globalSprite.Union(globalBubble).Inset(-12, -12);
        SpriteRect = globalSprite.Offset(-WindowFrame.MinX, -WindowFrame.MinY);
        BubbleRect = globalBubble.Offset(-WindowFrame.MinX, -WindowFrame.MinY);
        BubbleBelow = globalBubble.MidY < globalSprite.MidY;
    }
    public PetPlacement(Point spriteOrigin, double scale, Rect visibleFrame, Rect? screenFrame = null)
    {
        var safe = visibleFrame.Inset(12, 12); var size = new PetLayout(scale).SpriteSize;
        var sprite = PetLayout.ConstrainedSprite(new(spriteOrigin, size), visibleFrame, screenFrame);
        var bubbleSize = PetLayout.BubbleSize;
        BubbleBelow = sprite.MaxY + PetLayout.Gap(scale) + bubbleSize.Height > safe.MaxY;
        var gap = PetLayout.Gap(scale, BubbleBelow);
        if (BubbleBelow) sprite = sprite with { Y = Math.Max(sprite.MinY, safe.MinY + gap + bubbleSize.Height) };
        var bubble = new Rect(Math.Min(Math.Max(sprite.MidX - bubbleSize.Width / 2, safe.MinX), Math.Max(safe.MinX, safe.MaxX - bubbleSize.Width)), BubbleBelow ? sprite.MinY - gap - bubbleSize.Height : sprite.MaxY + gap, bubbleSize.Width, bubbleSize.Height);
        WindowFrame = sprite.Union(bubble).Inset(-12, -12);
        SpriteRect = sprite.Offset(-WindowFrame.MinX, -WindowFrame.MinY);
        BubbleRect = bubble.Offset(-WindowFrame.MinX, -WindowFrame.MinY);
    }
}
public sealed class PetBubbleMotion
{
    public Point? Position { get; private set; }
    private Point target;
    private double? lastTime;
    private Rect bounds;
    public void Retarget(Point point, Rect visibleFrame, double now, bool immediately = false)
    {
        bounds = visibleFrame.Inset(12, 12); target = Clamped(point);
        if (Position is null || immediately) { Position = target; lastTime = now; }
        else Position = Clamped(Position.Value);
    }
    public Point Step(double now)
    {
        var dt = Math.Min(0.1, Math.Max(0, now - (lastTime ?? now))); lastTime = now;
        var previous = Position ?? target; var fraction = 1 - Math.Exp(-dt / 0.065);
        var next = new Point(previous.X + (target.X - previous.X) * fraction, previous.Y + (target.Y - previous.Y) * fraction);
        Position = Math.Sqrt(Math.Pow(next.X - target.X, 2) + Math.Pow(next.Y - target.Y, 2)) < 0.2 ? target : Clamped(next);
        return Position.Value;
    }
    private Point Clamped(Point point) => new(Math.Min(Math.Max(point.X, bounds.MinX), Math.Max(bounds.MinX, bounds.MaxX - PetLayout.BubbleSize.Width)), Math.Min(Math.Max(point.Y, bounds.MinY), Math.Max(bounds.MinY, bounds.MaxY - PetLayout.BubbleSize.Height)));
}
