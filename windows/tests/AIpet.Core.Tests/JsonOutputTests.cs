using System.Text;
using System.Text.Json;
using AIpet.Core;

namespace AIpet.Core.Tests;

public sealed class JsonOutputTests : TestSupport
{
    [Fact]
    public void DiagnosticsKeepKoreanReadableAndValidJson()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new Snapshot(now, [], [new(Provider.Codex, false, 0, "기록 폴더에 접근할 수 없음")]);
        var json = JsonData.Sorted(snapshot, true);
        Assert.Contains("기록 폴더에 접근할 수 없음", json);
        Assert.DoesNotContain("\\u", json);
        Assert.Equal(snapshot.Connections[0].Detail, JsonSerializer.Deserialize<Snapshot>(json, JsonData.Options)!.Connections[0].Detail);

        var usage = JsonData.Sorted(new UsageState().Diagnostic(now), true);
        Assert.Contains("계정 확인 불가", usage);
        Assert.Contains("실행 안 함", usage);
        Assert.DoesNotContain("\\u", usage);
        using var parsed = JsonDocument.Parse(usage);
        Assert.Equal("실행 안 함", parsed.RootElement.GetProperty("directQuery").GetString());
    }

    [Fact]
    public void KoreanHookFileStillDecodesIncludingEscapedCharacters()
    {
        const string project = "한글\"프로젝트";
        var payload = JsonSerializer.Serialize(new { session_id = "test", hook_event_name = "PermissionRequest", cwd = "C:\\work\\" + project });
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        HookBridge.Run(Provider.Claude, input, Root);
        var json = File.ReadAllText(Assert.Single(Directory.GetFiles(Root, "*.json")));
        Assert.Contains("한글", json);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(1, parsed.RootElement.GetProperty("version").GetInt32());
        var item = HookBridge.DecodeEvent(parsed.RootElement);
        Assert.NotNull(item);
        Assert.Equal(project, item.Project);
        Assert.Equal(WorkState.Waiting, item.State);
    }
}
