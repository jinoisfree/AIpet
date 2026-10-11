using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace AIpet.Core;

public sealed record PetIdentity(string Name)
{
    public const string DefaultName = "태식이", SettingsKey = "petName";
    public const int MaximumLength = 20;
    public static string Normalized(string input) => Regex.Replace(input.Trim(), @"\s+", " ").Normalize(NormalizationForm.FormC);
    public static string? ValidationMessage(string input)
    {
        var name = Normalized(input);
        if (name.Length == 0) return "펫 이름을 입력해 주세요.";
        if (TextElements.Count(name) > MaximumLength) return "이름은 20자까지 입력할 수 있어요.";
        if (name.EnumerateRunes().Any(r => r.Value != 0x200D && Rune.GetUnicodeCategory(r) is UnicodeCategory.Control or UnicodeCategory.Format)) return "표시할 수 있는 문자로 이름을 입력해 주세요.";
        return null;
    }
    // Storage is supplied by the host; the WPF settings adapter belongs to stage 2.
    public static PetIdentity Load(IReadOnlyDictionary<string, string> settings)
    {
        var saved = settings.GetValueOrDefault(SettingsKey, DefaultName);
        return new(ValidationMessage(saved) is null ? Normalized(saved) : DefaultName);
    }
    public void Save(IDictionary<string, string> settings) => settings[SettingsKey] = Name;
    private bool HasFinalConsonant => Name.EnumerateRunes().LastOrDefault().Value is >= 0xAC00 and <= 0xD7A3 && (Name.EnumerateRunes().Last().Value - 0xAC00) % 28 != 0;
    public string Subject => Name + (HasFinalConsonant ? "이" : "가");
    public string Greeting => Name + (HasFinalConsonant ? "과" : "와") + " 함께";
    public string IdleHeadline => $"{Subject} 지켜보고 있어요";
    public string DozeHeadline => "나 자는거 아니다";
    public string DashboardIntroduction => $"AI가 일하는 동안, {Subject} 살펴볼게요.";
    public string DashboardTitle => $"AIpet · {Name} · AI 작업";
}
