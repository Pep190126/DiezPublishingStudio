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
            if (!StartupDiagnostics.TryAttach("Prepara consegna", () => EditionWorkflowUi.Attach(mainWindow), out var editionError) && editionError is not null)
                failures.Add(editionError);
            if (!StartupDiagnostics.TryAttach("Esporta / Consegna", () => HandoffWorkflowUi.Attach(mainWindow), out var handoffError) && handoffError is not null)
                failures.Add(handoffError);
            if (!StartupDiagnostics.TryAttach("Layout e aiuto contestuale", () => FriendlyLayoutUi.Attach(mainWindow), out var layoutError) && layoutError is not null)
                failures.Add(layoutError);

            StartupDiagnostics.ShowWarning(mainWindow, failures);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
