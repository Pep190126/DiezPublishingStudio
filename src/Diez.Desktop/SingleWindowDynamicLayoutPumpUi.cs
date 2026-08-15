using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Classic Win32 can delay Avalonia's next-render layout callback after controls change IsVisible inside an
/// already mounted page. Wire only the known dynamic Consistency controls and drain Avalonia's queued layout
/// after their normal production handlers have changed visibility.
/// </summary>
internal static class SingleWindowDynamicLayoutPumpUi
{
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<CheckBox> WiredChecks = [];
    private static readonly HashSet<ComboBox> WiredLevels = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;

        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
            ?? throw new InvalidOperationException("PageHost non disponibile per il layout dinamico.");

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            Dispatcher.UIThread.Post(() => WireCurrentPage(window, pageHost), DispatcherPriority.Loaded);
        };

        window.Closed += (_, _) => Attached.Remove(window);
        WireCurrentPage(window, pageHost);
    }

    private static void WireCurrentPage(MainWindow window, ContentControl pageHost)
    {
        if (pageHost.Content is not Control page) return;
        if (!Descendants(page).Any(c => string.Equals(c.Name, "DiezNativeV11QuantityPage", StringComparison.Ordinal))) return;

        var consistent = Descendants(page).OfType<CheckBox>().FirstOrDefault(c =>
            string.Equals(c.Name, "NativeConsistent", StringComparison.Ordinal));
        if (consistent is not null && WiredChecks.Add(consistent))
        {
            consistent.IsCheckedChanged += (_, _) => Schedule(window, pageHost, "consistent-toggle");
        }

        foreach (var level in Descendants(page).OfType<ComboBox>().Where(c =>
                     (c.Name ?? string.Empty).StartsWith("ConsistencyLevel_", StringComparison.Ordinal)))
        {
            if (!WiredLevels.Add(level)) continue;
            level.SelectionChanged += (_, _) => Schedule(window, pageHost, "consistency-level-change");
        }

        SafeStartupTrace.Write(
            "dynamic-layout-pump | wired=true | consistent=" + (consistent is not null) +
            " | levels=" + WiredLevels.Count);
    }

    private static void Schedule(MainWindow window, ContentControl pageHost, string reason)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var executed = AvaloniaLayoutPumpUi.Execute(window, reason);
            var page = pageHost.Content as Control;
            var panel = page is null ? null : Descendants(page).FirstOrDefault(c => c.Name == "DiezConsistencyCriteriaPanel");
            var notes = page is null ? null : Descendants(page).OfType<TextBox>().FirstOrDefault(c => c.Name == "ConsistencyNotes");
            var variation = page is null ? null : Descendants(page).OfType<TextBox>().FirstOrDefault(c =>
                c.Name == "ConsistencyVariation_character");

            SafeStartupTrace.Write(
                "dynamic-layout-pump | reason=" + reason +
                " | executed=" + executed +
                " | pageBounds=" + (page?.Bounds.ToString() ?? "<none>") +
                " | panelBounds=" + (panel?.Bounds.ToString() ?? "<none>") +
                " | notesBounds=" + (notes?.Bounds.ToString() ?? "<none>") +
                " | variationBounds=" + (variation?.Bounds.ToString() ?? "<none>"));
        }, DispatcherPriority.Loaded);
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
