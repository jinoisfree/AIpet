using AIpet.Core;
using static AIpet.Core.PetAnimationTimeline;
namespace AIpet.Core.Tests;
public sealed class PetAnimationTests : TestSupport
{
    [Fact]
    public void DirectionsTimingAndDozing()
    {
        var origin = new Point(-1200, 200);
        for (var index = 0; index < 16; index++)
        {
            var angle = index * Math.PI / 8; var target = new Point(origin.X + Math.Sin(angle) * 100, origin.Y + Math.Cos(angle) * 100);
            Check(LookFrame(origin, target) == new PetAnimationFrame(9 + index / 8, index % 8), "16방향 및 다중 모니터 좌표");
        }
        Check(LookFrame(origin, origin) is null);
        Check(LookFrame(default, new(double.NaN, 0)) is null);
        var up = new PetAnimationFrame(9, 0); var next = new PetAnimationFrame(9, 1);
        Point Direction(double degrees) => new(Math.Sin(degrees * Math.PI / 180) * 100, Math.Cos(degrees * Math.PI / 180) * 100);
        Check(LookFrame(default, Direction(12), up) == up, "direction boundary noise does not flicker");
        Check(LookFrame(default, Direction(14), up) == next, "intentional direction change still follows cursor");
        Check(LookFrame(default, Direction(11), next) == next, "reverse boundary has the same margin");
        Check(LookFrame(default, Direction(348), up) == up, "angular margin wraps across zero");
        Check(LookFrame(default, default, next) == next, "center dead zone retains last look instead of popping to idle");
        Check(DragAnimation(-3) == PetAnimation.Left);
        Check(DragAnimation(3) == PetAnimation.Right);
        Check(DragAnimation(0, PetAnimation.Left) == PetAnimation.Left);
        Check(Frame(PetAnimation.Running, 0.13).Column == 1);
        Check(Frame(PetAnimation.Running, 0.7).Column == 5);
        Check(Frame(PetAnimation.Running, 2.47).Row == 0, "세 번 재생 후 대기로 복귀");
        Check(Frame(PetAnimation.Left, 4, loop: true).Row == 2, "드래그 중 방향 유지");
        Check(Frame(PetAnimation.Idle, 1.67).Column == 0);
        Check(Frame(PetAnimation.Idle, 1.69).Column == 1);
        foreach (var animation in Enum.GetValues<PetAnimation>())
        {
            Check(Frame(animation, 30, reducedMotion: true) == new PetAnimationFrame((int)animation, 0), "동작 줄이기 준수");
            for (var i = 0; i < 600; i++)
            {
                var frame = Frame(animation, i * 0.05);
                Check(frame.Row is >= 0 and < 9 && frame.Column is >= 0 and < 8, "유효한 스프라이트 셀");
            }
        }
        Check(!PetAnimation.Waiting.AllowsLook() && !PetAnimation.Failed.AllowsLook());
        var doze = new PetDoze();
        Check(doze.Update(10, true) is null && doze.Update(189.9, true) is null, "3분이 되기 전에는 졸지 않는다");
        Check(doze.Update(190.5, true) == 0.5, "조용한 3분 뒤에 졸기 시작한다");
        Check(doze.Update(200, false) is null, "무슨 일이 생기면 깬다");
        Check(doze.Update(201, true) is null && doze.Update(380, true) is null, "깬 뒤에는 3분을 다시 센다");
        Check(doze.Update(381, true) == 0);
        Check(doze.Update(500, false) is null && doze.Update(501, true, after: 1) is null);
        Check(doze.Update(503, true, after: 1) == 1, "확인용으로 기다림을 줄일 수 있다");
        var steps = DozeSteps;
        Check(steps.Select(s => s.Image).SequenceEqual(new[] { 0, 1, 2, 3, 4, 3, 2, 3, 4, 3, 2, 3, 4, 3, 1 }), "눈 뜸, 한쪽 감김, 감은 채 세 번 까딱, 번쩍");
        Check(steps.Select(s => s.Zs).SequenceEqual(new[] { 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 0 }), "까딱일 때마다 z가 하나씩 는다");
        Check(Math.Abs(DozeCycle - 5.82) < 0.0001);
        var columns = new[] { 0, 0.89, 0.91, 1.36, 1.96, 2.16, 2.60, 5.66, 5.68, 5.83 }.Select(t => DozeFrame(t).Column);
        Check(columns.SequenceEqual(new[] { 0, 0, 1, 2, 3, 4, 5, 13, 14, 0 }), "순서대로 돌고 처음으로 돌아온다");
        Check(DozeFrame(100).Row == DozeRow);
        Check(DozeFrame(0.3, reducedMotion: true) == new PetAnimationFrame(11, 10), "동작 줄이기에서는 잠든 한 장면");
        Check(DozeZs(10).Length == 3);
        for (var column = 0; column < steps.Count; column++)
        {
            var zs = DozeZs(column);
            Check(zs.All(r => new Rect(2, 2, 188, 204).Contains(r)), "z는 펫의 칸 안에 그린다");
            Check(zs.Zip(zs.Skip(1)).All(p => p.Second.Width > p.First.Width && p.Second.MinY < p.First.MinY && p.Second.MinX > p.First.MaxX), "위로 갈수록 커진다");
        }
        Check(DozeZs(99).Length == 0);
    }
}
