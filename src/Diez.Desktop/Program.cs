using System.Text;
using Avalonia;
using Avalonia.Fonts.Inter;

namespace DiezPublishingStudio;

internal static class Program
{
    private const string AppMutexName = "DiezPublishingStudio.App";

    [STAThread]
    public static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                PackageSelfTest.RunAsync().GetAwaiter().GetResult();
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        using var mutex = new Mutex(true, AppMutexName, out var createdNew);
        if (!createdNew) return 0;

        var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        GC.KeepAlive(mutex);
        return exitCode;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
