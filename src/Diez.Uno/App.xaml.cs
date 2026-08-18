using Microsoft.UI.Xaml;

namespace DiezPublishingStudio.UnoSpike;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

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
        MainWindow.Content = new DiezRound2PolishHost(new DiezUiPolishHost(shell));
        MainWindow.Activate();
    }
}
