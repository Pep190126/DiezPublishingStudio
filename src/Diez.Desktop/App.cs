using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace DiezPublishingStudio;

public sealed class App : Application
{
    public override void Initialize()
    {
        CrashDiagnostics.Attach();
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var startupProjectPath = args.FirstOrDefault(a => a.EndsWith(".diez", StringComparison.OrdinalIgnoreCase));
            var rasterProbe = args.Any(a => string.Equals(a, "--ui-raster-probe", StringComparison.OrdinalIgnoreCase));
            var flowProbe = args.Any(a => string.Equals(a, "--ui-flow-contract", StringComparison.OrdinalIgnoreCase));

            if (rasterProbe || flowProbe)
            {
                var mainWindow = new MainWindow(startupProjectPath);
                desktop.MainWindow = mainWindow;

                Dispatcher.UIThread.Post(async () =>
                {
                    var failures = await AttachProductionModulesAsync(mainWindow);
                    StartupDiagnostics.ShowWarning(mainWindow, failures);

                    if (rasterProbe)
                        await RunRasterProbeAsync(mainWindow);
                    else
                        await RunFlowProbeAsync(mainWindow);
                }, DispatcherPriority.Loaded);
            }
            else
            {
                // Keep the diagnostic shell alive independently from automatic lifetime bookkeeping. On affected
                // machines the standalone window reached Opened and then the dispatcher shut down before its first
                // Loaded turn. Explicit shutdown removes OnLastWindowClose from that first-frame path entirely.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                SafeStartupTrace.Write("desktop-shutdown-mode=OnExplicitShutdown");

                desktop.Exit += (_, e) => SafeStartupTrace.Write("desktop-exit | code=" + e.ApplicationExitCode);
                desktop.ShutdownRequested += (_, _) => SafeStartupTrace.Write("desktop-shutdown-requested");
                Dispatcher.UIThread.ShutdownStarted += (_, _) => SafeStartupTrace.Write("dispatcher-shutdown-started");
                Dispatcher.UIThread.ShutdownFinished += (_, _) => SafeStartupTrace.Write("dispatcher-shutdown-finished");
                Dispatcher.UIThread.UnhandledException += (_, e) =>
                    SafeStartupTrace.Write("dispatcher-unhandled-exception | " + e.Exception);

                var activationCompleted = false;
                var userClosingShell = false;

                var safeWindow = SafeDesktopStartupUi.CreateStandalone(async shell =>
                {
                    SafeStartupTrace.Write("before-mainwindow-construction");
                    shell.Title = ProductInfo.WindowTitle + " — costruzione MainWindow";

                    var mainWindow = new MainWindow(startupProjectPath)
                    {
                        Title = ProductInfo.WindowTitle
                    };
                    SafeStartupTrace.Write("after-mainwindow-construction");

                    desktop.MainWindow = mainWindow;
                    SafeStartupTrace.Write("before-mainwindow-show");
                    mainWindow.Show();
                    SafeStartupTrace.Write("after-mainwindow-show");

                    await Dispatcher.UIThread.InvokeAsync(
                        () => SafeStartupTrace.Write("mainwindow-loaded-dispatcher-turn"),
                        DispatcherPriority.Loaded);
                    await Task.Delay(100);

                    var failures = await AttachProductionModulesAsync(mainWindow);
                    StartupDiagnostics.ShowWarning(mainWindow, failures);
                    SafeStartupTrace.Write("all-production-modules-completed");

                    activationCompleted = true;
                    desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    SafeStartupTrace.Write("desktop-shutdown-mode=OnMainWindowClose");
                    shell.Close();
                });

                safeWindow.Closing += (_, _) =>
                {
                    SafeStartupTrace.Write("bare-shell-closing | activationCompleted=" + activationCompleted);
                    if (!activationCompleted)
                        userClosingShell = true;
                };
                safeWindow.Closed += (_, _) =>
                {
                    SafeStartupTrace.Write("bare-shell-closed | activationCompleted=" + activationCompleted);
                    if (userClosingShell && !activationCompleted)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            SafeStartupTrace.Write("explicit-shutdown-after-user-shell-close");
                            desktop.Shutdown();
                        });
                    }
                };

                desktop.MainWindow = safeWindow;
                SafeStartupTrace.Write("bare-shell-assigned-as-mainwindow");
            }
        }

        SafeStartupTrace.Write("before-framework-initialization-completed-base");
        base.OnFrameworkInitializationCompleted();
        SafeStartupTrace.Write("after-framework-initialization-completed-base");
    }

    internal static async Task<IReadOnlyList<string>> AttachProductionModulesAsync(MainWindow window)
    {
        var failures = new List<string>();
        var modules = new (string Name, Action Attach)[]
        {
            ("Layout principale", () => FriendlyLayoutUi.Attach(window)),
            ("Host single-window", () => SingleWindowOverlayFlowUi.Attach(window)),
            ("Percorso nativo SW-FLOW-12", () => SingleWindowNativeV11Ui.Attach(window)),
            ("Conferma uscita", () => ExitConfirmationUi.Attach(window)),
            ("Identità Tipo libro visuale", () => SingleWindowVisualBookIdentityUi.Attach(window)),
            ("Specifiche immagini", () => SingleWindowImageSpecsQuantityOnlyUi.Attach(window)),
            ("Dimensioni personalizzate", () => SingleWindowCustomDimensionsUi.Attach(window)),
            ("Numero immagini persistente", () => SingleWindowPersistentImageCountUi.Attach(window)),
            ("Profili Coloring HARD indipendenti", () => SingleWindowColoringStylePolicyUi.Attach(window)),
            ("Label Multi-soggetto nativa", () => SingleWindowMultiSubjectLabelUi.Attach(window)),
            ("Scene strutturate", () => SingleWindowStructuredSceneUi.Attach(window)),
            ("Consenso libreria stile Custom", () => SingleWindowCustomStyleConsentUi.Attach(window)),
            ("Prompt Compiler 3.6", () => SingleWindowPromptTargetAiUi.Attach(window)),
            ("Contesto immagini V3", () => SingleWindowAiImageContextUi.Attach(window)),
            ("Pipeline visuale unica", () => SingleWindowSafeImageContextExportUi.Attach(window)),
            ("Controllo qualità Vision", () => SingleWindowVisionValidationUi.Attach(window)),
            ("Progetto attivo e ripresa percorso", () => SingleWindowProjectResumeUi.Attach(window)),
            ("Avvio guidato SW-FLOW-12", () => SingleWindowV5StartupUi.Attach(window))
        };

        foreach (var module in modules)
        {
            window.Title = ProductInfo.WindowTitle + " — caricamento: " + module.Name;
            SafeStartupTrace.Write("before-module: " + module.Name);

            if (!StartupDiagnostics.TryAttach(module.Name, module.Attach, out var error) && error is not null)
            {
                failures.Add(error);
                SafeStartupTrace.Write("module-error: " + module.Name + " | " + error);
            }
            else
            {
                SafeStartupTrace.Write("after-module: " + module.Name);
            }

            await Task.Delay(75);
        }

        window.Title = ProductInfo.WindowTitle;
        return failures;
    }

    private static async Task RunRasterProbeAsync(MainWindow mainWindow)
    {
        try
        {
            foreach (var file in new[] { "ui-quantity.png", "ui-prompt.png", "ui-consistent.png", "ui-raster-error.txt" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, file);
                if (File.Exists(path)) File.Delete(path);
            }

            await Task.Delay(200);
            await SingleWindowPhysicalScreenshotProbe.RunAsync(mainWindow);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-raster-error.txt"), ex.ToString()); } catch { }
            Environment.Exit(3);
        }
    }

    private static async Task RunFlowProbeAsync(MainWindow mainWindow)
    {
        var resultFile = Path.Combine(AppContext.BaseDirectory, "ui-flow-contract.txt");
        try
        {
            if (File.Exists(resultFile)) File.Delete(resultFile);
            await Task.Delay(150);

            if (!ExitConfirmationUi.IsAttached(mainWindow))
                throw new InvalidOperationException("La conferma uscita non è collegata al MainWindow.");

            await SingleWindowV11ContractProbe.RunAsync(mainWindow);
            await MultiSubjectUiContractProbe.RunAsync(mainWindow);
            await StructuredSceneUiContractProbeV2.RunAsync(mainWindow);
            await SingleWindowProjectResumeUi.RunContractAsync(mainWindow);
            await SingleWindowResponseReviewUiContractProbe.RunAsync(mainWindow);

            File.WriteAllText(resultFile,
                "OK\nSW-FLOW-12\nstartup=deferred-safe-first-frame\neditable-inputs=native-textbox-safe-startup\nstructured-scenes=optional\nprompt-provider-compiler-current=3.6\nvision-scene-participants=hard\n");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(resultFile, ex.ToString()); } catch { }
            Environment.Exit(2);
        }
    }
}
