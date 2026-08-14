using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Runs the classic desktop lifetime in explicit phases so startup diagnostics can observe
/// the exact boundary between Window.Show() and the Win32 dispatcher message loop.
/// </summary>
internal static class ManualDesktopLifetimeRunner
{
    public static int Run(string[] args)
    {
        SafeStartupTrace.Write("manual-lifetime | phase=before-setup");

        var builder = Program.BuildAvaloniaApp();
        builder.SetupWithClassicDesktopLifetime(
            args,
            lifetime => lifetime.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown);

        SafeStartupTrace.Write("manual-lifetime | phase=after-setup");

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            throw new InvalidOperationException("Classic desktop lifetime non disponibile dopo il setup Avalonia.");

        var mainWindow = desktop.MainWindow
            ?? throw new InvalidOperationException("MainWindow diagnostica non assegnata dopo il setup Avalonia.");

        var exitCode = 0;
        desktop.Exit += (_, e) =>
        {
            exitCode = e.ApplicationExitCode;
            SafeStartupTrace.Write("manual-lifetime | desktop-exit-observed | code=" + exitCode);
        };

        SafeStartupTrace.Write("manual-lifetime | phase=before-show");
        mainWindow.Show();
        SafeStartupTrace.Write("manual-lifetime | phase=after-show");

        // This is the critical probe missing from the normal ClassicDesktopStyleApplicationLifetime:
        // Show() is now completely finished, while Dispatcher.MainLoop has not started yet.
        var consumedQuit = Win32QuitMessageProbe.ProbeAndConsume(out var quitCode);
        SafeStartupTrace.Write(
            "manual-lifetime | post-show-wmquit | consumed=" + consumedQuit + " | code=" + quitCode);

        // Seed the per-thread Win32 last-error slot. If Win32DispatcherImpl.GetMessage returns -1,
        // Avalonia reads that error internally; reading it immediately after MainLoop returns gives
        // us an additional diagnostic signal on the affected machine.
        if (OperatingSystem.IsWindows())
            Marshal.SetLastPInvokeError(0);

        SafeStartupTrace.Write("manual-lifetime | phase=before-mainloop");
        Dispatcher.UIThread.MainLoop(CancellationToken.None);

        var lastPInvokeError = OperatingSystem.IsWindows() ? Marshal.GetLastPInvokeError() : 0;
        SafeStartupTrace.Write(
            "manual-lifetime | phase=after-mainloop | lastPInvokeError=" + lastPInvokeError + " | exitCode=" + exitCode);

        Environment.ExitCode = exitCode;
        return exitCode;
    }
}
