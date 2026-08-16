using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DiezPublishingStudio.UnoSpike;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window();
        var frame = new Frame();
        frame.NavigationFailed += OnNavigationFailed;
        _window.Content = frame;
        frame.Navigate(typeof(HomePage));
        _window.Activate();
    }

    private static void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException($"Navigation failed: {e.SourcePageType.FullName}", e.Exception);
    }
}
