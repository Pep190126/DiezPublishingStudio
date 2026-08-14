using System.Text;
using Avalonia;
using Avalonia.Controls;

namespace DiezPublishingStudio;

/// <summary>
/// Real-machine startup entry point. CI/self-test modes continue through Program.Main unchanged.
/// The normal desktop path uses Avalonia's standard classic lifetime and only adds dispatcher
/// bootstrap diagnostics after platform services have been initialized.
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

            DispatcherBootstrapProbe.TraceCachedState("entry-before-builder");

            var builder = Program.BuildAvaloniaApp()
                .AfterPlatformServicesSetup(_ => DispatcherBootstrapProbe.PinAfterPlatformServicesSetup());

            SafeStartupTrace.Write("diagnostic-entry | standard-lifetime-start");
            var exitCode = builder.StartWithClassicDesktopLifetime(
                args,
                ShutdownMode.OnExplicitShutdown);
            SafeStartupTrace.Write("diagnostic-entry | standard-lifetime-return | code=" + exitCode);

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
