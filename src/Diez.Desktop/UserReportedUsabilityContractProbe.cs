using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Regression contract for the exact usability issues reported from the installed Windows app:
/// Home material visibility, editable/left-aligned initial book title, physical Home-project navigation,
/// real imported-image preview on Coloring 1/4 and an actually scrollable long Quantity page.
/// </summary>
internal static class UserReportedUsabilityContractProbe
{
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    public static async Task RunAsync(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost")
            ?? throw new InvalidOperationException("Usability contract: pageHost assente.");
        var previewHost = Field<ContentControl>(host, "_previewHost")
            ?? throw new InvalidOperationException("Usability contract: previewHost assente.");
        var materialsList = Field<ListBox>(window, "_materialsList")
            ?? throw new InvalidOperationException("Usability contract: box Materiali Home assente.");
        var status = Field<TextBlock>(window, "_status")
            ?? throw new InvalidOperationException("Usability contract: status Home assente.");

        var tempProject = Path.Combine(Path.GetTempPath(), "diez-user-usability-" + Guid.NewGuid().ToString("N") + ".diez");
        var tempImage = Path.Combine(Path.GetTempPath(), "diez-user-preview-" + Guid.NewGuid().ToString("N") + ".png");

        try
        {
            await File.WriteAllBytesAsync(tempImage, Convert.FromBase64String(TinyPngBase64));

            var project = ProjectFileStore.Create("Progetto Giungla");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            project.EditionMetadata.Title = string.Empty;
            var material = await MaterialImporter.ImportAsync(tempImage);
            project.Materials.Add(material);
            await ProjectFileStore.SaveAsync(tempProject, project);
            SetSession(window, project, tempProject);

            // Reproduce the post-import Home state rather than invoking RefreshViews directly: the usability
            // module listens to the same status transition produced by the owned Windows import dialog.
            StableWorkflowRootUi.ActivateHome(window);
            materialsList.ItemsSource = null;
            materialsList.SelectedIndex = -1;
            status.Text = "Importati 1 materiali · contract";
            await WaitAsync(window, "home-material-refresh");

            if (materialsList.ItemsSource is not IEnumerable materialItems ||
                !materialItems.Cast<object>().Any(item => (item?.ToString() ?? string.Empty).Contains(material.FileName, StringComparison.Ordinal)))
                throw new InvalidOperationException("Il materiale importato non compare nel box Materiali della Home.");
            if (materialsList.SelectedIndex != 0)
                throw new InvalidOperationException("Il materiale appena importato non viene selezionato nel box Materiali Home.");
            RequireBounds(materialsList, 120, 40, "box Materiali Home");

            var entry = FindHomeEntry(window);
            entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(window, "open-book-flow");

            if (!StableWorkflowRootUi.IsWorkflowActive(window))
                throw new InvalidOperationException("Percorso libro non attiva il Workflow stabile.");
            var typePage = pageHost.Content as Control
                ?? throw new InvalidOperationException("Pagina Tipo libro assente nel contract usability.");
            var title = Require<TextBox>(typePage, "DiezBookTitle");
            var frame = Require<Border>(typePage, "DiezBookTitleFrame");
            if (!string.Equals(title.Text, project.Name, StringComparison.Ordinal))
                throw new InvalidOperationException("Il Titolo del libro non parte dal nome del progetto.");
            if (title.IsReadOnly || !title.IsEnabled || !title.IsHitTestVisible)
                throw new InvalidOperationException("Il Titolo del libro iniziale non resta editabile.");
            if (title.TextAlignment != TextAlignment.Left || frame.HorizontalAlignment != HorizontalAlignment.Left)
                throw new InvalidOperationException("Il Titolo del libro non è allineato a sinistra con la label.");
            RequireBounds(title, 180, 26, "Titolo del libro");

            var homeProject = Descendants(StableWorkflowRootUi.WorkflowRoot(window) ?? throw new InvalidOperationException("Workflow root assente."))
                .OfType<Button>()
                .FirstOrDefault(b => string.Equals(b.Content?.ToString(), "Home progetto", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Pulsante Home progetto assente.");
            homeProject.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(window, "home-project-click");

            if (StableWorkflowRootUi.IsWorkflowActive(window))
                throw new InvalidOperationException("Home progetto non restituisce ownership alla Home nello stesso click.");
            var homeRoot = StableWorkflowRootUi.HomeRoot(window)
                ?? throw new InvalidOperationException("Home root assente dopo Home progetto.");
            if (!homeRoot.IsEnabled || !homeRoot.IsHitTestVisible || homeRoot.Opacity < 0.9)
                throw new InvalidOperationException("Home progetto torna a una Home non interattiva/visibile.");
            RequireBounds(materialsList, 120, 40, "box Materiali dopo Home progetto");

            // Re-enter exactly like the user and reach Coloring 1/4 again.
            entry = FindHomeEntry(window);
            entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(window, "reopen-book-flow");
            SingleWindowNativeV11Ui.ShowQuantity(window, host);
            await WaitAsync(window, "quantity-page", 420);

            var quantity = pageHost.Content as Control
                ?? throw new InvalidOperationException("Pagina Coloring 1/4 assente.");
            var scroll = quantity as ScrollViewer ?? Descendants(quantity).OfType<ScrollViewer>().FirstOrDefault()
                ?? throw new InvalidOperationException("ScrollViewer Coloring 1/4 assente.");
            RequireBounds(scroll, 180, 100, "ScrollViewer Coloring 1/4");
            if (scroll.VerticalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Visible)
                throw new InvalidOperationException("La scrollbar verticale Coloring 1/4 non è resa esplicitamente visibile.");
            if (scroll.Extent.Height <= scroll.Viewport.Height + 1)
                throw new InvalidOperationException($"Coloring 1/4 non risulta scrollabile: extent={scroll.Extent}, viewport={scroll.Viewport}.");

            var maxY = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            var targetY = Math.Min(maxY, 180);
            scroll.Offset = new Vector(scroll.Offset.X, targetY);
            await WaitAsync(window, "quantity-scroll-offset");
            if (targetY > 1 && scroll.Offset.Y < 1)
                throw new InvalidOperationException("Coloring 1/4 espone contenuto oltre il viewport ma l'offset verticale non cambia.");

            // The usability module asynchronously decodes the latest imported image into the permanent preview host.
            await WaitAsync(window, "quantity-image-preview", 500);
            var preview = previewHost.Content as Control
                ?? throw new InvalidOperationException("Preview Coloring 1/4 assente.");
            var image = Descendants(preview).OfType<Image>().FirstOrDefault(i => i.Source is not null)
                ?? throw new InvalidOperationException("L'immagine importata non compare più nell'anteprima Coloring 1/4.");
            RequireBounds(previewHost, 120, 100, "preview host Coloring 1/4");
            if (image.Source is null)
                throw new InvalidOperationException("Anteprima Coloring 1/4 senza sorgente immagine.");

            SafeStartupTrace.Write(
                "user-usability-contract | OK" +
                " | homeMaterials=" + project.Materials.Count +
                " | titleLeftEditable=true" +
                " | homeProject=true" +
                " | scrollExtent=" + scroll.Extent +
                " | scrollViewport=" + scroll.Viewport +
                " | previewImage=true");
        }
        finally
        {
            try { SingleWindowEntryPointUi.Invoke(host, "ShowHome"); } catch { }
            try { StableWorkflowRootUi.ActivateHome(window); } catch { }
            try { if (File.Exists(tempProject)) File.Delete(tempProject); } catch { }
            try { if (File.Exists(tempImage)) File.Delete(tempImage); } catch { }
        }
    }

    private static Button FindHomeEntry(MainWindow window)
    {
        var home = StableWorkflowRootUi.HomeRoot(window)
            ?? throw new InvalidOperationException("Home root non disponibile.");
        return Descendants(home).OfType<Button>().FirstOrDefault(b =>
                   string.Equals(b.Name, SingleWindowNativeEntryBridgeUi.NativeEntryName, StringComparison.Ordinal))
               ?? throw new InvalidOperationException("Pulsante Percorso libro stabile non disponibile.");
    }

    private static async Task WaitAsync(MainWindow window, string reason, int delayMs = 180)
    {
        await Task.Delay(delayMs);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        AvaloniaLayoutPumpUi.Execute(window, "contract-" + reason);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static void RequireBounds(Control control, double minWidth, double minHeight, string label)
    {
        if (control.Bounds.Width < minWidth || control.Bounds.Height < minHeight)
            throw new InvalidOperationException(
                $"Il controllo '{label}' non partecipa al layout fisico: {control.Bounds.Width:0.##} × {control.Bounds.Height:0.##}.");
    }

    private static T Require<T>(Control root, string name) where T : Control =>
        Descendants(root).OfType<T>().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Controllo {name} mancante.");

    private static void SetSession(MainWindow window, PreviewProject project, string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
        typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, path);
    }

    private static T? Field<T>(object owner, string name) where T : class =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) as T;

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
                case Border border when border.Child is Control child:
                    stack.Push(child);
                    break;
                case ScrollViewer viewer when viewer.Content is Control child:
                    stack.Push(child);
                    break;
                case ContentControl content when content.Content is Control child:
                    stack.Push(child);
                    break;
            }
        }
    }
}
