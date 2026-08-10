using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class SingleWindowPhysicalScreenshotProbe
{
    public static async Task RunAsync(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var tempPath = Path.Combine(Path.GetTempPath(), "diez-ui-raster-" + Guid.NewGuid().ToString("N") + ".diez");
        try
        {
            var project = ProjectFileStore.Create("Raster UI Contract");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            await ProjectFileStore.SaveAsync(tempPath, project);
            SetSession(window, project, tempPath);

            window.Width = 1400;
            window.Height = 900;
            window.Show();

            SingleWindowNativeV11Ui.ShowStart(window);
            await WaitAsync();
            SingleWindowNativeV11Ui.ShowQuantity(window, host);
            await WaitAsync();
            await SaveWindowAsync(window, Path.Combine(AppContext.BaseDirectory, "ui-quantity.png"));

            SingleWindowNativeV11Ui.ShowPrompt(window, host, 12);
            await WaitAsync();
            await SaveWindowAsync(window, Path.Combine(AppContext.BaseDirectory, "ui-prompt.png"));
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static async Task SaveWindowAsync(MainWindow window, string path)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var scale = window.RenderScaling <= 0 ? 1.0 : window.RenderScaling;
            var width = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Width * scale));
            var height = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Height * scale));
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
            bitmap.Render(window);
            bitmap.Save(path);
        }, DispatcherPriority.Render);
    }

    private static async Task WaitAsync()
    {
        await Task.Delay(180);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(80);
    }

    private static void SetSession(MainWindow window, PreviewProject project, string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
        typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, path);
    }
}
