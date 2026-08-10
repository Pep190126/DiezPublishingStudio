namespace DiezPublishingStudio;

internal static class CrashDiagnostics
{
    private static int _attached;

    public static void Attach()
    {
        if (Interlocked.Exchange(ref _attached, 1) != 0) return;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception;
            WriteFatal("AppDomain.UnhandledException", exception?.ToString() ?? e.ExceptionObject?.ToString() ?? "Unknown fatal error");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteFatal("TaskScheduler.UnobservedTaskException", e.Exception.ToString());
            e.SetObserved();
        };
    }

    public static void Navigation(string stage, string? detail = null)
    {
        Write("navigation.log", $"[{DateTimeOffset.Now:O}] {stage}{(string.IsNullOrWhiteSpace(detail) ? string.Empty : " · " + detail)}{Environment.NewLine}");
    }

    public static void Error(string stage, Exception ex)
    {
        Write("navigation-errors.log", $"[{DateTimeOffset.Now:O}] {stage}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
    }

    private static void WriteFatal(string source, string detail)
    {
        Write("fatal-errors.log", $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{detail}{Environment.NewLine}{Environment.NewLine}");
    }

    private static void Write(string fileName, string text)
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Diez Publishing Studio", "logs");
            Directory.CreateDirectory(root);
            File.AppendAllText(Path.Combine(root, fileName), text);
        }
        catch
        {
            // Diagnostics must never be able to crash Diez.
        }
    }
}
