using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace DiezPublishingStudio;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
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

            // SW-FLOW-9 is the visible application flow. FriendlyLayout only builds
            // the physical MainWindow grid; the logical workflow immediately covers it.
            if (!StartupDiagnostics.TryAttach("Layout principale", () => FriendlyLayoutUi.Attach(mainWindow), out var layoutError) && layoutError is not null)
                failures.Add(layoutError);
            if (!StartupDiagnostics.TryAttach("Host single-window", () => SingleWindowOverlayFlowUi.Attach(mainWindow), out var singleWindowError) && singleWindowError is not null)
                failures.Add(singleWindowError);
            if (!StartupDiagnostics.TryAttach("Profilo Coloring", () => SingleWindowColoringProfileUi.Attach(mainWindow), out var coloringProfileError) && coloringProfileError is not null)
                failures.Add(coloringProfileError);
            if (!StartupDiagnostics.TryAttach("Profilo illustrazioni", () => SingleWindowImageCollectionProfileUi.Attach(mainWindow), out var illustrationProfileError) && illustrationProfileError is not null)
                failures.Add(illustrationProfileError);
            if (!StartupDiagnostics.TryAttach("Identità Tipo libro visuale", () => SingleWindowVisualBookIdentityUi.Attach(mainWindow), out var visualIdentityError) && visualIdentityError is not null)
                failures.Add(visualIdentityError);
            if (!StartupDiagnostics.TryAttach("Specifiche immagini", () => SingleWindowImageSpecsUi.Attach(mainWindow), out var imageSpecsError) && imageSpecsError is not null)
                failures.Add(imageSpecsError);
            if (!StartupDiagnostics.TryAttach("Criteri Consistent", () => SingleWindowConsistencyCriteriaUi.Attach(mainWindow), out var consistencyError) && consistencyError is not null)
                failures.Add(consistencyError);
            if (!StartupDiagnostics.TryAttach("AI prompt specifico", () => SingleWindowPromptTargetAiUi.Attach(mainWindow), out var promptTargetError) && promptTargetError is not null)
                failures.Add(promptTargetError);
            if (!StartupDiagnostics.TryAttach("Avvio guidato SW-FLOW-9", () => SingleWindowV5StartupUi.Attach(mainWindow), out var startupError) && startupError is not null)
                failures.Add(startupError);

            mainWindow.Title = ProductInfo.WindowTitle;
            StartupDiagnostics.ShowWarning(mainWindow, failures);

            if (args.Any(a => string.Equals(a, "--ui-flow-contract", StringComparison.OrdinalIgnoreCase)))
            {
                mainWindow.Opened += async (_, _) =>
                {
                    var resultFile = Path.Combine(AppContext.BaseDirectory, "ui-flow-contract.txt");
                    try
                    {
                        if (File.Exists(resultFile)) File.Delete(resultFile);
                        await SingleWindowV5UiContractProbe.RunAsync(mainWindow);
                        File.WriteAllText(resultFile,
                            "OK\nSW-FLOW-9\nstartup=guided\nbook-type=visible\nquantity-field=visible\ncoloring-style=visible\ncoloring-profile=rich\ncoloring-binary-bw=fixed\nline-thickness=dropdown\nsubject-environment=visible\nimage-specs=visible\nimage-specs-in-prompt=yes\nimage-collection-color-modes=visible\nillustrated-book-shares-illustration-profile=yes\nillustrated-book-not-coloring=yes\nconsistent-off=criteria-hidden\nconsistent-on=criteria-visible\nconsistency-levels=3\nprompt-target-ai=visible\nprompt-target-catalog=central\nprompt-editors=3\nundo=ctrl-z\nredo=ctrl-y");
                        Environment.Exit(0);
                    }
                    catch (Exception ex)
                    {
                        try { File.WriteAllText(resultFile, ex.ToString()); } catch { }
                        Environment.Exit(2);
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
