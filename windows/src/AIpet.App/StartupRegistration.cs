using Microsoft.Win32;
namespace AIpet.App;
/// Starts the pet when the user signs in, through the per-user Run key (no administrator rights needed).
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AIpet";
    /// Writing on every launch keeps the entry pointed at the copy that is actually running.
    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (!enabled) { key.DeleteValue(ValueName, false); return; }
            if (Environment.ProcessPath is not { } path) return;
            var command = $"\"{path}\"";
            if (key.GetValue(ValueName) as string != command) key.SetValue(ValueName, command);
        }
        catch (Exception error) { Program.Errors.Append(error); }
    }
}
