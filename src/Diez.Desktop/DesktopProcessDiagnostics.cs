using System.Runtime.CompilerServices;

namespace DiezPublishingStudio;

internal static class DesktopProcessDiagnostics
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        try
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)) ||
                args.Any(a => string.Equals(a, "--ui-headless-ci", StringComparison.OrdinalIgnoreCase)))
                return;

            SafeStartupTrace.Reset("process-module-enter | pid=" + Environment.ProcessId + " | args=" +
                                   (args.Length == 0 ? "<none>" : string.Join(" | ", args)));
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                SafeStartupTrace.Write("process-exit-event | pid=" + Environment.ProcessId);
        }
        catch
        {
        }
    }
}
