using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Owns the single production Home entry for SW-FLOW-12. The active workflow is not layered over the
/// Home desktop anymore: it becomes the Border's only child while active, then the Home Grid is restored.
/// This keeps one physical MainWindow while removing every overlapping/sibling input surface.
/// </summary>
internal static class SingleWindowNativeEntryBridgeUi
{
    internal const string NativeEntryName = "DiezNativeBookFlowEntry";
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<Button> WiredButtons = [];
    private static readonly Dictionary<MainWindow, Grid> HomeRoots = [];
    private static readonly Dictionary<MainWindow, ProjectOperationProbe> ProjectOperationProbes = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        if (!TryHomeSurface(window, out _, out var homeRoot, out var row))
            throw new InvalidOperationException("Riga comandi progetto non disponibile per l'ingresso nativo.");

        HomeRoots[window] = homeRoot;
        InstallProjectTimingProbe(window, row);

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
                Dispatcher.UIThread.Post(() => TraceMountedLayout(window, host, pageHost), DispatcherPriority.Render);
            }, DispatcherPriority.Loaded);
        };

        // Tunnel is enough to observe the real hit target and avoids duplicate trace lines for one click.
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) => TracePointer(window, host, e),
            RoutingStrategies.Tunnel, handledEventsToo: true);

        window.Closed += (_, _) =>
        {
            HomeRoots.Remove(window);
            ProjectOperationProbes.Remove(window);
            Attached.Remove(window);
        };
        SafeStartupTrace.Write("native-entry-bridge-attached | legacy-disabled=true | input-owner=workflow-root-swap");
    }

    private static void OpenNative(MainWindow window)
    {
        SafeStartupTrace.Write(
            "ui-click | action=Percorso libro | route=native-v11" +
            " | windowEnabled=" + window.IsEnabled +
            " | active=" + window.IsActive);
        try
        {
            var host = SingleWindowEntryPointUi.GetHost(window);
            if (!MountWorkflowRoot(window, host))
                throw new InvalidOperationException("La superficie del Percorso libro non può diventare la radice della MainWindow.");

            SingleWindowNativeV11Ui.ShowStart(window);
            EnsureWorkflowInputOwnership(window, host, active: true);
            SafeStartupTrace.Write("ui-navigation | target=native-v11-start | success=true | rootSwap=true");
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("ui-navigation | target=native-v11-start | error=" + ex);
            CrashDiagnostics.Error("native-book-flow-entry", ex);
        }
    }

    private static bool MountWorkflowRoot(MainWindow window, object host)
    {
        var overlay = Field<Grid>(host, "_overlay");
        if (overlay is null || window.Content is not Border border) return false;
        if (!HomeRoots.TryGetValue(window, out var homeRoot)) return false;

        if (ReferenceEquals(border.Child, overlay)) return true;
        if (!ReferenceEquals(border.Child, homeRoot))
        {
            SafeStartupTrace.Write(
                "ui-root-swap | mount=false | unexpectedCurrentRoot=" +
                (border.Child?.GetType().FullName ?? "<null>"));
            return false;
        }

        if (overlay.Parent is Panel oldParent)
            oldParent.Children.Remove(overlay);
        else if (overlay.Parent is not null)
        {
            SafeStartupTrace.Write("ui-root-swap | mount=false | unexpectedOverlayParent=" + overlay.Parent.GetType().FullName);
            return false;
        }

        overlay.HorizontalAlignment = HorizontalAlignment.Stretch;
        overlay.VerticalAlignment = VerticalAlignment.Stretch;
        overlay.IsHitTestVisible = true;
        overlay.Background = Brushes.White;
        overlay.ClipToBounds = true;
        border.Child = overlay;

        var mounted = ReferenceEquals(border.Child, overlay);
        SafeStartupTrace.Write(
            "ui-root-swap | mount=" + mounted +
            " | homeDetached=" + (homeRoot.Parent is null) +
            " | overlayParent=" + (overlay.Parent?.GetType().FullName ?? "<null>"));
        return mounted;
    }

    private static void RestoreHomeRoot(MainWindow window, object host)
    {
        if (window.Content is not Border border || !HomeRoots.TryGetValue(window, out var homeRoot)) return;
        var overlay = Field<Grid>(host, "_overlay");
        if (overlay is null) return;

        if (ReferenceEquals(border.Child, homeRoot)) return;
        if (!ReferenceEquals(border.Child, overlay))
        {
            SafeStartupTrace.Write(
                "ui-root-swap | restore=false | unexpectedCurrentRoot=" +
                (border.Child?.GetType().FullName ?? "<null>"));
            return;
        }

        border.Child = homeRoot;
        SafeStartupTrace.Write(
            "ui-root-swap | restore=true | homeParent=" + (homeRoot.Parent?.GetType().FullName ?? "<null>") +
            " | overlayDetached=" + (overlay.Parent is null));
    }

    private static void EnsureWorkflowInputOwnership(MainWindow window, object host, bool active)
    {
        var overlay = Field<Grid>(host, "_overlay");
        if (overlay is null) return;

        if (!active || !overlay.IsVisible)
        {
            RestoreHomeRoot(window, host);
            SafeStartupTrace.Write("ui-input-owner | active=false | home-root-restored=true");
            return;
        }

        if (!MountWorkflowRoot(window, host))
        {
            SafeStartupTrace.Write("ui-input-owner | active=true | mountedAsRoot=false | siblings-untouched=true");
            return;
        }

        overlay.ZIndex = 0;
        overlay.IsHitTestVisible = true;
        overlay.Background = Brushes.White;
        overlay.ClipToBounds = true;
        ConfigureWorkflowSurface(host, overlay);

        var border = window.Content as Border;
        SafeStartupTrace.Write(
            "ui-input-owner | active=true | rootSwap=true" +
            " | mountedAsRoot=" + ReferenceEquals(border?.Child, overlay) +
            " | overlayVisible=" + overlay.IsVisible +
            " | overlayHitTest=" + overlay.IsHitTestVisible +
            " | overlayBounds=" + overlay.Bounds +
            " | borderBounds=" + (border?.Bounds.ToString() ?? "<na>") +
            " | siblingsDisabled=0");
    }

    private static void ConfigureWorkflowSurface(object host, Grid overlay)
    {
        var pageHost = Field<ContentControl>(host, "_pageHost");
        var previewHost = Field<ContentControl>(host, "_previewHost");
        if (pageHost is null || previewHost is null) return;

        pageHost.IsHitTestVisible = true;
        previewHost.IsHitTestVisible = false;

        var body = overlay.Children.OfType<Grid>().FirstOrDefault(grid => Grid.GetRow(grid) == 1);
        if (body is null)
        {
            SafeStartupTrace.Write("ui-surface-layout | direct-body=missing");
            return;
        }

        var pageSurface = body.Children.OfType<Border>().FirstOrDefault(surface => ReferenceEquals(surface.Child, pageHost));
        var previewSurface = body.Children.OfType<Border>().FirstOrDefault(surface =>
            !ReferenceEquals(surface, pageSurface) && surface.Child is Control child && Contains(child, previewHost));
        if (pageSurface is null || previewSurface is null)
        {
            SafeStartupTrace.Write("ui-surface-layout | direct-page-or-preview=missing");
            return;
        }

        body.Background = Brushes.White;
        body.ClipToBounds = true;
        pageSurface.ZIndex = 1;
        pageSurface.IsHitTestVisible = true;
        previewSurface.ZIndex = 0;
        previewSurface.IsHitTestVisible = false;

        var title = Field<TextBlock>(host, "_title")?.Text ?? string.Empty;
        var bookTypePage = string.Equals(title, "Tipo libro", StringComparison.Ordinal);
        if (bookTypePage)
        {
            previewSurface.IsVisible = false;
            Grid.SetColumn(pageSurface, 0);
            Grid.SetColumnSpan(pageSurface, Math.Max(1, body.ColumnDefinitions.Count));
        }
        else
        {
            previewSurface.IsVisible = true;
            Grid.SetColumn(pageSurface, 0);
            Grid.SetColumnSpan(pageSurface, 1);
        }

        SafeStartupTrace.Write(
            "ui-surface-layout | title=" + (string.IsNullOrWhiteSpace(title) ? "<untitled>" : title) +
            " | bodyBounds=" + body.Bounds +
            " | pageBounds=" + pageSurface.Bounds +
            " | pageZ=" + pageSurface.ZIndex +
            " | pageHitTest=" + pageSurface.IsHitTestVisible +
            " | pageColumnSpan=" + Grid.GetColumnSpan(pageSurface) +
            " | previewVisible=" + previewSurface.IsVisible +
            " | previewHitTest=" + previewSurface.IsHitTestVisible +
            " | previewZ=" + previewSurface.ZIndex);
    }

    private static void TracePointer(MainWindow window, object host, PointerPressedEventArgs e)
    {
        try
        {
            var overlay = Field<Grid>(host, "_overlay");
            if (overlay?.IsVisible != true) return;
            var source = e.Source as Control;
            var pageHost = Field<ContentControl>(host, "_pageHost");
            var page = pageHost?.Content as Control;
            var buttons = page is null ? new List<Button>() : Descendants(page).OfType<Button>().ToList();
            var pointerOver = buttons.Where(button => button.IsPointerOver)
                .Select(button => (button.Name ?? "<unnamed>") + ":" + (button.Content?.ToString() ?? "<null>"))
                .ToList();
            var mountedAsRoot = window.Content is Border border && ReferenceEquals(border.Child, overlay);

            SafeStartupTrace.Write(
                "ui-pointer | event=pressed" +
                " | sourceType=" + (source?.GetType().FullName ?? e.Source?.GetType().FullName ?? "<null>") +
                " | sourceName=" + (source?.Name ?? "<unnamed>") +
                " | sourceEnabled=" + (source?.IsEnabled.ToString() ?? "<na>") +
                " | sourceHitTest=" + (source?.IsHitTestVisible.ToString() ?? "<na>") +
                " | pointerOverButtons=" + (pointerOver.Count == 0 ? "<none>" : string.Join(",", pointerOver)) +
                " | pageHostBounds=" + (pageHost?.Bounds.ToString() ?? "<na>") +
                " | mountedAsRoot=" + mountedAsRoot +
                " | overlayHitTest=" + overlay.IsHitTestVisible +
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
                        " | bounds=" + button.Bounds +
                        " | windowEnabled=" + window.IsEnabled +
                        " | active=" + window.IsActive);
                }, RoutingStrategies.Bubble, handledEventsToo: true);
            }

            SafeStartupTrace.Write(
                "ui-page | title=" + title +
                " | pageHostBounds=" + pageHost.Bounds +
                " | pageBounds=" + (page?.Bounds.ToString() ?? "<none>") +
                " | windowEnabled=" + window.IsEnabled +
                " | active=" + window.IsActive +
                " | buttons=" + string.Join(";", buttons.Select(b =>
                    (b.Name ?? "<unnamed>") + ":" + (b.Content?.ToString() ?? "<null>") +
                    ":enabled=" + b.IsEnabled + ":hitTest=" + b.IsHitTestVisible +
                    ":visible=" + b.IsVisible + ":bounds=" + b.Bounds)));
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("ui-page-trace-error | " + ex.GetBaseException().Message);
        }
    }

    private static void TraceMountedLayout(MainWindow window, object host, ContentControl pageHost)
    {
        try
        {
            var overlay = Field<Grid>(host, "_overlay");
            if (overlay?.IsVisible != true) return;
            var page = pageHost.Content as Control;
            var mounted = window.Content is Border border && ReferenceEquals(border.Child, overlay);
            SafeStartupTrace.Write(
                "ui-layout-after-render | mountedAsRoot=" + mounted +
                " | overlayBounds=" + overlay.Bounds +
                " | pageHostBounds=" + pageHost.Bounds +
                " | pageBounds=" + (page?.Bounds.ToString() ?? "<none>"));
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("ui-layout-after-render-error | " + ex.GetBaseException().Message);
        }
    }

    private static void InstallProjectTimingProbe(MainWindow window, StackPanel row)
    {
        var status = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as TextBlock;
        if (status is null) return;

        var probe = new ProjectOperationProbe();
        ProjectOperationProbes[window] = probe;

        foreach (var button in row.Children.OfType<Button>())
        {
            var text = button.Content?.ToString() ?? string.Empty;
            if (string.Equals(text, "Apri progetto", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Apri .diez", StringComparison.OrdinalIgnoreCase))
            {
                button.Click += (_, _) => probe.Arm("open-project");
            }
            else if (string.Equals(text, "Nuovo progetto", StringComparison.OrdinalIgnoreCase))
            {
                button.Click += (_, _) => probe.Arm("create-project");
            }
        }

        window.Activated += (_, _) =>
        {
            if (probe.Armed && !probe.Running)
            {
                probe.StartAfterDialog();
                SafeStartupTrace.Write("project-timing | operation=" + probe.Operation + " | phase=dialog-returned");
            }
        };

        status.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBlock.TextProperty || !probe.Running) return;
            var text = status.Text ?? string.Empty;
            var completed = probe.Operation == "open-project"
                ? text.StartsWith("Aperto", StringComparison.OrdinalIgnoreCase)
                : text.StartsWith("Creato pacchetto", StringComparison.OrdinalIgnoreCase);
            if (!completed) return;
            SafeStartupTrace.Write(
                "project-timing | operation=" + probe.Operation +
                " | phase=completed | elapsedMs=" + probe.StopElapsedMilliseconds());
        };
    }

    private static bool IsLegacyBookFlowEntry(Button button)
    {
        if (string.Equals(button.Name, NativeEntryName, StringComparison.Ordinal)) return false;
        var text = button.Content?.ToString() ?? string.Empty;
        return string.Equals(text, "Percorso libro", StringComparison.Ordinal) ||
               text.Contains("Percorso libro · SW-FLOW-", StringComparison.Ordinal);
    }

    private static bool TryHomeSurface(MainWindow window, out Border border, out Grid desktop, out StackPanel row)
    {
        border = null!;
        desktop = null!;
        row = null!;
        if (window.Content is not Border rootBorder || rootBorder.Child is not Grid rootDesktop) return false;
        var header = rootDesktop.Children.OfType<Grid>().FirstOrDefault(c => Grid.GetRow(c) == 0);
        if (header is null) return false;
        var commandRow = header.Children.OfType<StackPanel>().FirstOrDefault(p =>
            p.Orientation == Orientation.Horizontal &&
            p.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.OrdinalIgnoreCase)));
        if (commandRow is null) return false;
        border = rootBorder;
        desktop = rootDesktop;
        row = commandRow;
        return true;
    }

    private static T? Field<T>(object host, string name) where T : class =>
        host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as T;

    private static bool Contains(Control root, Control target) =>
        Descendants(root).Any(control => ReferenceEquals(control, target));

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

    private sealed class ProjectOperationProbe
    {
        private readonly Stopwatch _watch = new();
        public string Operation { get; private set; } = string.Empty;
        public bool Armed { get; private set; }
        public bool Running => _watch.IsRunning;

        public void Arm(string operation)
        {
            Operation = operation;
            Armed = true;
            _watch.Reset();
        }

        public void StartAfterDialog()
        {
            if (!Armed) return;
            _watch.Restart();
        }

        public long StopElapsedMilliseconds()
        {
            _watch.Stop();
            Armed = false;
            return _watch.ElapsedMilliseconds;
        }
    }
}
