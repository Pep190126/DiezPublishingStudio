using Avalonia;
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
                // Real desktop startup now creates no MainWindow and no feature module at all. The first visible
                // frame is a standalone Window with an opaque background. MainWindow construction itself is
                // deferred until explicit activation so a machine-specific render failure can be isolated cleanly.
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

                    shell.Close();
                });

                desktop.MainWindow = safeWindow;
            }
        }

        base.OnFrameworkInitializationCompleted();
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

            // Yield between modules. If a module starts a runaway dispatcher/layout cascade, the trace and
            // window title preserve the last completed stage instead of hiding it behind later attaches.
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
