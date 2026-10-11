using AIpet.Core;
namespace AIpet.Core.Tests;
public sealed class PetLayoutTests : TestSupport
{
    [Fact]
    public void ScalingDisplayEdgesThoughtTrailAndMotion()
    {
        var small = new PetLayout(0.5); var large = new PetLayout(2);
        Check(small.BubbleRect.Size == large.BubbleRect.Size, "캐릭터 배율이 말풍선에 영향을 주면 안 됩니다.");
        Check(small.BubbleRect.Size == new Size(330, 56), "말풍선 기준 크기가 유지되어야 합니다.");
        Check(large.SpriteRect.Width == small.SpriteRect.Width * 4, "캐릭터는 요청한 배율로 바뀌어야 합니다.");
        Check(new PetLayout(0.25).SpriteSize == new Size(48, 52));
        foreach (var scale in new[] { 0.25, 0.35, 0.5, 0.8, 1, 1.35, 2 })
        {
            var layout = new PetLayout(scale); var window = new Rect(default, layout.WindowSize);
            Check(window.Contains(layout.BubbleRect) && window.Contains(layout.SpriteRect), "크기 변경 후 내용이 잘리면 안 됩니다.");
            Check(!layout.BubbleRect.Intersects(layout.SpriteRect), "말풍선과 캐릭터가 겹치면 안 됩니다.");
            var head = PetLayout.TrailEnd(layout.SpriteRect, false);
            Check(layout.BubbleRect.MinY - head.Y >= ThoughtTrail.Length - 0.001, "말풍선과 머리 사이에 생각 방울이 들어갈 자리");
            Check(Math.Abs(head.X - layout.BubbleRect.MidX) <= 17 * scale + 0.001 && head.Y < layout.SpriteRect.MaxY);
        }
        foreach (var screen in new[] { new Rect(0, 30, 1440, 870), new Rect(-1920, -250, 1920, 1050), new Rect(0, 0, 800, 520) })
        foreach (var scale in new[] { 0.25, 0.5, 1, 2 })
        foreach (var x in new[] { screen.MinX - 1000, screen.MidX, screen.MaxX + 100 })
        foreach (var y in new[] { screen.MinY - 1000, screen.MidY, screen.MaxY + 100 })
        {
            var placement = new PetPlacement(new Point(x, y), scale, screen);
            var globalSprite = placement.SpriteRect.Offset(placement.WindowFrame.MinX, placement.WindowFrame.MinY);
            var globalBubble = placement.BubbleRect.Offset(placement.WindowFrame.MinX, placement.WindowFrame.MinY);
            var visible = PetLayout.VisibleSprite(globalSprite);
            Check(screen.Inset(4, 4).Inset(-0.001, -0.001).Contains(visible), "보이는 그림은 화면 안 4pt");
            Check(screen.Inset(12, 12).Contains(globalBubble), "말풍선은 화면 안 12pt");
            if (x < screen.MinX) Check(Math.Abs(visible.MinX - screen.MinX - 4) < 0.001);
            if (x > screen.MaxX) Check(Math.Abs(visible.MaxX - screen.MaxX + 4) < 0.001);
            if (y < screen.MinY) Check(Math.Abs(visible.MinY - screen.MinY - 4) < 0.001);
            if (y > screen.MaxY) Check(Math.Abs(visible.MaxY - screen.MaxY + 4) < 0.001);
            var restored = new PetPlacement(globalSprite.Origin, scale, screen);
            Check(restored.GlobalSprite == globalSprite, "저장 위치를 복원해도 그림이 움직이지 않음");
            Check(!globalSprite.Intersects(globalBubble), "말풍선이 펫을 가리지 않음");
            Check(globalBubble.Size == new Size(330, 56), "가장자리에서도 말풍선 고정 크기");
            var below = placement.BubbleBelow; var bubble = placement.BubbleRect; var end = PetLayout.TrailEnd(placement.SpriteRect, below);
            var circles = ThoughtTrail.Circles(bubble, end, below); var window = new Rect(default, placement.WindowFrame.Size);
            Check(circles.Select(r => r.Width).SequenceEqual(new[] { 15, 10.5, 6.5 }) && circles.All(r => r.Width == r.Height), "큰 방울부터 작은 방울까지 셋");
            Check(circles.All(r => window.Contains(r) && !r.Intersects(bubble)), "방울은 창 안, 말풍선 밖");
            Check(circles.All(r => below ? r.MinY > bubble.MaxY && r.MaxY <= end.Y + 0.001 : r.MaxY < bubble.MinY && r.MinY >= end.Y - 0.001), "방울은 말풍선과 펫 사이에만");
            Check(circles.Zip(circles.Skip(1)).All(p => below ? p.Second.MinY > p.First.MaxY : p.Second.MaxY < p.First.MinY), "서로 겹치지 않고 펫 쪽으로 이어짐");
            Check(circles[0].MinX >= bubble.MinX + 14 && circles[0].MaxX <= bubble.MaxX - 14, "첫 방울은 말풍선의 둥근 모서리 안쪽");
            Check(Math.Abs(circles[2].MidX - end.X) <= Math.Abs(circles[0].MidX - end.X) + 0.001, "펫 쪽으로 다가감");
        }
        foreach (var (screen, full) in new[] {
            (new Rect(0, 0, 1512, 944), new Rect(0, 0, 1512, 982)),
            (new Rect(-1920, -250, 1920, 1026), new Rect(-1920, -250, 1920, 1080)),
            (new Rect(0, 0, 1440, 900), new Rect(0, 0, 1440, 900)) })
        foreach (var scale in new[] { 0.25, 0.5, 1, 2 })
        foreach (var x in new[] { screen.MinX - 1000, screen.MidX, screen.MaxX + 1000 })
        foreach (var y in new[] { screen.MinY - 1000, full.MaxY + 1000 })
        {
            var placement = new PetPlacement(new Point(x, y), scale, screen, full);
            var visible = PetLayout.VisibleSprite(placement.GlobalSprite); var bubble = placement.GlobalBubble;
            Check(visible.MinX >= screen.MinX + 4 && visible.MaxX <= screen.MaxX - 4 && visible.MinY >= screen.MinY + 4);
            Check(visible.MaxY <= full.MaxY + 0.001, "위쪽은 전체 화면 안");
            if (x < screen.MinX) Check(Math.Abs(visible.MinX - screen.MinX - 4) < 0.001);
            if (x > screen.MaxX) Check(Math.Abs(visible.MaxX - screen.MaxX + 4) < 0.001);
            if (y < screen.MinY) Check(Math.Abs(visible.MinY - screen.MinY - 4) < 0.001);
            else
            {
                Check(Math.Abs(visible.MaxY - full.MaxY) < 0.001 && placement.BubbleBelow, "화면 맨 위에서는 여백 없이, 말풍선은 아래");
                var legacy = new PetPlacement(new Point(x, y), scale, screen);
                Check(Math.Abs(PetLayout.VisibleSprite(legacy.GlobalSprite).MaxY - screen.MaxY + 4) < 0.001, "전체 화면 미지정 시 기존 위쪽 4pt");
            }
            Check(screen.Inset(12, 12).Contains(bubble) && !visible.Intersects(bubble), "말풍선은 메뉴 막대 아래 12pt, 그림과 겹치지 않음");
            var below = placement.BubbleBelow; var end = PetLayout.TrailEnd(placement.GlobalSprite, below); var circles = ThoughtTrail.Circles(bubble, end, below);
            Check(circles.All(r => placement.WindowFrame.Contains(r) && !r.Intersects(bubble) && !r.Intersects(visible)), "방울은 창 안, 말풍선과 그림 밖");
            Check(circles.All(r => below ? r.MinY > bubble.MaxY && r.MaxY <= end.Y + 0.001 : r.MaxY < bubble.MinY && r.MinY >= end.Y - 0.001), "방울은 말풍선과 펫 사이");
            Check(circles.Zip(circles.Skip(1)).All(p => !p.First.Intersects(p.Second)), "방울끼리 겹치지 않음");
            var restored = new PetPlacement(placement.GlobalSprite.Origin, scale, screen, full);
            Check(restored.GlobalSprite == placement.GlobalSprite && restored.GlobalBubble == bubble, "맨 위에서도 저장 위치 복원 불변");
        }
        var left = new PetPlacement(default(Point), 0.25, new(0, 0, 1000, 800));
        Check(left.GlobalSprite.MinX == 0 && left.GlobalBubble.MinX == 12, "그림은 허용된 위치를 유지하고 말풍선만 화면 안으로 이동");
        var top = new PetPlacement(new Point(988, 790), 0.25, new(0, 0, 1000, 800));
        Check(top.BubbleBelow && PetLayout.VisibleSprite(top.GlobalSprite).MaxX == 996 && top.GlobalBubble.MaxX == 988, "우측 그림 4pt, 아래 말풍선 12pt");
        var motionScreen = new Rect(0, 0, 1000, 800); var motion = new PetBubbleMotion();
        motion.Retarget(new(12, 12), motionScreen, 0); motion.Retarget(new(658, 732), motionScreen, 0);
        var first = motion.Step(1.0 / 30);
        Check(first.X > 12 && first.X < 658 && first.Y > 12 && first.Y < 732, "순간 이동하지 않고 양 축에서 보간");
        var previous = first;
        for (var frame = 2; frame <= 60; frame++)
        {
            var next = motion.Step((double)frame / 30);
            Check(next.X >= previous.X && next.Y >= previous.Y, "과도한 튕김 없이 부드럽게 접근");
            Check(motionScreen.Contains(new Rect(next, PetLayout.BubbleSize)), "전환 도중 화면 안에 유지");
            previous = next;
        }
        Check(previous == new Point(658, 732), "목표 위치에 도착");
        motion.Retarget(new(-2000, -2000), motionScreen, 2); var reverse = motion.Step(2.03);
        Check(reverse.X < previous.X && reverse.X > 12 && reverse.Y < previous.Y && reverse.Y > 12, "반대 방향 전환도 보간");
        motion.Retarget(new(100, 100), motionScreen, 3, immediately: true);
        Check(motion.Step(3) == new Point(100, 100), "동작 줄이기에서는 즉시 배치");
    }
}
