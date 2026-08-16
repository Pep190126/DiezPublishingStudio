using Avalonia;
using Avalonia.Fonts.Inter;
using Avalonia.Headless;
using DiezPublishingStudio;

namespace Diez.SceneContract;

public static class Program
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .LogToTrace();

    public static int Main()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(Program));
        return session.Dispatch(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow(null);
                FriendlyLayoutUi.Attach(window);
                SingleWindowOverlayFlowUi.Attach(window);
                SingleWindowNativeV11Ui.Attach(window);
                SingleWindowColoringStylePolicyUi.Attach(window);
                SingleWindowSubjectStyleUi.Attach(window);
                SingleWindowMultiSubjectLabelUi.Attach(window);
                SingleWindowStructuredSceneUi.Attach(window);

                StructuredSceneUiContractProbeV3.Run(window);
                Console.WriteLine("STRUCTURED SCENE CONTRACT V3: OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 2;
            }
            finally
            {
                try { window?.Close(); } catch { }
            }
        }, CancellationToken.None).GetAwaiter().GetResult();
    }
}
