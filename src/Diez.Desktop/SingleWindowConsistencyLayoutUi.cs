using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps the native Consistent section physically attached to the Quantity layout. On classic Win32
/// Avalonia, toggling the whole panel from IsVisible=false to true underneath the page ScrollViewer can
/// leave that panel at 0x0 even though the containing page is fully measured. OFF therefore collapses
/// the already-attached panel to zero height instead of removing it from layout participation; ON restores
/// automatic height and schedules normal Avalonia layout invalidation. No reparenting or Win32 repainting.
/// </summary>
internal static class SingleWindowConsistencyLayoutUi
{
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<CheckBox> Wired = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;

        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
            ?? throw new InvalidOperationException("PageHost non disponibile per il layout Consistent.");

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            Dispatcher.UIThread.Post(() => WireCurrentPage(pageHost), DispatcherPriority.Loaded);
        };

        window.Closed += (_, _) =>
        {
            Attached.Remove(window);
            Wired.Clear();
        };

        WireCurrentPage(pageHost);
    }

    private static void WireCurrentPage(ContentControl pageHost)
    {
        if (pageHost.Content is not Control page) return;

        var consistent = Descendants(page).OfType<CheckBox>().FirstOrDefault(c =>
            string.Equals(c.Name, "NativeConsistent", StringComparison.Ordinal));
        var panel = Descendants(page).FirstOrDefault(c =>
            string.Equals(c.Name, "DiezConsistencyCriteriaPanel", StringComparison.Ordinal));
        var notes = Descendants(page).OfType<TextBox>().FirstOrDefault(c =>
            string.Equals(c.Name, "ConsistencyNotes", StringComparison.Ordinal));

        if (consistent is null || panel is null || notes is null) return;

        void Apply() => ApplyState(pageHost, page, panel, notes, consistent.IsChecked == true);

        if (Wired.Add(consistent))
            consistent.IsCheckedChanged += (_, _) => Apply();

        // NativeConsistencyEditor may have constructed the panel with IsVisible=false before it was
        // attached to the page. Normalize that state immediately once the real page is mounted.
        Apply();
    }

    private static void ApplyState(
        ContentControl pageHost,
        Control page,
        Control panel,
        TextBox notes,
        bool enabled)
    {
        panel.IsVisible = true;
        panel.IsEnabled = enabled;
        panel.IsHitTestVisible = enabled;
        panel.Opacity = enabled ? 1d : 0d;
        panel.MinHeight = 0d;
        panel.Height = enabled ? double.NaN : 0d;
        panel.ClipToBounds = !enabled;

        InvalidateLayoutChain(panel);
        Invalidate(page);
        Invalidate(pageHost);
        Trace("state", enabled, pageHost, page, panel, notes);

        Dispatcher.UIThread.Post(() =>
        {
            InvalidateLayoutChain(panel);
            Invalidate(page);
            Invalidate(pageHost);
            Trace("loaded", enabled, pageHost, page, panel, notes);

            Dispatcher.UIThread.Post(() =>
            {
                InvalidateLayoutChain(panel);
                Invalidate(page);
                Invalidate(pageHost);
                Trace("render", enabled, pageHost, page, panel, notes);
            }, DispatcherPriority.Render);
        }, DispatcherPriority.Loaded);
    }

    private static void InvalidateLayoutChain(Control start)
    {
        Control? current = start;
        var seen = new HashSet<Control>();
        while (current is not null && seen.Add(current))
        {
            Invalidate(current);
            current = current.Parent as Control;
        }
    }

    private static void Invalidate(Control control)
    {
        control.InvalidateMeasure();
        control.InvalidateArrange();
        control.InvalidateVisual();
    }

    private static void Trace(
        string phase,
        bool enabled,
        ContentControl pageHost,
        Control page,
        Control panel,
        TextBox notes)
    {
        var panelParent = panel.Parent as Control;
        var notesParent = notes.Parent as Control;
        SafeStartupTrace.Write(
            "consistency-layout | phase=" + phase +
            " | enabled=" + enabled +
            " | panelVisible=" + panel.IsVisible +
            " | panelEnabled=" + panel.IsEnabled +
            " | panelHeight=" + (double.IsNaN(panel.Height) ? "Auto" : panel.Height.ToString("0.##")) +
            " | pageHostBounds=" + pageHost.Bounds +
            " | pageBounds=" + page.Bounds +
            " | panelParent=" + (panelParent?.GetType().Name ?? "<null>") +
            " | panelParentBounds=" + (panelParent?.Bounds.ToString() ?? "<null>") +
            " | panelBounds=" + panel.Bounds +
            " | notesParentBounds=" + (notesParent?.Bounds.ToString() ?? "<null>") +
            " | notesBounds=" + notes.Bounds);
    }

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
