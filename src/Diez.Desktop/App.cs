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
            EditionWorkflowUi.Attach(mainWindow);
            HandoffWorkflowUi.Attach(mainWindow);
            ResponsiveLayoutUi.Attach(mainWindow);
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
