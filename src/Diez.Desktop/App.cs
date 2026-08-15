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
            var homeFileDialogProbe = args.Any(a => string.Equals(a, "--home-file-dialog-probe", StringComparison.OrdinalIgnoreCase));

            if (rasterProbe || flowProbe || homeFileDialogProbe)
            {
                // Probe startup must match production startup: build the complete visual tree before MainWindow
                // is ever presented. Mounting stable-root after Opened creates a synthetic zero-layout state that
                // the installed application never uses.
                var mainWindow = new MainWindow(startupProjectPath);
                var failures = AttachProductionModules(mainWindow);
                StartupDiagnostics.ShowWarning(mainWindow, failures);

                var probeOpened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                mainWindow.Opened += (_, _) =>
                {
                    probeOpened.TrySetResult(true);
                    SafeStartupTrace.Write(
                        "probe-mainwindow-opened | visible=" + mainWindow.IsVisible +
                        " | clientSize=" + mainWindow.ClientSize +
                        " | stableRootInstalled=" + StableWorkflowRootUi.IsInstalled(mainWindow));
                };

                desktop.MainWindow = mainWindow;

                Dispatcher.UIThread.Post(async () =>
                {
                    if (homeFileDialogProbe)
                        await RunHomeFileDialogProbeAsync(mainWindow);
                    else if (rasterProbe)
                        await RunRasterProbeAsync(mainWindow);
                    else
                        await RunFlowProbeAsync(mainWindow, probeOpened.Task);
                }, DispatcherPriority.Loaded);
            }
            else
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                SafeStartupTrace.Write("desktop-shutdown-mode=OnExplicitShutdown");

                desktop.Exit += (_, e) => SafeStartupTrace.Write("desktop-exit | code=" + e.ApplicationExitCode);
                desktop.ShutdownRequested += (_, _) => SafeStartupTrace.Write("desktop-shutdown-requested");
                Dispatcher.UIThread.ShutdownStarted += (_, _) => SafeStartupTrace.Write("dispatcher-shutdown-started");
                Dispatcher.UIThread.ShutdownFinished += (_, _) => SafeStartupTrace.Write("dispatcher-shutdown-finished");
                Dispatcher.UIThread.UnhandledException += (_, e) =>
                    SafeStartupTrace.Write("dispatcher-unhandled-exception | " + e.Exception);

                SafeStartupTrace.Write("before-mainwindow-construction");
                var mainWindow = new MainWindow(startupProjectPath)
                {
                    Title = ProductInfo.WindowTitle
                };
                SafeStartupTrace.Write("after-mainwindow-construction");

                var failures = AttachProductionModules(mainWindow);
                StartupDiagnostics.ShowWarning(mainWindow, failures);
                SafeStartupTrace.Write("all-production-modules-completed");

                desktop.MainWindow = mainWindow;
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                SafeStartupTrace.Write("mainwindow-assigned-as-mainwindow");
                SafeStartupTrace.Write("desktop-shutdown-mode=OnMainWindowClose");

                mainWindow.Opened += (_, _) =>
                {
                    SafeStartupTrace.Write(
                        "mainwindow-opened | enabled=" + mainWindow.IsEnabled +
                        " | active=" + mainWindow.IsActive +
                        " | visible=" + mainWindow.IsVisible);
                    Dispatcher.UIThread.Post(() =>
                    {
                        mainWindow.Activate();
                        SafeStartupTrace.Write(
                            "mainwindow-activated-dispatcher-turn | enabled=" + mainWindow.IsEnabled +
                            " | active=" + mainWindow.IsActive);
                    }, DispatcherPriority.Loaded);
                };
            }
        }

        SafeStartupTrace.Write("before-framework-initialization-completed-base");
        base.OnFrameworkInitializationCompleted();
        SafeStartupTrace.Write("after-framework-initialization-completed-base");
    }

    internal static IReadOnlyList<string> AttachProductionModules(MainWindow window)
    {
        var failures = new List<string>();
        var modules = new (string Name, Action Attach)[]
        {
            ("Layout principale", () => FriendlyLayoutUi.Attach(window)),
            ("Dialoghi Home Windows owned", () => WindowsHomeFileDialogUi.Attach(window)),
            ("Host single-window", () => SingleWindowOverlayFlowUi.Attach(window)),
            ("Radice Home/Workflow stabile", () => StableWorkflowRootUi.Attach(window)),
            ("Content host pagine stabile", () => StablePageContentHostUi.Attach(window)),
            ("Percorso nativo SW-FLOW-12", () => SingleWindowNativeV11Ui.Attach(window)),
            ("Ingresso percorso nativo stabile", () => SingleWindowStableEntryBridgeUi.Attach(window)),
            ("Home stabile: materiali e ritorno", () => StableHomeUsabilityUi.Attach(window)),
            ("Conferma uscita", () => ExitConfirmationUi.Attach(window)),
            ("Identità Tipo libro visuale", () => SingleWindowVisualBookIdentityUi.Attach(window)),
            ("Titolo libro allineato e iniziale", () => SingleWindowBookTitleUsabilityUi.Attach(window)),
            ("Specifiche immagini", () => SingleWindowImageSpecsQuantityOnlyUi.Attach(window)),
            ("Dimensioni personalizzate", () => SingleWindowCustomDimensionsUi.Attach(window)),
            ("Numero immagini persistente", () => SingleWindowPersistentImageCountUi.Attach(window)),
            ("Profili Coloring HARD indipendenti", () => SingleWindowColoringStylePolicyUi.Attach(window)),
            ("Label Multi-soggetto nativa", () => SingleWindowMultiSubjectLabelUi.Attach(window)),
            ("Scene strutturate", () => SingleWindowStructuredSceneUi.Attach(window)),
            ("Consenso libreria stile Custom", () => SingleWindowCustomStyleConsentUi.Attach(window)),
            ("Prompt Compiler 3.6", () => SingleWindowPromptTargetAiUi.Attach(window)),
            ("Contesto immagini V3", () => SingleWindowAiImageContextUi.Attach(window)),
            ("Anteprima e scroll Quantità", () => SingleWindowQuantityUsabilityUi.Attach(window)),
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
        }

        window.Title = ProductInfo.WindowTitle;
        return failures;
    }

    private static async Task RunHomeFileDialogProbeAsync(MainWindow mainWindow)
    {
        var errorFile = Path.Combine(AppContext.BaseDirectory, "home-file-dialog-probe-error.txt");
        try
        {
            if (File.Exists(errorFile)) File.Delete(errorFile);
            await WindowsOwnedDialogProbe.RunAsync(mainWindow);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(errorFile, ex.ToString()); } catch { }
            Environment.Exit(4);
        }
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

    private static async Task RunFlowProbeAsync(MainWindow mainWindow, Task openedTask)
    {
        var resultFile = Path.Combine(AppContext.BaseDirectory, "ui-flow-contract.txt");
        try
        {
            if (File.Exists(resultFile)) File.Delete(resultFile);

            if (!mainWindow.IsVisible)
                mainWindow.Show();

            var openedCompleted = await Task.WhenAny(openedTask, Task.Delay(3000));
            if (!ReferenceEquals(openedCompleted, openedTask))
                throw new TimeoutException("Classic flow probe non ha ricevuto MainWindow.Opened entro 3 secondi.");
            await openedTask;
            SafeStartupTrace.Write(
                "classic-flow-window-opened | visible=" + mainWindow.IsVisible +
                " | clientSize=" + mainWindow.ClientSize);

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await WaitForClassicProbeLayoutAsync(mainWindow);

            if (!ExitConfirmationUi.IsAttached(mainWindow))
                throw new InvalidOperationException("La conferma uscita non è collegata al MainWindow.");

            await SingleWindowNativeClickContract.RunAsync(mainWindow);
            await FlowContractRootMountProbe.EnsureMountedAsync(mainWindow);
            await SingleWindowV11ContractProbe.RunAsync(mainWindow);
            await MultiSubjectUiContractProbe.RunAsync(mainWindow);
            await StructuredSceneUiContractProbeV2.RunAsync(mainWindow);
            await SingleWindowProjectResumeUi.RunContractAsync(mainWindow);
            await SingleWindowResponseReviewUiContractProbe.RunAsync(mainWindow);

            File.WriteAllText(resultFile,
                "OK\nSW-FLOW-12\nstartup=direct-completed-mainwindow\nproduction-entry=native-v11\nvisual-root=permanent-home-workflow\nruntime-root-swap=no\nreal-click-quantity-to-prompt=yes\neditable-inputs=native-textbox-safe-startup\nstructured-scenes=optional\nprompt-provider-compiler-current=3.6\nvision-scene-participants=hard\n");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(resultFile, ex.ToString()); } catch { }
            Environment.Exit(2);
        }
    }

    private static async Task WaitForClassicProbeLayoutAsync(MainWindow mainWindow)
    {
        for (var i = 0; i < 40; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            var root = StableWorkflowRootUi.StableRoot(mainWindow);
            var home = StableWorkflowRootUi.HomeRoot(mainWindow);
            var workflow = StableWorkflowRootUi.WorkflowRoot(mainWindow);
            if (root is not null && home is not null && workflow is not null &&
                root.Bounds.Width > 0 && root.Bounds.Height > 0 &&
                home.Bounds.Width > 0 && home.Bounds.Height > 0 &&
                workflow.Bounds.Width > 0 && workflow.Bounds.Height > 0)
            {
                SafeStartupTrace.Write(
                    "classic-flow-window-ready | stableRoot=true" +
                    " | rootBounds=" + root.Bounds +
                    " | homeBounds=" + home.Bounds +
                    " | workflowBounds=" + workflow.Bounds);
                return;
            }
            await Task.Delay(25);
        }

        throw new InvalidOperationException(
            "Classic flow probe non ha ottenuto una MainWindow fisicamente misurata prima del contract.");
    }
}
