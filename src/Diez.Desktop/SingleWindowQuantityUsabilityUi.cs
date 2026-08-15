using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Restores two user-facing affordances on the native visual 1/4 page: a persistent real-image preview
/// in the right preview host and an explicit, wheel-capable vertical scrollbar for the long form.
/// Scroll configuration is synchronous with Content assignment so it cannot be lost behind the classic
/// Win32 delayed-render layout turn; image decoding remains asynchronous.
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

            // The logical page tree already exists at Content assignment time even if Win32 has not yet
            // executed the queued physical layout. Configure the ScrollViewer now, then repeat at Loaded/Render
            // as an idempotent guard for wrappers or decorators added by later modules.
            ConfigureCurrentPage(pageHost, "content-change");
            Dispatcher.UIThread.Post(() =>
            {
                ConfigureCurrentPage(pageHost, "loaded");

                // On the affected classic-Win32 path Avalonia can finish measure/arrange while the HWND still
                // presents the previous page. Force a real window repaint only after the stable layout turn;
                // this does not reparent controls and does not manually Measure/Arrange anything.
                ForceWin32Frame(window, "page-content-loaded");

                _ = LoadPreviewAsync(window, pageHost, previewHost);
                Dispatcher.UIThread.Post(() =>
                {
                    ConfigureCurrentPage(pageHost, "render");
                    ForceWin32Frame(window, "page-content-render");
                }, DispatcherPriority.Render);
            }, DispatcherPriority.Loaded);
        };

        window.Closed += (_, _) =>
        {
            Attached.Remove(window);
            if (PreviewBitmaps.Remove(window, out var bitmap)) bitmap.Dispose();
        };

        ConfigureCurrentPage(pageHost, "attach");
        _ = LoadPreviewAsync(window, pageHost, previewHost);
    }

    private static ScrollViewer? ConfigureCurrentPage(ContentControl pageHost, string phase)
    {
        if (pageHost.Content is not Control page) return null;

        var scroll = page as ScrollViewer ?? Descendants(page).OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null) return null;

        // Quantity is the only native page whose ScrollViewer directly contains this named root. This check is
        // independent of physical Bounds so it works before the delayed classic Win32 layout pass.
        var quantityRoot = scroll.Content as Control;
        var isQuantity = string.Equals(quantityRoot?.Name, "DiezNativeV11QuantityPage", StringComparison.Ordinal) ||
                         Descendants(scroll).Any(c => string.Equals(c.Name, "DiezNativeV11QuantityPage", StringComparison.Ordinal));
        if (!isQuantity) return null;

        // These are input-ownership invariants for the active page, not cosmetic defaults. Keeping them explicit
        // prevents an inherited disabled/hit-test state from making the physically rendered 1/4 page inert.
        page.IsEnabled = true;
        page.IsHitTestVisible = true;
        if (quantityRoot is not null)
        {
            quantityRoot.IsEnabled = true;
            quantityRoot.IsHitTestVisible = true;
        }

        ConfigureScroll(scroll);
        SafeStartupTrace.Write(
            "quantity-scroll | phase=" + phase +
            " | vertical=" + scroll.VerticalScrollBarVisibility +
            " | enabled=" + scroll.IsEnabled +
            " | hitTest=" + scroll.IsHitTestVisible +
            " | bounds=" + scroll.Bounds +
            " | extent=" + scroll.Extent +
            " | viewport=" + scroll.Viewport);
        return scroll;
    }

    private static async Task LoadPreviewAsync(MainWindow window, ContentControl pageHost, ContentControl previewHost)
    {
        if (pageHost.Content is not Control page) return;
        var scroll = ConfigureCurrentPage(pageHost, "preview-start");
        if (scroll is null) return;

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

            AvaloniaLayoutPumpUi.Execute(window, "quantity-preview-content");
            ForceWin32Frame(window, "quantity-preview-content");
            SafeStartupTrace.Write(
                "quantity-usability | preview=image | file=" + imageMaterial.FileName +
                " | bytes=" + bytes.Length +
                " | previewHostBounds=" + previewHost.Bounds +
                " | scroll=configured");
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("quantity-usability | preview=decode-error | " + ex.GetBaseException().Message);
        }
    }

    private static void ConfigureScroll(ScrollViewer scroll)
    {
        // Do not assign Name here: at ContentChanged the control may already be styled, and Avalonia forbids
        // renaming a StyledElement at that point. The Quantity page is identified by its named content root.
        scroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Visible;
        scroll.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        scroll.IsEnabled = true;
        scroll.IsHitTestVisible = true;
        scroll.Focusable = true;

        if (!WiredScrollers.Add(scroll)) return;

        // Listen even when a child control or Avalonia's built-in ScrollViewer logic already marked the wheel
        // event handled. This is the installed-app failure mode: the form has a valid extent, but wheel input over
        // editors/combos never reaches the old fallback because it returned immediately on e.Handled.
        scroll.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
        {
            var handledBefore = e.Handled;
            var maxY = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            if (maxY <= 0) return;

            var nextY = Math.Clamp(scroll.Offset.Y - (e.Delta.Y * 72), 0, maxY);
            if (Math.Abs(nextY - scroll.Offset.Y) < 0.1) return;

            scroll.Offset = new Vector(scroll.Offset.X, nextY);
            e.Handled = true;
            SafeStartupTrace.Write(
                "quantity-scroll | wheel=true" +
                " | handledBefore=" + handledBefore +
                " | offsetY=" + nextY.ToString("0.##") +
                " | extent=" + scroll.Extent +
                " | viewport=" + scroll.Viewport);
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        SafeStartupTrace.Write(
            "quantity-scroll | configured=true | vertical=Visible | extent=" + scroll.Extent +
            " | viewport=" + scroll.Viewport);
    }

    internal static void ForceWin32Frame(MainWindow window, string reason)
    {
        // Keep Avalonia's own invalidation first. RedrawWindow is only a classic-Win32 presentation fallback
        // for the observed case where layout is current but the compositor still displays the previous page.
        window.InvalidateVisual();
        StableWorkflowRootUi.StableRoot(window)?.InvalidateVisual();
        StableWorkflowRootUi.WorkflowRoot(window)?.InvalidateVisual();
        StableWorkflowRootUi.HomeRoot(window)?.InvalidateVisual();

        if (!OperatingSystem.IsWindows()) return;
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            SafeStartupTrace.Write("win32-frame-refresh | reason=" + reason + " | hwnd=none | executed=false");
            return;
        }

        try
        {
            var flags = RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow | RdwFrame;
            var executed = RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, flags);
            SafeStartupTrace.Write(
                "win32-frame-refresh | reason=" + reason +
                " | hwnd=0x" + handle.ToInt64().ToString("X") +
                " | executed=" + executed);
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write(
                "win32-frame-refresh | reason=" + reason +
                " | error=" + ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message);
        }
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
                case ScrollViewer viewer when viewer.Content is Control child:
                    stack.Push(child);
                    break;
                case ContentControl content when content.Content is Control child:
                    stack.Push(child);
                    break;
            }
        }
    }

    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;
    private const uint RdwFrame = 0x0400;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr updateRect, IntPtr updateRegion, uint flags);
}
