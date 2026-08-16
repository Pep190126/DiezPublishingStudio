using System.Text;
using Avalonia;
using Avalonia.Controls;

namespace DiezPublishingStudio;

/// <summary>
/// Production desktop entry point. CI/self-test modes continue through Program.Main unchanged.
/// Normal Windows startup keeps Avalonia's classic lifetime and repairs only the known premature
/// NullDispatcherImpl cache after Win32 platform services have been initialized.
/// </summary>
internal static class DesktopEntryPoint
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
                SafeStartupTrace.Write("desktop-entry | mutex-already-owned");
                return 0;
            }

            DispatcherBootstrapProbe.TraceCachedState("entry-before-builder");

            var builder = Program.BuildAvaloniaApp()
                .AfterPlatformServicesSetup(_ => DispatcherBootstrapProbe.PinAfterPlatformServicesSetup());

            SafeStartupTrace.Write("desktop-entry | classic-lifetime-start");
            var exitCode = builder.StartWithClassicDesktopLifetime(
                args,
                ShutdownMode.OnExplicitShutdown);
            SafeStartupTrace.Write("desktop-entry | classic-lifetime-return | code=" + exitCode);

            GC.KeepAlive(mutex);
            return exitCode;
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("desktop-entry | fatal | " + ex);
            CrashDiagnostics.Error("desktop-startup", ex);
            return 1;
        }
    }
}
