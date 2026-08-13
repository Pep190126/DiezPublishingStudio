using System.Text;

namespace DiezPublishingStudio;

internal static class SafeStartupTrace
{
    private const string FileName = "safe-startup-trace.log";

    public static void Reset(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory());
            File.WriteAllText(Path(), Header() + message + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory());
            File.AppendAllText(Path(), DateTimeOffset.Now.ToString("O") + " | " + message + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    public static string Path() => System.IO.Path.Combine(LogDirectory(), FileName);

    private static string LogDirectory() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Diez Publishing Studio",
        "logs");

    private static string Header() =>
        "Diez Publishing Studio safe startup trace" + Environment.NewLine +
        "Version: " + ProductInfo.Version + Environment.NewLine +
        "Started: " + DateTimeOffset.Now.ToString("O") + Environment.NewLine;
}
