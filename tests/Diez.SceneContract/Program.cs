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
        return session.Dispatch(async () =>
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

                // This contract verifies the real control tree, events and persistence, not raster layout.
                // Avoid Window.Show(): it would enqueue render/layout work unrelated to the Scene behavior and
                // make Avalonia's isolated-session teardown depend on font/render services after the probe ends.
                await StructuredSceneUiContractProbeV2.RunAsync(window);
                Console.WriteLine("STRUCTURED SCENE CONTRACT: OK");
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
