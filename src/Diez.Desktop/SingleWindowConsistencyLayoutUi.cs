using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Repairs one classic-desktop layout edge discovered by the physical flow probe. NativeConsistencyEditor
/// correctly toggles DiezConsistencyCriteriaPanel.IsVisible, but on classic Win32 the ScrollViewer can keep
/// the extent measured while the panel was collapsed. After NativeConsistent changes, this module forces a
/// fresh Avalonia measure/arrange of the current Quantity ScrollViewer only. It never reparents the workflow
/// and never calls Win32 repaint APIs.
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

        if (Wired.Add(consistent))
        {
            // NativeConsistencyEditor registered its handler when the page was constructed, before this
            // module sees the page. Its IsVisible change therefore happens first; we then remeasure the
            // already-mounted scroll surface using that new visibility state.
            consistent.IsCheckedChanged += (_, _) =>
                ScheduleLayout(pageHost, page, panel, notes, consistent.IsChecked == true);
        }

        if (consistent.IsChecked == true)
            ScheduleLayout(pageHost, page, panel, notes, enabled: true);
    }

    private static void ScheduleLayout(
        ContentControl pageHost,
        Control page,
        Control panel,
        TextBox notes,
        bool enabled)
    {
        InvalidateLayoutChain(panel);
        Invalidate(page);
        Invalidate(pageHost);
        Trace("state", enabled, pageHost, page, panel, notes);

        Dispatcher.UIThread.Post(() =>
        {
            ForceCurrentPageLayout(pageHost, page);
            Trace("forced-layout", enabled, pageHost, page, panel, notes);

            Dispatcher.UIThread.Post(() =>
            {
                Trace("render", enabled, pageHost, page, panel, notes);
            }, DispatcherPriority.Render);
        }, DispatcherPriority.Loaded);
    }

    private static void ForceCurrentPageLayout(ContentControl pageHost, Control page)
    {
        var width = pageHost.Bounds.Width;
        var height = pageHost.Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var viewport = new Size(width, height);
        page.InvalidateMeasure();
        page.InvalidateArrange();
        page.Measure(viewport);
        page.Arrange(new Rect(0, 0, width, height));
        page.InvalidateVisual();
        pageHost.InvalidateVisual();
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
