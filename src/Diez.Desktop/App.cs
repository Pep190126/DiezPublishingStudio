using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace DiezPublishingStudio;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        CrashDiagnostics.Attach();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var startupProjectPath = args
                .FirstOrDefault(a => a.EndsWith(".diez", StringComparison.OrdinalIgnoreCase));
            var mainWindow = new MainWindow(startupProjectPath);
            desktop.MainWindow = mainWindow;

            var failures = new List<string>();

            if (!StartupDiagnostics.TryAttach("Layout principale", () => FriendlyLayoutUi.Attach(mainWindow), out var layoutError) && layoutError is not null)
                failures.Add(layoutError);
            if (!StartupDiagnostics.TryAttach("Host single-window", () => SingleWindowOverlayFlowUi.Attach(mainWindow), out var singleWindowError) && singleWindowError is not null)
                failures.Add(singleWindowError);
            if (!StartupDiagnostics.TryAttach("Pagina Tipo libro sicura", () => SingleWindowSafeBookTypePageUi.Attach(mainWindow), out var safeBookTypeError) && safeBookTypeError is not null)
                failures.Add(safeBookTypeError);
            if (!StartupDiagnostics.TryAttach("Conferma uscita", () => ExitConfirmationUi.Attach(mainWindow), out var exitConfirmationError) && exitConfirmationError is not null)
                failures.Add(exitConfirmationError);
            if (!StartupDiagnostics.TryAttach("Profilo Coloring", () => SingleWindowColoringProfileUi.Attach(mainWindow), out var coloringProfileError) && coloringProfileError is not null)
                failures.Add(coloringProfileError);
            if (!StartupDiagnostics.TryAttach("Profilo illustrazioni", () => SingleWindowImageCollectionProfileUi.Attach(mainWindow), out var illustrationProfileError) && illustrationProfileError is not null)
                failures.Add(illustrationProfileError);
            if (!StartupDiagnostics.TryAttach("Identità Tipo libro visuale", () => SingleWindowVisualBookIdentityUi.Attach(mainWindow), out var visualIdentityError) && visualIdentityError is not null)
                failures.Add(visualIdentityError);
            if (!StartupDiagnostics.TryAttach("Specifiche immagini", () => SingleWindowImageSpecsUi.Attach(mainWindow), out var imageSpecsError) && imageSpecsError is not null)
                failures.Add(imageSpecsError);
            if (!StartupDiagnostics.TryAttach("Dimensioni personalizzate esplicite", () => SingleWindowCustomDimensionsUi.Attach(mainWindow), out var customDimensionsError) && customDimensionsError is not null)
                failures.Add(customDimensionsError);
            if (!StartupDiagnostics.TryAttach("Criteri Consistent", () => SingleWindowConsistencyCriteriaUi.Attach(mainWindow), out var consistencyError) && consistencyError is not null)
                failures.Add(consistencyError);
            if (!StartupDiagnostics.TryAttach("Dati essenziali immagini", () => SingleWindowVisualEssentialsUi.Attach(mainWindow), out var visualEssentialsError) && visualEssentialsError is not null)
                failures.Add(visualEssentialsError);
            if (!StartupDiagnostics.TryAttach("Numero immagini persistente", () => SingleWindowPersistentImageCountUi.Attach(mainWindow), out var persistentCountError) && persistentCountError is not null)
                failures.Add(persistentCountError);
            if (!StartupDiagnostics.TryAttach("AI prompt specifico", () => SingleWindowPromptTargetAiUi.Attach(mainWindow), out var promptTargetError) && promptTargetError is not null)
                failures.Add(promptTargetError);
            if (!StartupDiagnostics.TryAttach("Contesto immagini AI V2", () => SingleWindowAiImageContextUi.Attach(mainWindow), out var imageContextError) && imageContextError is not null)
                failures.Add(imageContextError);
            if (!StartupDiagnostics.TryAttach("Export immagini AI sicuro", () => SingleWindowSafeImageContextExportUi.Attach(mainWindow), out var safeExportError) && safeExportError is not null)
                failures.Add(safeExportError);
            if (!StartupDiagnostics.TryAttach("Input editabili ben visibili", () => SingleWindowVisibleInputsUi.Attach(mainWindow), out var visibleInputsError) && visibleInputsError is not null)
                failures.Add(visibleInputsError);
            if (!StartupDiagnostics.TryAttach("Avvio guidato SW-FLOW-10", () => SingleWindowV5StartupUi.Attach(mainWindow), out var startupError) && startupError is not null)
                failures.Add(startupError);

            mainWindow.Title = ProductInfo.WindowTitle;
            StartupDiagnostics.ShowWarning(mainWindow, failures);

            if (args.Any(a => string.Equals(a, "--ui-flow-contract", StringComparison.OrdinalIgnoreCase)))
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    var resultFile = Path.Combine(AppContext.BaseDirectory, "ui-flow-contract.txt");
                    try
                    {
                        if (File.Exists(resultFile)) File.Delete(resultFile);
                        await Task.Delay(120);
                        if (!ExitConfirmationUi.IsAttached(mainWindow))
                            throw new InvalidOperationException("La conferma uscita non è collegata al MainWindow.");
                        await SingleWindowSafeBookTypePageUi.RunContractAsync(mainWindow);
                        await SingleWindowInstallerUiProbe.RunAsync(mainWindow);
                        File.WriteAllText(resultFile,
                            "OK\nSW-FLOW-10\nstartup=guided\nbook-type=visible\nbook-type-page=native-safe-v2\nbook-type-apply=safe-save-deferred-navigation\ncrash-diagnostics=persistent\nexit-confirmation=x-button\nquantity-field=visible\nquantity-field=numeric-native\nquantity-visible-all-steps=yes\nvisual-subject-environment=always-visible\nvisual-per-image-overrides=yes\ncoloring-style=visible\ncoloring-profile=rich\ncoloring-binary-bw=fixed\nline-thickness=dropdown\nsubject-environment=visible\nimage-specs=visible\nkdp-trim-presets=yes\ncustom-physical-dimensions=numeric-visible\ncustom-pixel-dimensions=numeric-visible\neditable-inputs=rendered-bordered\nimage-resolution-classes=HD,FHD,2K,4K,8K,PRINT,CUSTOM\nimage-resolution-preserves-aspect=yes\nimage-specs-in-prompt=yes\nimage-collection-color-modes=visible\nillustrated-book-shares-illustration-profile=yes\nillustrated-book-not-coloring=yes\nresolution-classes-all-visual-book-types=yes\nimage-intake-real-files=yes\nimage-intake-json=yes\ncorrection-base-image-real=yes\ncorrection-base-description=yes\ncorrection-full-image-presets=yes\nrequest-context-json=yes\nsafe-image-context-export=yes\nconsistent-off=criteria-hidden\nconsistent-on=criteria-visible\nconsistency-levels=3\nconsistency-free-strategies=USER,AI,MIXED\nconsistency-free-user=description-required\nconsistency-free-ai=description-optional\nconsistency-free-mixed=description-required\nbleed=image-generation-removed\nprompt-target-ai=visible\nprompt-target-catalog=central\nprompt-editors=3\nundo=ctrl-z\nredo=ctrl-y");
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
