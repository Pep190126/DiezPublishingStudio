using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Avoids a classic Win32/Avalonia layout edge in the native Consistent section. Collapsing the whole
/// criteria StackPanel and later setting it visible can leave that subtree at 0x0 inside the Quantity
/// ScrollViewer. Keep the panel itself attached and visible; when Consistent is OFF, hide only its direct
/// children, remove its margin and disable input. ON restores exactly the child visibility that existed
/// before our collapse. No manual Measure/Arrange, no workflow reparenting and no Win32 repaint calls.
/// </summary>
internal static class SingleWindowConsistencyLayoutUi
{
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<CheckBox> Wired = [];
    private static readonly Dictionary<Panel, PanelState> States = [];

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
            States.Clear();
        };

        WireCurrentPage(pageHost);
    }

    private static void WireCurrentPage(ContentControl pageHost)
    {
        if (pageHost.Content is not Control page) return;

        var consistent = Descendants(page).OfType<CheckBox>().FirstOrDefault(c =>
            string.Equals(c.Name, "NativeConsistent", StringComparison.Ordinal));
        var panel = Descendants(page).OfType<Panel>().FirstOrDefault(c =>
            string.Equals(c.Name, "DiezConsistencyCriteriaPanel", StringComparison.Ordinal));
        var notes = Descendants(page).OfType<TextBox>().FirstOrDefault(c =>
            string.Equals(c.Name, "ConsistencyNotes", StringComparison.Ordinal));

        if (consistent is null || panel is null || notes is null) return;

        _ = StateFor(panel);

        if (Wired.Add(consistent))
        {
            // NativeConsistencyEditor registered first and may set Panel.IsVisible. Normalize immediately
            // afterwards so the panel itself never remains collapsed in the mounted Quantity page.
            consistent.IsCheckedChanged += (_, _) =>
                ApplyState(pageHost, page, panel, notes, consistent.IsChecked == true, "toggle");
        }

        ApplyState(pageHost, page, panel, notes, consistent.IsChecked == true, "wire");
    }

    private static void ApplyState(
        ContentControl pageHost,
        Control page,
        Panel panel,
        TextBox notes,
        bool enabled,
        string source)
    {
        var state = StateFor(panel);
        panel.IsVisible = true;
        panel.IsEnabled = enabled;
        panel.IsHitTestVisible = enabled;

        if (enabled)
        {
            panel.Margin = state.OriginalMargin;
            foreach (var child in panel.Children.OfType<Control>().ToList())
            {
                if (!state.HiddenByUs.Remove(child)) continue;
                if (state.VisibilityBeforeHide.TryGetValue(child, out var wasVisible))
                    child.IsVisible = wasVisible;
            }
        }
        else
        {
            panel.Margin = new Thickness(0);
            HideCurrentChildren(panel, state);
        }

        InvalidateLayoutChain(panel);
        Trace("state-" + source, enabled, pageHost, page, panel, notes, state);

        // Other Quantity decorators run from the same ContentProperty change. A second normal dispatcher
        // turn catches any direct child they add while Consistent is still OFF, without forcing layout.
        Dispatcher.UIThread.Post(() =>
        {
            if (!enabled) HideCurrentChildren(panel, state);
            InvalidateLayoutChain(panel);
            Trace("loaded", enabled, pageHost, page, panel, notes, state);

            Dispatcher.UIThread.Post(() =>
            {
                Trace("render", enabled, pageHost, page, panel, notes, state);
            }, DispatcherPriority.Render);
        }, DispatcherPriority.Loaded);
    }

    private static void HideCurrentChildren(Panel panel, PanelState state)
    {
        foreach (var child in panel.Children.OfType<Control>().ToList())
        {
            if (state.HiddenByUs.Add(child))
                state.VisibilityBeforeHide[child] = child.IsVisible;
            child.IsVisible = false;
        }
    }

    private static PanelState StateFor(Panel panel)
    {
        if (States.TryGetValue(panel, out var state)) return state;
        state = new PanelState(panel.Margin);
        States[panel] = state;
        return state;
    }

    private static void InvalidateLayoutChain(Control start)
    {
        Control? current = start;
        var seen = new HashSet<Control>();
        while (current is not null && seen.Add(current))
        {
            current.InvalidateMeasure();
            current.InvalidateArrange();
            current.InvalidateVisual();
            current = current.Parent as Control;
        }
    }

    private static void Trace(
        string phase,
        bool enabled,
        ContentControl pageHost,
        Control page,
        Panel panel,
        TextBox notes,
        PanelState state)
    {
        var panelParent = panel.Parent as Control;
        var notesParent = notes.Parent as Control;
        SafeStartupTrace.Write(
            "consistency-layout | phase=" + phase +
            " | enabled=" + enabled +
            " | panelVisible=" + panel.IsVisible +
            " | panelEnabled=" + panel.IsEnabled +
            " | hiddenChildren=" + state.HiddenByUs.Count +
            " | pageHostBounds=" + pageHost.Bounds +
            " | pageBounds=" + page.Bounds +
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

    private sealed class PanelState
    {
        public PanelState(Thickness originalMargin) => OriginalMargin = originalMargin;
        public Thickness OriginalMargin { get; }
        public Dictionary<Control, bool> VisibilityBeforeHide { get; } = [];
        public HashSet<Control> HiddenByUs { get; } = [];
    }
}
