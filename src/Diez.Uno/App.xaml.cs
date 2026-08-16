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
            Title = "Diez Publishing Studio — Uno Platform"
        };
        MainWindow.Content = new MainShellPage();
        MainWindow.Activate();
    }
}
