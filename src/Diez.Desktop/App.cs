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

            // SW-FLOW-5 is the visible application flow. FriendlyLayout only builds
            // the physical MainWindow grid; the logical workflow immediately covers it.
            if (!StartupDiagnostics.TryAttach("Layout principale", () => FriendlyLayoutUi.Attach(mainWindow), out var layoutError) && layoutError is not null)
                failures.Add(layoutError);
            if (!StartupDiagnostics.TryAttach("Host single-window", () => SingleWindowOverlayFlowUi.Attach(mainWindow), out var singleWindowError) && singleWindowError is not null)
                failures.Add(singleWindowError);
            if (!StartupDiagnostics.TryAttach("Avvio guidato SW-FLOW-5", () => SingleWindowV5StartupUi.Attach(mainWindow), out var startupError) && startupError is not null)
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
                            "OK\nSW-FLOW-5\nstartup=guided\nbook-type=visible\nquantity-field=visible\nprompt-editors=3\nundo=ctrl-z\nredo=ctrl-y");
                        desktop.Shutdown(0);
                    }
                    catch (Exception ex)
                    {
                        try { File.WriteAllText(resultFile, ex.ToString()); } catch { }
                        desktop.Shutdown(2);
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
