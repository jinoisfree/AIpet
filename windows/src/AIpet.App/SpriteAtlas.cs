using System.Windows.Media.Imaging;
namespace AIpet.App;
public sealed class SpriteAtlas
{
    private readonly List<BitmapSource[]> frames = [];
    private readonly List<PetSpritePresentation[]> presentations = [];
    private readonly List<byte[][]> pixels = [];
    private readonly Rect interactionBounds;
    public static readonly int[] Counts = [7, 8, 8, 4, 5, 8, 8, 6, 6, 8, 8, PetAnimationTimeline.DozeSteps.Count];
    private static BitmapSource Load(string path, int width, int height)
    {
        var image = new BitmapImage(new Uri("pack://application:,,,/AIpet;component/" + path)); image.Freeze();
        if (image.PixelWidth != width || image.PixelHeight != height) throw new InvalidDataException("펫 이미지 파일을 읽을 수 없습니다.");
        return image;
    }
    public SpriteAtlas()
    {
        var atlas = Load("Assets/Generated/spritesheet.png", 1536, 2288);
        var wave = Load("Assets/wave.png", 768, 208); var asleep = Load("Assets/doze.png", 768, 208);
        var approval = Load("Assets/approval-phone.png", 1536, 208);
        var metrics = new List<SpriteContentMetrics[]>();
        BitmapSource Crop(BitmapSource source, int x, int y) { var result = new CroppedBitmap(source, new Int32Rect(x, y, 192, 208)); result.Freeze(); return result; }
        byte[] Rgba(BitmapSource image)
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); var data = new byte[192 * 208 * 4]; converted.CopyPixels(data, 192 * 4, 0);
            for (var i = 0; i < data.Length; i += 4) (data[i], data[i + 2]) = (data[i + 2], data[i]);
            return data;
        }
        for (var row = 0; row < 11; row++)
        {
            var cells = Enumerable.Range(0, Counts[row]).Select(c => Crop(row == 6 ? approval : row == 3 ? wave : atlas, c * 192, row is 3 or 6 ? 0 : row * 208)).ToArray();
            frames.Add(cells); var rgba = cells.Select(Rgba).ToArray(); pixels.Add(rgba);
            metrics.Add(rgba.Select(p => SpriteContentMetrics.Measure(p, 192, 208) ?? throw new InvalidDataException("펫 동작 이미지를 읽을 수 없습니다.")).ToArray());
        }
        var drawings = new[] { frames[0][0] }.Concat(Enumerable.Range(0, 4).Select(c => Crop(asleep, c * 192, 0))).ToArray();
        frames.Add(PetAnimationTimeline.DozeSteps.Select(s => drawings[s.Image]).ToArray()); pixels.Add(frames[11].Select(Rgba).ToArray());
        metrics.Add(Enumerable.Repeat(metrics[0][0], Counts[11]).ToArray());
        var reference = metrics[10][7]; interactionBounds = reference.Bounds;
        var idleScale = PetSpritePresentation.Matching(metrics[0][0], reference).Scale;
        for (var row = 0; row < metrics.Count; row++)
            presentations.Add(metrics[row].Select(m => row == 6 ? new PetSpritePresentation(1, default) : PetSpritePresentation.Matching(row is 9 or 10 ? m : metrics[row][0], reference, row == 3 ? idleScale : null)).ToArray());
    }
    public BitmapSource Image(PetAnimationFrame frame) => frames[frame.Row][frame.Column % Counts[frame.Row]];
    public Rect DrawingRect(PetAnimationFrame frame, Rect cell) => presentations[frame.Row][frame.Column % Counts[frame.Row]].DrawingRect(cell);
    public Rect InteractionRect(Rect cell) => PetSpritePresentation.InteractionRect(interactionBounds, cell);
    public bool Opaque(Point point, PetAnimationFrame frame, Rect cell)
    {
        var rect = DrawingRect(frame, cell); if (!rect.Contains(point)) return false;
        int x = Math.Clamp((int)((point.X - rect.X) / rect.Width * 192), 0, 191), y = Math.Clamp((int)((rect.MaxY - point.Y) / rect.Height * 208), 0, 207);
        return pixels[frame.Row][frame.Column % Counts[frame.Row]][(y * 192 + x) * 4 + 3] >= 64;
    }
}
