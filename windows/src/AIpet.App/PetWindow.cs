using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
namespace AIpet.App;

public sealed class PetWindow : Window
{
    private readonly SpriteAtlas atlas;
    private readonly PetSurface surface;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly BubbleReveal reveal = new();
    private readonly PetPointerInteraction pointer = new();
    private PetDoze doze = new();
    private readonly PetBubbleMotion bubbleMotion = new();
    private PetPlacement? target;
    public PetPlacement? Placement { get; private set; }
    public ScreenCoordinates Screen { get; private set; } = ScreenCoordinates.Primary;
    public PetAnimationFrame Frame { get; private set; }
    public PetStatus Status { get; private set; } = new(WorkState.Idle, "AIpet", "AI 작업을 살펴보고 있어요", null, "AIpet");
    public PetIdentity Identity { get; set; } = new(PetIdentity.DefaultName);
    public double PetScale { get; private set; } = 1;
    public bool AlwaysShown { get; set; }
    public bool IsDozing { get; private set; }
    public bool BubbleShown { get; private set; }
    public double BubbleOpacity { get; private set; }
    public bool Paused { get; private set; }
    public bool Reduced => !SystemParameters.ClientAreaAnimation;
    public double Now => clock.Elapsed.TotalSeconds;
    public event Action? OpenDashboard, OpenMenu, Moved, DozeChanged;
    private System.Drawing.Point? dragStart, anchorStart;
    private System.Drawing.Point previousDrag;
    private bool dragging, hovering, wasLooking;
    private PetAnimation dragAnimation = PetAnimation.Right, animation = PetAnimation.Idle;
    private double animationStarted, lastTick, previewStarted;
    private bool preview, previewLook;
    private int? previewRow;
    private PetRenderState? renderedState;
    public double DotOpacity { get; private set; } = 1;
    public PetWindow(SpriteAtlas atlas)
    {
        this.atlas = atlas;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        Width = 354; Height = 320;
        surface = new PetSurface(this, atlas); Content = surface;
        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x80 | 0x08000000));
            HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
        };
        surface.MouseLeftButtonDown += (_, e) => {
            dragStart = ScreenCoordinates.CursorPixels;
            anchorStart = target is null ? null : Screen.ToPixels(target.GlobalSprite.Origin);
            previousDrag = dragStart.Value; dragging = false;
            surface.CaptureMouse(); e.Handled = true;
        };
        surface.MouseMove += (_, _) => {
            if (dragStart is not { } start || anchorStart is not { } anchor) return;
            var mouse = ScreenCoordinates.CursorPixels; var dx = mouse.X - start.X; var dy = mouse.Y - start.Y;
            if (Math.Sqrt((double)dx * dx + (double)dy * dy) / Screen.Scale > 4) dragging = true;
            if (!dragging) return;
            dragAnimation = PetAnimationTimeline.DragAnimation(mouse.X - previousDrag.X, dragAnimation); previousDrag = mouse;
            Place(Screen.FromPixels(new(anchor.X + dx, anchor.Y + dy)));
        };
        surface.MouseLeftButtonUp += (_, e) => {
            if (dragStart is null) return;
            var moved = dragging; dragStart = anchorStart = null; dragging = false; surface.ReleaseMouseCapture();
            if (moved) { Constrain(); Moved?.Invoke(); } else OpenDashboard?.Invoke();
            e.Handled = true;
        };
        surface.LostMouseCapture += (_, _) => { dragging = false; dragStart = anchorStart = null; };
        surface.MouseRightButtonDown += (_, e) => { OpenMenu?.Invoke(); e.Handled = true; };
        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1.0 / 60) };
        timer.Tick += (_, _) => Tick(); timer.Start(); Closed += (_, _) => timer.Stop();
    }
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        if (message is 0x007E or 0x02E0) Dispatcher.BeginInvoke(new Action(Constrain)); // display/DPI change
        return IntPtr.Zero;
    }
    public void SetStatus(PetStatus status)
    {
        if (Status.AlertKey != status.AlertKey && status.AlertKey is not null) reveal.Alert(Now);
        Status = status; ToolTip = $"{Identity.Name} · 클릭: 작업 목록 · 드래그: 이동 · 오른쪽 클릭: 메뉴";
        System.Windows.Automation.AutomationProperties.SetName(surface, $"AIpet · {Identity.Name} · {status.Headline} · 작업 목록 열기");
    }
    public void SetPaused(bool paused)
    {
        if (paused == Paused) return;
        Paused = paused; pointer.Reset(); doze = new(); IsDozing = false; animationStarted = Now;
    }
    public void Restore(PetSettings settings)
    {
        PetScale = settings.PetScale / 100; AlwaysShown = settings.BubbleAlwaysShown;
        if (settings.Position is { } saved)
        {
            var pixels = new System.Drawing.Point((int)Math.Round(saved.X), (int)Math.Round(saved.Y));
            Screen = ScreenCoordinates.Nearest(pixels); Place(Screen.FromPixels(pixels));
        }
        else ResetPosition();
    }
    public Point? SavedPosition => target is null ? null : ToStored(Screen.ToPixels(target.GlobalSprite.Origin));
    private static Point ToStored(System.Drawing.Point pixels) => new(pixels.X, pixels.Y);
    public void ResetPosition()
    {
        Screen = ScreenCoordinates.Primary;
        Place(new(Screen.WorkArea.MaxX - new PetLayout(PetScale).SpriteSize.Width - 30, Screen.WorkArea.MinY + 25));
        Moved?.Invoke();
    }
    public void ResizePet(double percent)
    {
        var visible = target is null ? default : PetLayout.VisibleSprite(target.GlobalSprite);
        PetScale = Math.Clamp(Math.Round(percent), 25, 200) / 100;
        var next = PetLayout.VisibleSprite(new(default(Point), new PetLayout(PetScale).SpriteSize));
        Place(new(visible.MidX - next.MidX, visible.MinY - next.MinY)); Moved?.Invoke();
    }
    public void Constrain() { if (target is not null) Place(target.GlobalSprite.Origin); }
    private void Place(Point origin, bool reselect = true)
    {
        if (reselect)
        {
            var visible = PetLayout.VisibleSprite(new(origin, new PetLayout(PetScale).SpriteSize));
            var center = Screen.ToPixels(new(visible.MidX, visible.MidY)); var pixels = Screen.ToPixels(origin);
            Screen = ScreenCoordinates.Nearest(center); origin = Screen.FromPixels(pixels);
        }
        // Windows constrains every edge to the work area, including a top taskbar.
        target = new(origin, PetScale, Screen.WorkArea);
        bubbleMotion.Retarget(target.GlobalBubble.Origin, Screen.WorkArea, Now, Reduced);
        AdvancePlacement();
    }
    private void AdvancePlacement()
    {
        if (target is null) return;
        var placement = new PetPlacement(target.GlobalSprite, new Rect(bubbleMotion.Step(Now), PetLayout.BubbleSize));
        if (placement == Placement) return;
        Placement = placement;
        surface.Width = placement.WindowFrame.Width; surface.Height = placement.WindowFrame.Height;
        Screen.Place(this, placement.WindowFrame);
    }
    public void Preview(bool look)
    {
        preview = true; previewLook = look; previewStarted = Now; previewRow = null; animationStarted = Now;
        if (!IsVisible) Show();
    }
    public (string Headline, string Subtitle) Text
    {
        get
        {
            if (!preview) return (Status.Headline, Status.Subtitle);
            if (previewLook) return ("미리보기 · 마우스 바라보기", $"{Identity.Name} · 포인터를 움직여 보세요");
            string[] names = ["쉬는 중", "오른쪽 이동", "왼쪽 이동", "손 흔들기", "점프", "오류", "확인 대기", "작업 중", "검토", "방향 보기 1", "방향 보기 2", "조는 중"];
            return ($"미리보기 · {names[previewRow ?? 0]}", $"{Identity.Name}의 동작 · 실제 작업과 별개");
        }
    }
    private void Tick()
    {
        var now = Now; var dt = Math.Clamp(now - lastTick, 0, 0.1); lastTick = now;
        if (!IsVisible || Placement is null) { pointer.Reset(); return; }
        AdvancePlacement(); var p = Placement!; var interaction = atlas.InteractionRect(p.GlobalSprite); var mouse = Screen.Cursor;
        hovering = interaction.Contains(mouse) || hovering && BubbleShown && interaction.Union(p.GlobalBubble).Contains(mouse);
        BubbleShown = reveal.Update(now, preview || dragging || hovering) || AlwaysShown;
        BubbleOpacity = Reduced ? (BubbleShown ? 1 : 0) : Math.Clamp(BubbleOpacity + (BubbleShown ? 1 : -1) * dt / 0.18, 0, 1);
        if (preview)
        {
            var elapsed = now - previewStarted;
            if (elapsed >= (previewLook ? 15 : 22 + PetAnimationTimeline.DozeCycle)) { preview = previewLook = false; previewRow = null; }
            else if (!previewLook)
            {
                var row = Math.Min(11, (int)(elapsed / 2));
                if (row != previewRow) { previewRow = row; animationStarted = now; }
            }
        }
        if (!Paused)
        {
            var phase = pointer.Update(mouse, interaction, Screen.Frame, now, !dragging && !preview, Reduced);
            var next = previewRow is >= 0 and < 9 ? (PetAnimation)previewRow : dragging ? dragAnimation : PetAnimations.FromState(Status.State);
            if (next != animation) { animation = next; animationStarted = now; }
            var frame = PetAnimationTimeline.Frame(animation, now - animationStarted, Reduced, dragging || preview);
            var looking = false;
            if (previewRow == 11) frame = PetAnimationTimeline.DozeFrame(now - animationStarted, Reduced);
            else if (previewRow is >= 9 and <= 10) frame = new(previewRow.Value, (int)((now - animationStarted) / 0.25) % 8);
            else if (previewRow is null && !dragging)
            {
                if (phase.Kind == PetPointerInteraction.PhaseKind.Jumping) frame = PetAnimationTimeline.Frame(PetAnimation.Jumping, phase.Elapsed);
                else if ((phase.Kind == PetPointerInteraction.PhaseKind.Looking || previewLook) && PetAnimationTimeline.LookFrame(new(interaction.MidX, interaction.MidY), mouse, wasLooking ? Frame : null) is { } look)
                { frame = look; looking = true; }
            }
            if (wasLooking && !looking) animationStarted = now; wasLooking = looking;
            var dozing = doze.Update(now, animation == PetAnimation.Idle && !preview && !dragging && !looking && phase.Kind == PetPointerInteraction.PhaseKind.Inactive);
            if (dozing is { } elapsed) frame = PetAnimationTimeline.DozeFrame(elapsed, Reduced);
            else if (IsDozing) animationStarted = now;
            if (IsDozing != (dozing is not null)) { IsDozing = dozing is not null; DozeChanged?.Invoke(); }
            Frame = frame;
        }
        var text = Text;
        var visual = new PetRenderState(Frame, Placement, BubbleOpacity, text.Headline, text.Subtitle,
            Status.State, Paused, Reduced, PetRenderState.GetPulseStep(now, BubbleOpacity, Status.State, Paused, Reduced));
        if (visual.NeedsRedraw(renderedState))
        {
            renderedState = visual; DotOpacity = visual.DotOpacity; surface.InvalidateVisual();
        }
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
