namespace AIpet.Core;
// Image measurement is supplied by the UI adapter. Atlas coordinates increase downward.
public readonly record struct SpriteContentMetrics(Rect Bounds, int OpaquePixels)
{
    public static SpriteContentMetrics? Measure(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 != rgba.Length) return null;
        int left = width, top = height, right = 0, bottom = 0, count = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (rgba[(y * width + x) * 4 + 3] < 64) continue;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); count++;
        }
        return count == 0 ? null : new(new(left, top, right - left, bottom - top), count);
    }
}
public readonly record struct PetSpritePresentation(double Scale, Point Offset)
{
    public static Size CellSize => new(192, 208);
    public static PetSpritePresentation Matching(SpriteContentMetrics basis, SpriteContentMetrics reference, double? fixedScale = null)
    {
        var heightRatio = reference.Bounds.Height / basis.Bounds.Height;
        var areaRatio = Math.Sqrt((double)reference.OpaquePixels / basis.OpaquePixels);
        var scale = fixedScale ?? Math.Min(1, Math.Min(heightRatio, areaRatio));
        return new(scale, new(reference.Bounds.MidX - basis.Bounds.MidX * scale, reference.Bounds.MaxY - basis.Bounds.MaxY * scale));
    }
    public Rect DrawingRect(Rect cell) => new(cell.MinX + Offset.X * cell.Width / CellSize.Width, cell.MinY + (CellSize.Height * (1 - Scale) - Offset.Y) * cell.Height / CellSize.Height, cell.Width * Scale, cell.Height * Scale);
    public Rect ContentBounds(Rect source) => new(Offset.X + source.MinX * Scale, Offset.Y + source.MinY * Scale, source.Width * Scale, source.Height * Scale);
    public static Rect InteractionRect(Rect reference, Rect cell) => new(cell.MinX + reference.MinX * cell.Width / CellSize.Width, cell.MinY + (CellSize.Height - reference.MaxY) * cell.Height / CellSize.Height, reference.Width * cell.Width / CellSize.Width, reference.Height * cell.Height / CellSize.Height);
}
