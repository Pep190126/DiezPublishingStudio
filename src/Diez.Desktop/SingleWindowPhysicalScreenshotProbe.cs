using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
        var evidenceDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "ui-evidence"));
        Directory.CreateDirectory(evidenceDirectory);
        try
        {
            var project = ProjectFileStore.Create("Raster UI Contract");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            await ProjectFileStore.SaveAsync(tempPath, project);
            SetSession(window, project, tempPath);

            window.Width = 1400;
            window.Height = 900;
            window.Show();
            await WaitAsync();

            var entry = Descendants(window).OfType<Button>().FirstOrDefault(b =>
                string.Equals(b.Name, SingleWindowNativeEntryBridgeUi.NativeEntryName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("Ingresso Percorso libro nativo raster mancante.");
            if (!entry.IsVisible || !entry.IsEnabled || !entry.IsHitTestVisible)
                throw new InvalidOperationException("Ingresso Percorso libro raster non operativo.");

            entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync();

            if (pageHost.Content is not Control bookType)
                throw new InvalidOperationException("Pagina Tipo libro raster non materializzata.");
            AssertBookTypePhysicallyMounted(window, host, pageHost, bookType);
            await SaveWindowAsync(window, Path.Combine(evidenceDirectory, "ui-book-type.png"));

            SingleWindowNativeV11Ui.ShowQuantity(window, host);
            await WaitAsync();
            if (pageHost.Content is Control quantity)
            {
                FindTextBox(quantity, "VisualSubjectInstructions").Text = "TEST PERSONAGGIO VISIBILE";
                FindTextBox(quantity, "VisualEnvironmentInstructions").Text = "TEST AMBIENTAZIONE VISIBILE";
            }
            await WaitAsync();
            AssertWorkflowRootMounted(window, host, pageHost, "Quantità");
            await SaveWindowAsync(window, Path.Combine(evidenceDirectory, "ui-quantity.png"));

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

                var variation = FindTextBox(consistencyPage, "ConsistencyVariation_character");
                var notes = FindTextBox(consistencyPage, "ConsistencyNotes");
                variation.Text = "TEST VARIAZIONE VISIBILE: abiti e accessori possono cambiare.";
                notes.Text = "TEST NOTE CONSISTENT VISIBILI";
                await WaitAsync();

                BringBridgeIntoView(variation);
                await WaitAsync();
                await SaveWindowAsync(window, Path.Combine(evidenceDirectory, "ui-consistent-variation.png"));

                BringBridgeIntoView(notes);
                await WaitAsync();
                await SaveWindowAsync(window, Path.Combine(evidenceDirectory, "ui-consistent-notes.png"));
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
            AssertWorkflowRootMounted(window, host, pageHost, "Istruzioni");
            await SaveWindowAsync(window, Path.Combine(evidenceDirectory, "ui-prompt.png"));
        }
        finally
        {
            try { SingleWindowEntryPointUi.Invoke(host, "ShowHome"); } catch { }
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static void AssertBookTypePhysicallyMounted(MainWindow window, object host, ContentControl pageHost, Control page)
    {
        AssertWorkflowRootMounted(window, host, pageHost, "Tipo libro");

        var frame = Descendants(page).OfType<Border>().FirstOrDefault(b => b.Name == "DiezBookTitleFrame")
            ?? throw new InvalidOperationException("Cornice visibile Titolo del libro raster mancante.");
        var title = Descendants(page).OfType<TextBox>().FirstOrDefault(t => t.Name == "DiezBookTitle")
            ?? throw new InvalidOperationException("Campo Titolo del libro raster mancante.");
        var button = Descendants(page).OfType<Button>().FirstOrDefault(b => b.Name == "DiezNativeBookTypeApply")
            ?? throw new InvalidOperationException("Pulsante Tipo libro raster mancante.");

        if (frame.BorderThickness.Left < 1 || frame.BorderBrush is null)
            throw new InvalidOperationException("La cornice Titolo del libro non ha un bordo visibile esplicito.");
        RequireBounds(page, 120, 60, "pagina Tipo libro");
        RequireBounds(frame, 160, 30, "cornice Titolo del libro");
        RequireBounds(title, 150, 26, "campo Titolo del libro");
        RequireBounds(button, 80, 20, "pulsante Usa questo Tipo libro");
    }

    private static void AssertWorkflowRootMounted(MainWindow window, object host, ContentControl pageHost, string stage)
    {
        var overlay = host.GetType().GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as Grid
            ?? throw new InvalidOperationException("Workflow root raster non disponibile durante " + stage + ".");
        var stableRoot = StableWorkflowRootUi.StableRoot(window)
            ?? throw new InvalidOperationException("Radice stabile raster non disponibile durante " + stage + ".");
        var homeRoot = StableWorkflowRootUi.HomeRoot(window)
            ?? throw new InvalidOperationException("Home root raster non disponibile durante " + stage + ".");

        if (!StableWorkflowRootUi.IsWorkflowActive(window))
            throw new InvalidOperationException("Workflow non attivo nella radice stabile durante " + stage + ".");
        if (window.Content is not Border border || !ReferenceEquals(border.Child, stableRoot))
            throw new InvalidOperationException("MainWindow non conserva la radice stabile durante " + stage + ".");
        if (!ReferenceEquals(overlay.Parent, stableRoot) || !ReferenceEquals(homeRoot.Parent, stableRoot))
            throw new InvalidOperationException("Home e Workflow non sono entrambi parented alla radice stabile durante " + stage + ".");
        if (homeRoot.IsEnabled || homeRoot.IsHitTestVisible || !overlay.IsEnabled || !overlay.IsHitTestVisible)
            throw new InvalidOperationException("Ownership input stabile errata durante " + stage + ".");

        RequireBounds(stableRoot, 200, 150, "stable root " + stage);
        RequireBounds(overlay, 200, 150, "workflow root " + stage);
        RequireBounds(homeRoot, 200, 150, "home root mantenuto " + stage);
        RequireBounds(pageHost, 100, 60, "pageHost " + stage);
        if (pageHost.Content is Control page)
            RequireBounds(page, 100, 50, "pagina " + stage);
    }

    private static void RequireBounds(Control control, double minWidth, double minHeight, string label)
    {
        if (control.Bounds.Width < minWidth || control.Bounds.Height < minHeight)
            throw new InvalidOperationException(
                $"Il controllo '{label}' non partecipa al layout fisico: {control.Bounds.Width:0.##} × {control.Bounds.Height:0.##}.");
    }

    private static void BringBridgeIntoView(TextBox source)
    {
        if (source.Parent is Control bridgeHost) bridgeHost.BringIntoView();
        else source.BringIntoView();
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
