using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Native Percorso libro entry for the permanent-root architecture. It never replaces Border.Child.
/// The existing SingleWindowOverlayFlowHost still owns page/history/Home semantics; this bridge only
/// selects which permanently parented surface owns input and keeps the native page/preview layout rules.
/// </summary>
internal static class SingleWindowStableEntryBridgeUi
{
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<Button> WiredButtons = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        if (!StableWorkflowRootUi.IsInstalled(window))
            throw new InvalidOperationException("Radice visuale stabile non installata prima dell'ingresso nativo.");

        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost")
            ?? throw new InvalidOperationException("PageHost single-window non disponibile.");
        var overlay = StableWorkflowRootUi.WorkflowRoot(window)
            ?? throw new InvalidOperationException("Workflow root stabile non disponibile.");
        var row = FindProjectRow(window)
            ?? throw new InvalidOperationException("Riga comandi progetto non disponibile per l'ingresso nativo stabile.");

        foreach (var legacy in row.Children.OfType<Button>().Where(button =>
                     !string.Equals(button.Name, SingleWindowNativeEntryBridgeUi.NativeEntryName, StringComparison.Ordinal) &&
                     string.Equals(button.Content?.ToString(), "Percorso libro", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            legacy.IsVisible = false;
            legacy.IsEnabled = false;
            legacy.IsHitTestVisible = false;
        }

        var entry = row.Children.OfType<Button>().FirstOrDefault(button =>
            string.Equals(button.Name, SingleWindowNativeEntryBridgeUi.NativeEntryName, StringComparison.Ordinal));
        if (entry is null)
        {
            entry = new Button
            {
                Name = SingleWindowNativeEntryBridgeUi.NativeEntryName,
                Content = "Percorso libro",
                Width = 150,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            row.Children.Add(entry);
        }

        entry.IsVisible = true;
        entry.IsEnabled = true;
        entry.IsHitTestVisible = true;
        ToolTip.SetTip(entry, "Percorso libro nativo nella stessa MainWindow, senza cambio della radice visuale.");
        entry.Click += (_, _) => OpenNative(window);

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            if (pageHost.Content is not null)
            {
                StableWorkflowRootUi.ActivateWorkflow(window);
                ConfigureWorkflowSurface(host, overlay);
                TraceCurrentPage(window, host, pageHost);
                Dispatcher.UIThread.Post(() => TraceMountedLayout(window, pageHost), DispatcherPriority.Render);
            }
            else
            {
                Dispatcher.UIThread.Post(() => StableWorkflowRootUi.ActivateHome(window), DispatcherPriority.Loaded);
            }
        };

        window.AddHandler(InputElement.PointerPressedEvent, (_, e) => TracePointer(window, pageHost, e),
            RoutingStrategies.Tunnel, handledEventsToo: true);

        window.Closed += (_, _) => Attached.Remove(window);
        SafeStartupTrace.Write(
            "native-entry-stable-bridge-attached | legacy-disabled=true | input-owner=stable-root | runtime-root-swap=false | page-span=stable-one-column");
    }

    private static void OpenNative(MainWindow window)
    {
        SafeStartupTrace.Write(
            "ui-click | action=Percorso libro | route=native-v11 | stableRoot=true" +
            " | windowEnabled=" + window.IsEnabled +
            " | active=" + window.IsActive);
        try
        {
            StableWorkflowRootUi.ActivateWorkflow(window);
            SingleWindowNativeV11Ui.ShowStart(window);
            SafeStartupTrace.Write("ui-navigation | target=native-v11-start | success=true | stableRoot=true | rootSwap=false");
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("ui-navigation | target=native-v11-start | error=" + ex);
            CrashDiagnostics.Error("native-book-flow-stable-entry", ex);
        }
    }

    private static void ConfigureWorkflowSurface(object host, Grid overlay)
    {
        var pageHost = Field<ContentControl>(host, "_pageHost");
        var previewHost = Field<ContentControl>(host, "_previewHost");
        if (pageHost is null || previewHost is null) return;

        pageHost.IsHitTestVisible = true;
        previewHost.IsHitTestVisible = false;

        var body = overlay.Children.OfType<Grid>().FirstOrDefault(grid => Grid.GetRow(grid) == 1);
        if (body is null) return;
        var pageSurface = body.Children.OfType<Border>().FirstOrDefault(surface => ReferenceEquals(surface.Child, pageHost));
        var previewSurface = body.Children.OfType<Border>().FirstOrDefault(surface =>
            !ReferenceEquals(surface, pageSurface) && surface.Child is Control child && Contains(child, previewHost));
        if (pageSurface is null || previewSurface is null) return;

        body.Background = Brushes.White;
        body.ClipToBounds = true;
        pageSurface.ZIndex = 1;
        pageSurface.IsHitTestVisible = true;
        previewSurface.ZIndex = 0;
        previewSurface.IsHitTestVisible = false;

        var title = Field<TextBlock>(host, "_title")?.Text ?? string.Empty;
        var bookTypePage = string.Equals(title, "Tipo libro", StringComparison.Ordinal);
        previewSurface.IsVisible = !bookTypePage;

        // Keep the interactive page surface in one invariant grid slot. The installed Windows path and the
        // physical CI hit-test both proved that changing ColumnSpan at runtime can update rendering while the
        // input map remains on the old geometry. Hiding the preview must not reshape the page hit-test tree.
        Grid.SetColumn(pageSurface, 0);
        Grid.SetColumnSpan(pageSurface, 1);

        SafeStartupTrace.Write(
            "ui-surface-layout | title=" + (string.IsNullOrWhiteSpace(title) ? "<untitled>" : title) +
            " | stableRoot=true | bodyBounds=" + body.Bounds +
            " | pageBounds=" + pageSurface.Bounds +
            " | pageColumnSpan=" + Grid.GetColumnSpan(pageSurface) +
            " | previewVisible=" + previewSurface.IsVisible +
            " | inputGeometry=stable");
    }

    private static void TraceCurrentPage(MainWindow window, object host, ContentControl pageHost)
    {
        var title = Field<TextBlock>(host, "_title")?.Text ?? "<untitled>";
        var page = pageHost.Content as Control;
        var buttons = page is null ? new List<Button>() : Descendants(page).OfType<Button>().ToList();
        foreach (var button in buttons)
        {
            if (!WiredButtons.Add(button)) continue;
            button.AddHandler(Button.ClickEvent, (_, _) =>
            {
                SafeStartupTrace.Write(
                    "ui-click | button=" + (button.Name ?? "<unnamed>") +
                    " | content=" + (button.Content?.ToString() ?? "<null>") +
                    " | enabled=" + button.IsEnabled +
                    " | bounds=" + button.Bounds +
                    " | stableRoot=true");
            }, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        SafeStartupTrace.Write(
            "ui-page | title=" + title +
            " | stableRoot=true | workflowActive=" + StableWorkflowRootUi.IsWorkflowActive(window) +
            " | pageHostBounds=" + pageHost.Bounds +
            " | pageBounds=" + (page?.Bounds.ToString() ?? "<none>") +
            " | buttons=" + string.Join(";", buttons.Select(button =>
                (button.Name ?? "<unnamed>") + ":" + (button.Content?.ToString() ?? "<null>") +
                ":enabled=" + button.IsEnabled + ":visible=" + button.IsVisible + ":bounds=" + button.Bounds)));
    }

    private static void TraceMountedLayout(MainWindow window, ContentControl pageHost)
    {
        var page = pageHost.Content as Control;
        SafeStartupTrace.Write(
            "ui-layout-after-render | stableRoot=true" +
            " | workflowActive=" + StableWorkflowRootUi.IsWorkflowActive(window) +
            " | pageHostBounds=" + pageHost.Bounds +
            " | pageBounds=" + (page?.Bounds.ToString() ?? "<none>"));
    }

    private static void TracePointer(MainWindow window, ContentControl pageHost, PointerPressedEventArgs e)
    {
        if (!StableWorkflowRootUi.IsWorkflowActive(window)) return;
        var source = e.Source as Control;
        var page = pageHost.Content as Control;
        var pointerOver = page is null
            ? []
            : Descendants(page).OfType<Button>().Where(button => button.IsPointerOver)
                .Select(button => (button.Name ?? "<unnamed>") + ":" + (button.Content?.ToString() ?? "<null>"))
                .ToList();
        SafeStartupTrace.Write(
            "ui-pointer | event=pressed | stableRoot=true" +
            " | sourceType=" + (source?.GetType().FullName ?? e.Source?.GetType().FullName ?? "<null>") +
            " | sourceName=" + (source?.Name ?? "<unnamed>") +
            " | pointerOverButtons=" + (pointerOver.Count == 0 ? "<none>" : string.Join(",", pointerOver)) +
            " | pageHostBounds=" + pageHost.Bounds +
            " | windowEnabled=" + window.IsEnabled);
    }

    private static StackPanel? FindProjectRow(MainWindow window)
    {
        var homeRoot = StableWorkflowRootUi.HomeRoot(window);
        if (homeRoot is null) return null;
        var header = homeRoot.Children.OfType<Grid>().FirstOrDefault(child => Grid.GetRow(child) == 0);
        return header?.Children.OfType<StackPanel>().FirstOrDefault(panel =>
            panel.Orientation == Orientation.Horizontal && panel.Children.OfType<Button>().Any(button =>
                string.Equals(button.Content?.ToString(), "Nuovo progetto", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(button.Name, "DiezOwnedNewProject", StringComparison.Ordinal)));
    }

    private static bool Contains(Control root, Control target) => Descendants(root).Any(control => ReferenceEquals(control, target));

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
