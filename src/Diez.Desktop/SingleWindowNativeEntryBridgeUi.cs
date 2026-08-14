using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Owns the single production Home entry for SW-FLOW-12 and makes the active single-window overlay
/// the explicit pointer-input owner. Older workflow entry buttons remain present only as hidden legacy
/// controls; they must never compete with the native entry or with the active page for hit testing.
/// </summary>
internal static class SingleWindowNativeEntryBridgeUi
{
    internal const string NativeEntryName = "DiezNativeBookFlowEntry";
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<Button> WiredButtons = [];
    private static readonly Dictionary<MainWindow, Dictionary<Control, bool>> DesktopSiblingHitTestState = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        if (!TryCommandRow(window, out var row))
            throw new InvalidOperationException("Riga comandi progetto non disponibile per l'ingresso nativo.");

        foreach (var legacy in row.Children.OfType<Button>().Where(IsLegacyBookFlowEntry).ToList())
        {
            legacy.IsVisible = false;
            legacy.IsEnabled = false;
            legacy.IsHitTestVisible = false;
        }

        var entry = row.Children.OfType<Button>().FirstOrDefault(b => string.Equals(b.Name, NativeEntryName, StringComparison.Ordinal));
        if (entry is null)
        {
            entry = new Button
            {
                Name = NativeEntryName,
                Content = "Percorso libro",
                Width = 150,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            ToolTip.SetTip(entry, "Percorso nativo SW-FLOW-12 nella stessa MainWindow.");
            row.Children.Add(entry);
        }

        entry.IsVisible = true;
        entry.IsEnabled = true;
        entry.IsHitTestVisible = true;
        entry.Click += (_, _) => OpenNative(window);

        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost")
            ?? throw new InvalidOperationException("PageHost single-window non disponibile.");
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            Dispatcher.UIThread.Post(() =>
            {
                EnsureWorkflowInputOwnership(window, host, pageHost.Content is not null);
                TraceCurrentPage(window, host, pageHost);
            }, DispatcherPriority.Loaded);
        };

        window.AddHandler(InputElement.PointerPressedEvent, (_, e) => TracePointer(window, host, e),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        window.Closed += (_, _) =>
        {
            RestoreDesktopSiblingHitTesting(window);
            Attached.Remove(window);
        };
        SafeStartupTrace.Write("native-entry-bridge-attached | legacy-disabled=true | input-owner=workflow-overlay");
    }

    private static void OpenNative(MainWindow window)
    {
        SafeStartupTrace.Write(
            "ui-click | action=Percorso libro | route=native-v11" +
            " | windowEnabled=" + window.IsEnabled +
            " | active=" + window.IsActive);
        try
        {
            SingleWindowNativeV11Ui.ShowStart(window);
            var host = SingleWindowEntryPointUi.GetHost(window);
            EnsureWorkflowInputOwnership(window, host, active: true);
            SafeStartupTrace.Write("ui-navigation | target=native-v11-start | success=true");
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("ui-navigation | target=native-v11-start | error=" + ex);
            CrashDiagnostics.Error("native-book-flow-entry", ex);
        }
    }

    private static void EnsureWorkflowInputOwnership(MainWindow window, object host, bool active)
    {
        if (window.Content is not Border border || border.Child is not Grid desktop) return;
        var overlay = Field<Grid>(host, "_overlay");
        if (overlay is null) return;

        if (!active || !overlay.IsVisible)
        {
            RestoreDesktopSiblingHitTesting(window);
            SafeStartupTrace.Write("ui-input-owner | active=false | siblings-restored=true");
            return;
        }

        // Do not depend on insertion order: decorators can be attached after the host. The workflow is the
        // only full-window surface that may own pointer input while a logical page is active.
        overlay.ZIndex = 1000000;
        overlay.IsHitTestVisible = true;

        if (!DesktopSiblingHitTestState.TryGetValue(window, out var saved))
        {
            saved = [];
            DesktopSiblingHitTestState[window] = saved;
        }

        foreach (var sibling in desktop.Children.OfType<Control>())
        {
            if (ReferenceEquals(sibling, overlay)) continue;
            if (!saved.ContainsKey(sibling)) saved[sibling] = sibling.IsHitTestVisible;
            sibling.IsHitTestVisible = false;
        }

        SafeStartupTrace.Write(
            "ui-input-owner | active=true | overlayVisible=" + overlay.IsVisible +
            " | overlayHitTest=" + overlay.IsHitTestVisible +
            " | overlayZ=" + overlay.ZIndex +
            " | siblingCount=" + saved.Count);
    }

