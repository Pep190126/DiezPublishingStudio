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
            var startupProjectPath = desktop.Args?
                .FirstOrDefault(a => a.EndsWith(".diez", StringComparison.OrdinalIgnoreCase));
            var mainWindow = new MainWindow(startupProjectPath);
            desktop.MainWindow = mainWindow;

            var failures = new List<string>();

            // SW-FLOW-3 deliberately owns the visible application shell.
            // Legacy popup/tab UI modules are not attached here: their services remain
            // available to the logical pages, but they must not replace or compete with
            // the single physical MainWindow while this vertical slice is validated.
            if (!StartupDiagnostics.TryAttach("Layout principale", () => FriendlyLayoutUi.Attach(mainWindow), out var layoutError) && layoutError is not null)
                failures.Add(layoutError);
            if (!StartupDiagnostics.TryAttach("Percorso libro a finestra unica", () => SingleWindowOverlayFlowUi.Attach(mainWindow), out var singleWindowError) && singleWindowError is not null)
                failures.Add(singleWindowError);

            mainWindow.Title = ProductInfo.WindowTitle;
            StartupDiagnostics.ShowWarning(mainWindow, failures);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
