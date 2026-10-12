namespace AIpet.Core;

public sealed record PetStatus(WorkState State, string Headline, string Subtitle, string? AlertKey, string ToolTip);
public static class PetStatusPresenter
{
    public static PetStatus Present(Snapshot snapshot, PetIdentity identity, DateTimeOffset now,
        bool paused = false, string? selectedID = null, bool dozing = false, string idleSubtitle = "Codex · Claude Code")
    {
        var tasks = snapshot.Tasks; var active = tasks.Count(t => t.State == WorkState.Running);
        var tip = $"AIpet · {identity.Name} · 작업 중 {active}개";
        if (paused) return new(WorkState.Idle, $"{identity.Subject} 쉬고 있어요", "메뉴에서 다시 시작할 수 있어요", null, tip);
        var urgent = tasks.FirstOrDefault(t => t.State == WorkState.Waiting || t.State == WorkState.Failed && (now - t.UpdatedAt).TotalSeconds < 60);
        var selected = tasks.FirstOrDefault(t => t.Id == selectedID);
        var focused = tasks.FirstOrDefault(t => t.Id == snapshot.ScreenFocus?.TaskID && t.State == WorkState.Running);
        var eligible = tasks.FirstOrDefault(t => t.State == WorkState.Running || t.State == WorkState.Responded && (now - t.UpdatedAt).TotalSeconds < 20);
        var alert = urgent is null ? null : $"task:{urgent.Id}:{urgent.State.ToString().ToLowerInvariant()}";
        if ((urgent ?? selected ?? focused ?? eligible) is { } item)
        {
            var state = item.State; var headline = $"{item.Provider.Name()} · {item.State.Label()}";
            var subtitle = active > 1 ? $"{active}개 작업 진행 중 · 클릭해서 보기" : $"{(item.Project.Length == 0 ? item.ShortID : item.Project)} · 클릭해서 보기";
            if (urgent is null && PetTaskSummary.RunningHeadline(tasks) is { } multiple) { state = WorkState.Running; headline = multiple; }
            else if (urgent is not null && active > 0) subtitle = $"작업 중: {PetTaskSummary.RunningDetail(tasks)}";
            return new(state, headline, subtitle, alert, tip);
        }
        return new(WorkState.Idle, tasks.Any(t => t.State == WorkState.Unknown) ? "상태를 확인할 작업이 있어요" : dozing ? identity.DozeHeadline : identity.IdleHeadline, idleSubtitle, null, tip);
    }
}
