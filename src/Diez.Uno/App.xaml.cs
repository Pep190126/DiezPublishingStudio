using Microsoft.UI.Xaml;

namespace DiezPublishingStudio.UnoSpike;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }
    private bool _allowClose;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window
        {
            Title = "Diez Publishing Studio"
        };

        var shell = new MainShellPage();
        var polished = new DiezRound2PolishHost(new DiezUiPolishHost(shell));
        var publisher = new DiezPublisherShellHost(shell, polished);
        MainWindow.Content = publisher;
        MainWindow.Activate();

        MainWindow.AppWindow.Closing += async (_, eventArgs) =>
        {
            if (_allowClose) return;
            eventArgs.Cancel = true;
            if (!await publisher.ConfirmCloseAsync()) return;
            _allowClose = true;
            MainWindow.Close();
        };
    }
}
