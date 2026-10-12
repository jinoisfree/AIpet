using System.Globalization;
namespace AIpet.App;
public sealed class PetSurface : FrameworkElement
{
    private readonly PetWindow pet;
    private readonly SpriteAtlas atlas;
    private static readonly Brush bubble = new SolidColorBrush(Color.FromArgb(248, 247, 249, 244));
    public PetSurface(PetWindow pet, SpriteAtlas atlas)
    {
        this.pet = pet; this.atlas = atlas; Focusable = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters)
    {
        if (pet.Placement is not { } p) return null;
        var point = ScreenCoordinates.LocalPoint(parameters.HitPoint, p.WindowFrame.Height);
        return atlas.Opaque(point, pet.Frame, p.SpriteRect) || pet.BubbleShown && p.BubbleRect.Contains(point) ? new PointHitTestResult(this, parameters.HitPoint) : null;
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (pet.Placement is not { } p) return;
        var height = p.WindowFrame.Height;
        if (pet.BubbleOpacity > 0)
        {
            dc.PushOpacity(pet.BubbleOpacity);
            var b = ScreenCoordinates.Local(p.BubbleRect, height);
            dc.DrawRoundedRectangle(bubble, new Pen(new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)), 1), b, 20, 20);
            foreach (var circle in ThoughtTrail.Circles(p.BubbleRect, PetLayout.TrailEnd(p.SpriteRect, p.BubbleBelow), p.BubbleBelow))
            {
                var r = ScreenCoordinates.Local(circle, height); dc.DrawEllipse(bubble, null, new(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
            }
            var state = pet.Status.State;
            var color = state == WorkState.Waiting ? Brushes.DarkOrange : state == WorkState.Failed ? Brushes.IndianRed : state == WorkState.Running ? Brushes.ForestGreen : Brushes.Teal;
            dc.PushOpacity(pet.DotOpacity);
            dc.DrawEllipse(color, null, new(b.X + 19, b.Y + 18), 3, 3); dc.Pop();
            var text = pet.Text;
            DrawText(dc, text.Headline, b.X + 30, b.Y + 10, 284, 13, Brushes.Black, true);
            DrawText(dc, text.Subtitle, b.X + 16, b.Y + 30, 298, 11, Brushes.DimGray, false);
            dc.Pop();
        }
        dc.DrawImage(atlas.Image(pet.Frame), ScreenCoordinates.Local(atlas.DrawingRect(pet.Frame, p.SpriteRect), height));
        if (pet.Frame.Row == PetAnimationTimeline.DozeRow)
        {
            var cell = ScreenCoordinates.Local(p.SpriteRect, height); var unit = cell.Width / 192;
            foreach (var square in PetAnimationTimeline.DozeZs(pet.Frame.Column))
            {
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(new(cell.X + square.MinX * unit, cell.Y + square.MinY * unit), false, false);
                    path.LineTo(new(cell.X + square.MaxX * unit, cell.Y + square.MinY * unit), true, false);
                    path.LineTo(new(cell.X + square.MinX * unit, cell.Y + square.MaxY * unit), true, false);
                    path.LineTo(new(cell.X + square.MaxX * unit, cell.Y + square.MaxY * unit), true, false);
                }
                foreach (var (width, brush) in new[] { (4.0, Brushes.Black), (1.9, Brushes.White) })
                    dc.DrawGeometry(null, new Pen(brush, width * unit) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
            }
        }
    }
    private void DrawText(DrawingContext dc, string value, double x, double y, double width, double size, Brush color, bool bold)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(text, new(x, y));
    }
}
