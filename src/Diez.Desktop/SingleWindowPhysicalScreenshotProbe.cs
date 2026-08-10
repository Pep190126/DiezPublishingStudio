using System.Collections;
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
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
            ?? throw new InvalidOperationException("PageHost raster non disponibile.");
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
            if (pageHost.Content is Control quantity)
            {
                FindTextBox(quantity, "VisualSubjectInstructions").Text = "TEST PERSONAGGIO VISIBILE";
                FindTextBox(quantity, "VisualEnvironmentInstructions").Text = "TEST AMBIENTAZIONE VISIBILE";
            }
            await WaitAsync();
            await SaveWindowAsync(window, Path.Combine(AppContext.BaseDirectory, "ui-quantity.png"));

            if (pageHost.Content is Control consistencyPage)
            {
                var consistent = Descendants(consistencyPage).OfType<CheckBox>().FirstOrDefault(c => c.Name == "NativeConsistent")
                    ?? throw new InvalidOperationException("Consistent raster mancante.");
                consistent.IsChecked = true;
                await WaitAsync();

                var level = Descendants(consistencyPage).OfType<ComboBox>().FirstOrDefault(c => c.Name == "ConsistencyLevel_character")
                    ?? throw new InvalidOperationException("Criterio personaggio raster mancante.");
                if (level.ItemsSource is not IEnumerable items)
                    throw new InvalidOperationException("ItemsSource criterio personaggio non disponibile.");
                level.SelectedItem = items.Cast<object>().FirstOrDefault(x => string.Equals(x.ToString(), "Può variare", StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("Voce Può variare non disponibile nel raster.");
                await WaitAsync();

                FindTextBox(consistencyPage, "ConsistencyVariation_character").Text = "TEST VARIAZIONE VISIBILE: abiti e accessori possono cambiare.";
                FindTextBox(consistencyPage, "ConsistencyNotes").Text = "TEST NOTE CONSISTENT VISIBILI";
                await WaitAsync();

                if (pageHost.Content is ScrollViewer scroller)
                {
                    scroller.Offset = new Vector(0, 10000);
                    await WaitAsync();
                }
                await SaveWindowAsync(window, Path.Combine(AppContext.BaseDirectory, "ui-consistent.png"));
            }

            SingleWindowNativeV11Ui.ShowPrompt(window, host, 12);
            await WaitAsync();
            if (pageHost.Content is Control prompt)
            {
                FindTextBox(prompt, "MustDoEditor").Text = "TEST DEVE FARE VISIBILE";
                FindTextBox(prompt, "MustNotDoEditor").Text = "TEST NON DEVE FARE VISIBILE";
                FindTextBox(prompt, "PromptEditor").Text = "TEST PROMPT VISIBILE";
            }
            await WaitAsync();
            await SaveWindowAsync(window, Path.Combine(AppContext.BaseDirectory, "ui-prompt.png"));
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static TextBox FindTextBox(Control root, string name) =>
        Descendants(root).OfType<TextBox>().FirstOrDefault(box => box.Name == name)
        ?? throw new InvalidOperationException("TextBox raster mancante: " + name);

    private static IEnumerable<Control> Descendants(Control root)
    {
        var stack = new Stack<Control>();
        var seen = new HashSet<Control>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current)) continue;
            yield return current;
            switch (current)
            {
                case Panel panel:
                    for (var i = panel.Children.Count - 1; i >= 0; i--) stack.Push(panel.Children[i]);
                    break;
                case Border border when border.Child is Control child: stack.Push(child); break;
                case ScrollViewer scroll when scroll.Content is Control child: stack.Push(child); break;
                case ContentControl content when content.Content is Control child: stack.Push(child); break;
            }
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
