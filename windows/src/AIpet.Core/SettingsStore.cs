using System.Text.Json;
namespace AIpet.Core;

public sealed record PetSettings(string PetName = PetIdentity.DefaultName, double PetScale = 100,
    Point? Position = null, bool BubbleAlwaysShown = false, bool ClaudeUsageDirectEnabled = false, string? CodexPath = null,
    bool LaunchAtLogin = true)
{
    public PetSettings Validated() => this with {
        PetName = PetName is not null && PetIdentity.ValidationMessage(PetName) is null ? PetIdentity.Normalized(PetName) : PetIdentity.DefaultName,
        PetScale = double.IsFinite(PetScale) ? Math.Clamp(PetScale, 25, 200) : 100,
        Position = Position is { } p && double.IsFinite(p.X) && double.IsFinite(p.Y) ? p : null
    };
}
public sealed class SettingsStore(string? path = null)
{
    public string FilePath { get; } = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AIpet", "settings.json");
    public PetSettings Load()
    {
        try { return (JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(FilePath), JsonData.Options) ?? new()).Validated(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return new(); }
    }
    public void Save(PetSettings settings)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath))!; Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(temporary, JsonData.Sorted(settings.Validated(), true)); File.Move(temporary, FilePath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