    private static void RestoreDesktopSiblingHitTesting(MainWindow window)
    {
        if (!DesktopSiblingHitTestState.Remove(window, out var saved)) return;
        foreach (var pair in saved)
        {
            try { pair.Key.IsHitTestVisible = pair.Value; } catch { }
        }
    }

    private static void TracePointer(MainWindow window, object host, PointerPressedEventArgs e)
    {
        try
        {
            var overlay = Field<Grid>(host, "_overlay");
            if (overlay?.IsVisible != true) return;
            var source = e.Source as Control;
            SafeStartupTrace.Write(
                "ui-pointer | event=pressed" +
                " | sourceType=" + (source?.GetType().FullName ?? e.Source?.GetType().FullName ?? "<null>") +
                " | sourceName=" + (source?.Name ?? "<unnamed>") +
                " | sourceEnabled=" + (source?.IsEnabled.ToString() ?? "<na>") +
                " | sourceHitTest=" + (source?.IsHitTestVisible.ToString() ?? "<na>") +
                " | overlayHitTest=" + overlay.IsHitTestVisible +
                " | overlayZ=" + overlay.ZIndex +
                " | windowEnabled=" + window.IsEnabled);
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("ui-pointer-trace-error | " + ex.GetBaseException().Message);
        }
    }

    private static void TraceCurrentPage(MainWindow window, object host, ContentControl pageHost)
    {
        try
        {
            var title = Field<TextBlock>(host, "_title")?.Text ?? "<untitled>";
            var page = pageHost.Content as Control;
            List<Button> buttons = page is null ? [] : Descendants(page).OfType<Button>().ToList();

            foreach (var button in buttons)
            {
                if (!WiredButtons.Add(button)) continue;
                button.AddHandler(Button.ClickEvent, (_, _) =>
                {
                    SafeStartupTrace.Write(
                        "ui-click | button=" + (button.Name ?? "<unnamed>") +
                        " | content=" + (button.Content?.ToString() ?? "<null>") +
                        " | enabled=" + button.IsEnabled +
                        " | hitTest=" + button.IsHitTestVisible +
                        " | windowEnabled=" + window.IsEnabled +
                        " | active=" + window.IsActive);
                }, RoutingStrategies.Bubble, handledEventsToo: true);
            }

            SafeStartupTrace.Write(
                "ui-page | title=" + title +
                " | windowEnabled=" + window.IsEnabled +
                " | active=" + window.IsActive +
                " | buttons=" + string.Join(";", buttons.Select(b =>
                    (b.Name ?? "<unnamed>") + ":" + (b.Content?.ToString() ?? "<null>") +
                    ":enabled=" + b.IsEnabled + ":hitTest=" + b.IsHitTestVisible + ":visible=" + b.IsVisible)));
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("ui-page-trace-error | " + ex.GetBaseException().Message);
        }
    }

    private static bool IsLegacyBookFlowEntry(Button button)
    {
        if (string.Equals(button.Name, NativeEntryName, StringComparison.Ordinal)) return false;
        var text = button.Content?.ToString() ?? string.Empty;
        return string.Equals(text, "Percorso libro", StringComparison.Ordinal) ||
               text.Contains("Percorso libro · SW-FLOW-", StringComparison.Ordinal);
    }

    private static bool TryCommandRow(MainWindow window, out StackPanel row)
    {
        row = null!;
        if (window.Content is not Border border || border.Child is not Grid desktop) return false;
        var header = desktop.Children.OfType<Grid>().FirstOrDefault(c => Grid.GetRow(c) == 0);
        if (header is null) return false;
        row = header.Children.OfType<StackPanel>().FirstOrDefault(p =>
            p.Orientation == Orientation.Horizontal &&
            p.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.OrdinalIgnoreCase)))!;
        return row is not null;
    }

    private static T? Field<T>(object host, string name) where T : class =>
        host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as T;

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
}
