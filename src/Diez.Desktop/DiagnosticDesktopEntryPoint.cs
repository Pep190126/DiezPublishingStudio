using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// Temporary real-machine startup entry point. CI/self-test modes continue through Program.Main unchanged;
/// only the normal desktop path is split into setup -> Show -> Win32 probe -> MainLoop phases.
/// </summary>
internal static class DiagnosticDesktopEntryPoint
{
    private const string AppMutexName = "DiezPublishingStudio.App";

    [STAThread]
    public static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)) ||
            args.Any(a => string.Equals(a, "--ui-headless-ci", StringComparison.OrdinalIgnoreCase)))
            return Program.Main(args);

        CrashDiagnostics.Attach();

        try
        {
            using var mutex = new Mutex(true, AppMutexName, out var createdNew);
            if (!createdNew)
            {
                SafeStartupTrace.Write("diagnostic-entry | mutex-already-owned");
                return 0;
            }

            SafeStartupTrace.Write("diagnostic-entry | manual-lifetime-start");
            var exitCode = ManualDesktopLifetimeRunner.Run(args);
            SafeStartupTrace.Write("diagnostic-entry | manual-lifetime-return | code=" + exitCode);
            GC.KeepAlive(mutex);
            return exitCode;
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("diagnostic-entry | fatal | " + ex);
            CrashDiagnostics.Error("diagnostic-desktop-startup", ex);
            return 1;
        }
    }
}
