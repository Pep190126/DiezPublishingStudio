using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Restores two user-facing affordances on the native visual 1/4 page: a persistent real-image preview
/// in the right preview host and an explicit, wheel-capable vertical scrollbar for the long form.
/// </summary>
internal static class SingleWindowQuantityUsabilityUi
{
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<ScrollViewer> WiredScrollers = [];
    private static readonly Dictionary<MainWindow, Bitmap> PreviewBitmaps = [];
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"
    };

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;

        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost")
            ?? throw new InvalidOperationException("PageHost non disponibile per l'usabilità Quantità.");
        var previewHost = Field<ContentControl>(host, "_previewHost")
            ?? throw new InvalidOperationException("PreviewHost non disponibile per l'usabilità Quantità.");

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            Dispatcher.UIThread.Post(() => _ = ApplyAsync(window, pageHost, previewHost), DispatcherPriority.Loaded);
        };

        window.Closed += (_, _) =>
        {
            Attached.Remove(window);
            if (PreviewBitmaps.Remove(window, out var bitmap)) bitmap.Dispose();
        };

        _ = ApplyAsync(window, pageHost, previewHost);
    }

    private static async Task ApplyAsync(MainWindow window, ContentControl pageHost, ContentControl previewHost)
    {
        if (pageHost.Content is not Control page) return;
        if (!Descendants(page).Any(c => string.Equals(c.Name, "DiezNativeV11QuantityPage", StringComparison.Ordinal))) return;

        var scroll = page as ScrollViewer ?? Descendants(page).OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is not null) ConfigureScroll(scroll);

        var project = Field<PreviewProject>(window, "_project");
        var path = Field<string>(window, "_currentProjectPath");
        if (project is null || string.IsNullOrWhiteSpace(path)) return;

        var imageMaterial = project.Materials.LastOrDefault(IsImageMaterial);
        if (imageMaterial is null)
        {
            SafeStartupTrace.Write("quantity-usability | preview=none | reason=no-image-material");
            return;
        }

        byte[]? bytes = null;
        try
        {
            bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(path, imageMaterial);
            if ((bytes is null || bytes.Length == 0) && !string.IsNullOrWhiteSpace(imageMaterial.SourcePath) && File.Exists(imageMaterial.SourcePath))
                bytes = await File.ReadAllBytesAsync(imageMaterial.SourcePath);
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("quantity-usability | preview=load-error | " + ex.GetBaseException().Message);
        }

        if (bytes is null || bytes.Length == 0 || !ReferenceEquals(pageHost.Content, page)) return;

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new Bitmap(stream);
            if (PreviewBitmaps.Remove(window, out var previous)) previous.Dispose();
            PreviewBitmaps[window] = bitmap;

            previewHost.Content = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto,Auto"),
                RowSpacing = 7,
                Children =
                {
                    new Border
                    {
                        MinHeight = 240,
                        Child = new Image
                        {
                            Source = bitmap,
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
                        }
                    },
                    new TextBlock
                    {
                        Text = imageMaterial.FileName,
                        FontSize = 14,
                        TextWrapping = TextWrapping.Wrap
                    }.WithGridRow(1),
                    new TextBlock
                    {
                        Text = "Anteprima del materiale immagine importato. Usa i campi della pagina per valutarlo e correggere soggetto, ambientazione, stile e vincoli prima di proseguire.",
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap
                    }.WithGridRow(2)
                }
            };

            SafeStartupTrace.Write(
                "quantity-usability | preview=image | file=" + imageMaterial.FileName +
                " | bytes=" + bytes.Length +
                " | scroll=" + (scroll is null ? "missing" : "configured"));
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("quantity-usability | preview=decode-error | " + ex.GetBaseException().Message);
        }
    }

    private static void ConfigureScroll(ScrollViewer scroll)
    {
        scroll.Name ??= "DiezNativeQuantityScroll";
        scroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Visible;
        scroll.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        scroll.IsHitTestVisible = true;
        scroll.Focusable = true;

        if (!WiredScrollers.Add(scroll)) return;

        scroll.PointerWheelChanged += (_, e) =>
        {
            if (e.Handled) return;
            var maxY = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            if (maxY <= 0) return;

            var nextY = Math.Clamp(scroll.Offset.Y - (e.Delta.Y * 72), 0, maxY);
            if (Math.Abs(nextY - scroll.Offset.Y) < 0.1) return;

            scroll.Offset = new Vector(scroll.Offset.X, nextY);
            e.Handled = true;
            SafeStartupTrace.Write(
                "quantity-scroll | offsetY=" + nextY.ToString("0.##") +
                " | extent=" + scroll.Extent +
                " | viewport=" + scroll.Viewport);
        };

        SafeStartupTrace.Write(
            "quantity-scroll | configured=true | vertical=Visible | extent=" + scroll.Extent +
            " | viewport=" + scroll.Viewport);
    }

    private static bool IsImageMaterial(MaterialEntry material) =>
        ImageExtensions.Contains(Path.GetExtension(material.FileName ?? string.Empty));

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
                case ScrollViewer scroll when scroll.Content is Control child:
                    stack.Push(child);
                    break;
                case ContentControl content when content.Content is Control child:
                    stack.Push(child);
                    break;
            }
        }
    }
}
