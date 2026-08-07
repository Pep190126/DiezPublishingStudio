using Avalonia;
using Avalonia.Fonts.Inter;

namespace DiezPublishingStudio;

internal static class Program
{
    private const string AppMutexName = "DiezPublishingStudio.App";

    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, AppMutexName, out var createdNew);
        if (!createdNew)
            return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        GC.KeepAlive(mutex);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
