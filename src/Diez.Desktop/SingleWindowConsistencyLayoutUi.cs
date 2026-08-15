using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Repairs one classic-desktop layout edge discovered by the physical flow probe: the Consistent panel
/// is created collapsed and, on some Win32 layout turns, making it visible does not immediately remeasure
/// its nested native editors. This module only invalidates the already-mounted Quantity visual tree after
/// NativeConsistent changes; it does not reparent controls or force native window repainting.
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
            consistent.IsCheckedChanged += (_, _) =>
            {
                ScheduleLayout(pageHost, page, panel, notes, consistent.IsChecked == true);
            };
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
        InvalidateLayoutChain(notes);
        InvalidateLayoutChain(panel);
        Invalidate(page);
        Invalidate(pageHost);

        Dispatcher.UIThread.Post(() =>
        {
            InvalidateLayoutChain(notes);
            InvalidateLayoutChain(panel);
            Invalidate(page);
            Invalidate(pageHost);
            Trace("loaded", enabled, pageHost, panel, notes);

            Dispatcher.UIThread.Post(() =>
            {
                InvalidateLayoutChain(notes);
                InvalidateLayoutChain(panel);
                Invalidate(page);
                Invalidate(pageHost);
                Trace("render", enabled, pageHost, panel, notes);
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

    private static void Trace(string phase, bool enabled, ContentControl pageHost, Control panel, TextBox notes)
    {
        SafeStartupTrace.Write(
            "consistency-layout | phase=" + phase +
            " | enabled=" + enabled +
            " | panelVisible=" + panel.IsVisible +
            " | pageHostBounds=" + pageHost.Bounds +
            " | panelBounds=" + panel.Bounds +
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
