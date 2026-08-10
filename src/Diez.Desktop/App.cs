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

            // SW-FLOW-4 owns the visible application shell. Legacy popup/tab modules
            // remain out of the visible path while the single-window workflow is tested.
            if (!StartupDiagnostics.TryAttach("Layout principale", () => FriendlyLayoutUi.Attach(mainWindow), out var layoutError) && layoutError is not null)
                failures.Add(layoutError);
            if (!StartupDiagnostics.TryAttach("Percorso libro a finestra unica", () => SingleWindowOverlayFlowUi.Attach(mainWindow), out var singleWindowError) && singleWindowError is not null)
                failures.Add(singleWindowError);
            if (!StartupDiagnostics.TryAttach("Ingresso visibile SW-FLOW-4", () => SingleWindowEntryPointUi.Attach(mainWindow), out var entryError) && entryError is not null)
                failures.Add(entryError);

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
                        await SingleWindowUiContractProbe.RunAsync(mainWindow);
                        File.WriteAllText(resultFile, "OK\nSW-FLOW-4\nquantity-field=visible\nprompt-editors=3\nundo=enabled");
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
