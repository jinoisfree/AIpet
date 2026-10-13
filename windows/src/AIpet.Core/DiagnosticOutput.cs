using System.Text;
namespace AIpet.Core;

public sealed class DiagnosticOutput(Func<Stream>? openOutput = null, Action<string, byte[]>? writeFile = null, string? root = null)
{
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIpet");
    public int Write(string json, string name, string? output = null)
    {
        var bytes = new UTF8Encoding(false).GetBytes(json + "\n");
        if (output is null)
        {
            try
            {
                using var stream = (openOutput ?? Console.OpenStandardOutput)();
                // WinExe without a console can return Stream.Null instead of throwing.
                if (ReferenceEquals(stream, Stream.Null) || !stream.CanWrite) throw new IOException();
                stream.Write(bytes); stream.Flush(); return 0;
            }
            catch { }
        }
        try
        {
            var path = output ?? Path.Combine(root ?? DefaultRoot, name);
            if (writeFile is not null) writeFile(path, bytes);
            else { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllBytes(path, bytes); }
            return 0;
        }
        catch { return 1; }
    }
}
