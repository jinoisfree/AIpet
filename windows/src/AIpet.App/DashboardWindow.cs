namespace AIpet.App;
public sealed class DashboardWindow : Window
{
    private readonly PetApplication model;
    private readonly TextBlock introduction = Ui.Text("", 13), warning = Ui.Text("", 11), scan = Ui.Text("", 10);
    private readonly Button pause;
    private readonly System.Windows.Controls.Primitives.UniformGrid metrics = new() { Columns = 3 };
    private readonly StackPanel tasks = new(), connections = new();
    private readonly ComboBox filter = new() { Width = 260, ItemsSource = new[] { "전체", "Codex", "Claude Code" }, SelectedIndex = 0 };
    public DashboardWindow(PetApplication model)
    {
        this.model = model; Width = 710; Height = 610; MinWidth = 680; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Ui.Background;
        var root = new DockPanel { Margin = new Thickness(26) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var header = new DockPanel(); pause = Ui.Button("잠시 쉬기", model.TogglePause); DockPanel.SetDock(pause, Dock.Right); header.Children.Add(pause);
        var heading = new StackPanel(); heading.Children.Add(Ui.Text("AIpet", 29, true)); heading.Children.Add(introduction); header.Children.Add(heading); top.Children.Add(header);
        metrics.Margin = new Thickness(0, 20, 0, 20); top.Children.Add(metrics);
        var toolbar = new DockPanel(); DockPanel.SetDock(filter, Dock.Right); toolbar.Children.Add(filter); toolbar.Children.Add(Ui.Text("작업 목록", 16, true)); top.Children.Add(toolbar);
        filter.SelectionChanged += (_, _) => Refresh(); warning.Foreground = Brushes.DarkOrange; top.Children.Add(warning);
        var bottom = new StackPanel { Margin = new Thickness(0, 15, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        bottom.Children.Add(connections);
        var refresh = new DockPanel(); var refreshButton = Ui.Button("새로고침", () => _ = model.Refresh(true)); DockPanel.SetDock(refreshButton, Dock.Right); refresh.Children.Add(refreshButton);
        refresh.Children.Add(Ui.Text("이 PC의 최근 기록 · 응답 종료는 작업 성공을 뜻하지 않습니다.", 10)); bottom.Children.Add(refresh);
        var preview = new DockPanel(); DockPanel.SetDock(scan, Dock.Right); preview.Children.Add(scan); preview.Children.Add(Ui.Button("동작 미리보기", model.Preview)); bottom.Children.Add(preview);
        root.Children.Add(new ScrollViewer { Content = tasks, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Refresh();
    }
    public void Refresh()
    {
        Title = model.Identity.DashboardTitle; introduction.Text = model.Identity.DashboardIntroduction;
        pause.Content = model.Paused ? "다시 살펴보기" : "잠시 쉬기";
        metrics.Children.Clear(); var snapshot = model.Snapshot;
        foreach (var (title, state) in new[] { ("작업 중", WorkState.Running), ("확인 필요", WorkState.Waiting), ("최근 응답", WorkState.Responded) })
        {
            var panel = new StackPanel(); panel.Children.Add(Ui.Text(title, 11));
            var count = Ui.Text(snapshot.Tasks.Count(t => t.State == state).ToString(), 27, true); count.Foreground = Ui.StateColor(state); panel.Children.Add(count);
            metrics.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(14), Padding = new Thickness(15), Margin = new Thickness(0, 0, 10, 0), Child = panel });
        }
        warning.Text = snapshot.Warning ?? ""; warning.Visibility = snapshot.Warning is null ? Visibility.Collapsed : Visibility.Visible;
        tasks.Children.Clear(); var filtered = snapshot.Tasks.Where(t => filter.SelectedItem as string == "전체" || t.Provider.Name() == filter.SelectedItem as string).ToArray();
        if (filtered.Length == 0)
        {
            tasks.Children.Add(Ui.Text(model.Paused ? "지금은 쉬고 있어요" : "아직 표시할 작업이 없어요", 15, true));
            tasks.Children.Add(Ui.Text("Codex나 Claude Code에서 작업을 시작하면 여기에 나타납니다.", 12));
        }
        foreach (var item in filtered)
        {
            var row = new DockPanel();
            var status = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(status, Dock.Right);
            var label = Ui.Text(item.State.Label(), 12); label.Foreground = Ui.StateColor(item.State); status.Children.Add(label); status.Children.Add(Ui.Text(item.UpdatedAt.ToLocalTime().ToString("T"), 10)); row.Children.Add(status);
            var stripe = new Border { Width = 4, Height = 42, Background = Ui.StateColor(item.State), Margin = new Thickness(0, 0, 12, 0), CornerRadius = new CornerRadius(3) }; DockPanel.SetDock(stripe, Dock.Left); row.Children.Add(stripe);
            var detail = new StackPanel(); detail.Children.Add(Ui.Text($"{item.Provider.Name()}   {(item.Project.Length == 0 ? "작업 공간" : item.Project)}{(model.SelectedID == item.Id ? "  📌" : "")}", 12, true));
            detail.Children.Add(Ui.Text($"{item.ShortID} · {item.Source}", 10)); row.Children.Add(detail);
            var button = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12), Margin = new Thickness(0, 4, 0, 4), Background = Brushes.White,
                BorderBrush = model.SelectedID == item.Id ? Brushes.Teal : Brushes.WhiteSmoke, BorderThickness = new Thickness(1) };
            button.Click += (_, _) => model.Select(model.SelectedID == item.Id ? null : item.Id); tasks.Children.Add(button);
        }
        connections.Children.Clear();
        foreach (var connection in snapshot.Connections)
        {
            var text = Ui.Text($"● {connection.Provider.Name()}  {connection.Detail}", 10); text.Foreground = connection.Available ? Brushes.Teal : Brushes.Gray; connections.Children.Add(text);
        }
        scan.Text = snapshot.ObservedAt.ToLocalTime().ToString("T");
    }
}
internal static class Ui
{
    public static Brush Background { get; } = new SolidColorBrush(Color.FromRgb(242, 245, 237));
    public static TextBlock Text(string value, double size, bool bold = false) => new() { Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 3, 0, 3), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    public static Button Button(string label, Action action) { var button = new Button { Content = label, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left }; button.Click += (_, _) => action(); return button; }
    public static Brush StateColor(WorkState state) => state switch { WorkState.Running => Brushes.Teal, WorkState.Waiting => Brushes.DarkOrange, WorkState.Failed => Brushes.IndianRed, WorkState.Responded => Brushes.SteelBlue, _ => Brushes.Gray };
}
