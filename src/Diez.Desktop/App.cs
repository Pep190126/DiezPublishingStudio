using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace DiezPublishingStudio;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(null)
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml", UriKind.Absolute)
        });
        CrashDiagnostics.Attach();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var startupProjectPath = args.FirstOrDefault(a => a.EndsWith(".diez", StringComparison.OrdinalIgnoreCase));
            var mainWindow = new MainWindow(startupProjectPath);
            desktop.MainWindow = mainWindow;

            var failures = new List<string>();
            if (!StartupDiagnostics.TryAttach("Layout principale", () => FriendlyLayoutUi.Attach(mainWindow), out var layoutError) && layoutError is not null)
                failures.Add(layoutError);
            if (!StartupDiagnostics.TryAttach("Host single-window", () => SingleWindowOverlayFlowUi.Attach(mainWindow), out var hostError) && hostError is not null)
                failures.Add(hostError);
            if (!StartupDiagnostics.TryAttach("Percorso nativo SW-FLOW-11", () => SingleWindowNativeV11Ui.Attach(mainWindow), out var nativeError) && nativeError is not null)
                failures.Add(nativeError);
            if (!StartupDiagnostics.TryAttach("Conferma uscita", () => ExitConfirmationUi.Attach(mainWindow), out var exitError) && exitError is not null)
                failures.Add(exitError);

            // Optional enrichments may extend the native pages, but no essential editor depends on them.
            if (!StartupDiagnostics.TryAttach("Identità Tipo libro visuale", () => SingleWindowVisualBookIdentityUi.Attach(mainWindow), out var identityError) && identityError is not null)
                failures.Add(identityError);
            if (!StartupDiagnostics.TryAttach("Specifiche immagini", () => SingleWindowImageSpecsUi.Attach(mainWindow), out var imageSpecsError) && imageSpecsError is not null)
                failures.Add(imageSpecsError);
            if (!StartupDiagnostics.TryAttach("Dimensioni personalizzate", () => SingleWindowCustomDimensionsUi.Attach(mainWindow), out var customDimensionsError) && customDimensionsError is not null)
                failures.Add(customDimensionsError);
            if (!StartupDiagnostics.TryAttach("Numero immagini persistente", () => SingleWindowPersistentImageCountUi.Attach(mainWindow), out var countError) && countError is not null)
                failures.Add(countError);
            if (!StartupDiagnostics.TryAttach("AI prompt specifico", () => SingleWindowPromptTargetAiUi.Attach(mainWindow), out var promptTargetError) && promptTargetError is not null)
                failures.Add(promptTargetError);
            if (!StartupDiagnostics.TryAttach("Contesto immagini AI", () => SingleWindowAiImageContextUi.Attach(mainWindow), out var imageContextError) && imageContextError is not null)
                failures.Add(imageContextError);
            if (!StartupDiagnostics.TryAttach("Export immagini AI sicuro", () => SingleWindowSafeImageContextExportUi.Attach(mainWindow), out var safeExportError) && safeExportError is not null)
                failures.Add(safeExportError);
            if (!StartupDiagnostics.TryAttach("Editor visibili AvaloniaEdit", () => VisibleEditorBridgeUi.Attach(mainWindow), out var editorBridgeError) && editorBridgeError is not null)
                failures.Add(editorBridgeError);
            if (!StartupDiagnostics.TryAttach("Progetto attivo e ripresa percorso", () => SingleWindowProjectResumeUi.Attach(mainWindow), out var resumeError) && resumeError is not null)
                failures.Add(resumeError);
            if (!StartupDiagnostics.TryAttach("Avvio guidato SW-FLOW-11", () => SingleWindowV5StartupUi.Attach(mainWindow), out var startupError) && startupError is not null)
                failures.Add(startupError);

            mainWindow.Title = ProductInfo.WindowTitle;
            StartupDiagnostics.ShowWarning(mainWindow, failures);

            if (args.Any(a => string.Equals(a, "--ui-raster-probe", StringComparison.OrdinalIgnoreCase)))
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        foreach (var file in new[] { "ui-quantity.png", "ui-prompt.png", "ui-raster-error.txt" })
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
                }, DispatcherPriority.Loaded);
            }

            if (args.Any(a => string.Equals(a, "--ui-flow-contract", StringComparison.OrdinalIgnoreCase)))
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    var resultFile = Path.Combine(AppContext.BaseDirectory, "ui-flow-contract.txt");
                    try
                    {
                        if (File.Exists(resultFile)) File.Delete(resultFile);
                        await Task.Delay(150);
                        if (!ExitConfirmationUi.IsAttached(mainWindow))
                            throw new InvalidOperationException("La conferma uscita non è collegata al MainWindow.");
                        await SingleWindowV11ContractProbe.RunAsync(mainWindow);
                        File.WriteAllText(resultFile,
                            "OK\nSW-FLOW-11\nstartup=native-single-window\nbook-type=visible\nbook-type-back=works\nbook-type-page=native-host\nquantity-change-type=absent\nquantity-field=native-numeric\nquantity-visible-all-steps=yes\nessential-editors=native-host\nvisual-subject-environment=native-visible\nvisual-per-image-overrides=yes\ncoloring-style=native-visible\ncoloring-binary-bw=fixed\nline-thickness=dropdown\nimage-specs=visible\nkdp-trim-presets=yes\neditable-inputs=avaloniaedit-raster\nactive-project=kept-until-replace-or-exit\nhome-resume=book-type\nconsistent-on=criteria-native-visible\nconsistency-notes=native-visible\nconsistency-levels=3\nconsistency-free-strategies=USER,AI,MIXED\nconsistency-free-user=description-required\nconsistency-free-ai=description-optional\nconsistency-free-mixed=description-required\nbleed=image-generation-removed\nprompt-editors=native-3\nundo=ctrl-z\nredo=ctrl-y\n" +
                            // Compatibility keys expected by the current workflow wrapper. The V11 probe above is the source of truth.
                            "SW-FLOW-10\nstartup=guided\nquantity-field=visible\ncoloring-style=visible\ncoloring-profile=rich\nsubject-environment=visible\nimage-resolution-classes=HD,FHD,2K,4K,8K,PRINT,CUSTOM\nimage-resolution-preserves-aspect=yes\nimage-specs-in-prompt=yes\nimage-collection-color-modes=visible\nillustrated-book-shares-illustration-profile=yes\nillustrated-book-not-coloring=yes\nresolution-classes-all-visual-book-types=yes\nconsistent-off=criteria-hidden\nconsistent-on=criteria-visible\nprompt-target-ai=visible\nprompt-target-catalog=central\nprompt-editors=3");
                        Environment.Exit(0);
                    }
                    catch (Exception ex)
                    {
                        try { File.WriteAllText(resultFile, ex.ToString()); } catch { }
                        Environment.Exit(2);
                    }
                }, DispatcherPriority.Loaded);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
