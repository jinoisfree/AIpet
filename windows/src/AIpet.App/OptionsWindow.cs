namespace AIpet.App;
public sealed class OptionsWindow : Window
{
    private readonly PetApplication model;
    private readonly StackPanel usage = new();
    public OptionsWindow(PetApplication model, PetSettings settings)
    {
        this.model = model;
        Title = "AIpet 옵션"; Width = 540; Height = Math.Min(570, SystemParameters.WorkArea.Height); MaxWidth = SystemParameters.WorkArea.Width; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Ui.Background;
        var root = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        root.Children.Add(Ui.Text("펫 이름", 14, true));
        var nameRow = new DockPanel(); var name = new TextBox { Text = settings.PetName, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "펫 이름을 입력하세요" };
        System.Windows.Automation.AutomationProperties.SetName(name, "펫 이름");
        var hint = Ui.Text("1~20자 · 저장하면 모든 화면에 적용됩니다.", 11);
        void SaveName()
        {
            if (PetIdentity.ValidationMessage(name.Text) is { } error) { hint.Text = error; hint.Foreground = Brushes.IndianRed; return; }
            if (model.SaveName(name.Text)) { name.Text = model.Identity.Name; hint.Text = "이름을 저장했어요. 모든 화면에 적용했습니다."; hint.Foreground = Brushes.DimGray; }
        }
        var save = Ui.Button("이름 저장", SaveName); DockPanel.SetDock(save, Dock.Right); nameRow.Children.Add(save); nameRow.Children.Add(name); root.Children.Add(nameRow); root.Children.Add(hint);
        name.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SaveName(); };
        var sizeRow = new DockPanel { Margin = new Thickness(0, 22, 0, 0) }; var value = Ui.Text($"{settings.PetScale:0}%", 13); DockPanel.SetDock(value, Dock.Right); sizeRow.Children.Add(value); sizeRow.Children.Add(Ui.Text("펫 크기", 14, true)); root.Children.Add(sizeRow);
        var slider = new Slider { Minimum = 25, Maximum = 200, Value = settings.PetScale, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 10, 0, 0), ToolTip = "캐릭터만 25%부터 200%까지 조절 · 말풍선 크기는 고정" };
        System.Windows.Automation.AutomationProperties.SetName(slider, "펫 크기");
        slider.ValueChanged += (_, _) => { value.Text = $"{slider.Value:0}%"; model.Resize(slider.Value); }; root.Children.Add(slider);
        var limits = new DockPanel(); var max = Ui.Text("200%", 11); DockPanel.SetDock(max, Dock.Right); limits.Children.Add(max); limits.Children.Add(Ui.Text("25%", 11)); root.Children.Add(limits);
        var bottom = new DockPanel(); var reset = Ui.Button("기본 크기", () => slider.Value = 100); DockPanel.SetDock(reset, Dock.Right); bottom.Children.Add(reset); bottom.Children.Add(Ui.Text("말풍선 크기는 그대로 유지됩니다.", 11)); root.Children.Add(bottom);
        var direct = new CheckBox { Content = "Claude 사용량 직접 조회", IsChecked = settings.ClaudeUsageDirectEnabled, Margin = new Thickness(0, 18, 0, 5) };
        direct.Click += (_, _) => model.SetDirectUsage(direct.IsChecked == true); root.Children.Add(direct);
        root.Children.Add(Ui.Wrapped("켜면 Claude Code 로그인 파일을 읽고 api.anthropic.com 에 사용량을 조회합니다", 11));
        root.Children.Add(usage); RefreshUsage();
    }
    public void RefreshUsage()
    {
        usage.Children.Clear(); var state = model.Usage; var now = DateTimeOffset.UtcNow;
        var status = state.DirectStatus.Label();
        if (state.DirectStatus == DirectUsageStatus.Success && state.DirectSuccess is { } date) status += $" · {date.ToLocalTime():g}";
        usage.Children.Add(Ui.Text($"마지막 조회: {status}", 11));
        usage.Children.Add(Ui.Text("추적 계정", 12, true));
        usage.Children.Add(Ui.PrivateText($"Codex {state.Codex?.Account.Label ?? "계정 확인 불가"}"));
        usage.Children.Add(Ui.PrivateText($"Claude Code {state.Claude(now)?.Account.Label ?? "계정 확인 불가"}"));
    }
}
