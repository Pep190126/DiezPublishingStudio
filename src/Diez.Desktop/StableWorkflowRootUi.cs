using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Permanent single-window visual root. Home and Workflow are parented once during startup and are never
/// reparented during navigation. Workflow stays permanently visible, opaque and registered for pointer input;
/// Home stays mounted, opaque and measurable but is removed from hit testing only while Workflow is active.
/// Surface ownership changes only through Z-order and the Home hit gate, never through root opacity.
/// </summary>
internal static class StableWorkflowRootUi
{
    internal const string RootName = "DiezStableMainRoot";
    private static readonly Dictionary<MainWindow, RootState> States = [];

    public static void Attach(MainWindow window)
    {
        if (States.ContainsKey(window)) return;
        if (window.Content is not Border border || border.Child is not Grid homeRoot)
            throw new InvalidOperationException("Home root non disponibile per la radice visuale stabile.");

        var host = SingleWindowEntryPointUi.GetHost(window);
        var overlay = Field<Grid>(host, "_overlay")
            ?? throw new InvalidOperationException("Workflow overlay non disponibile per la radice visuale stabile.");

        if (overlay.Parent is Panel oldParent)
            oldParent.Children.Remove(overlay);
        else if (overlay.Parent is not null)
            throw new InvalidOperationException("Parent workflow inatteso prima della radice stabile: " + overlay.Parent.GetType().FullName);

        border.Child = null;

        homeRoot.HorizontalAlignment = HorizontalAlignment.Stretch;
        homeRoot.VerticalAlignment = VerticalAlignment.Stretch;
        overlay.HorizontalAlignment = HorizontalAlignment.Stretch;
        overlay.VerticalAlignment = VerticalAlignment.Stretch;
        // The legacy overlay was originally mounted directly in the old desktop Grid with its own 14x10
        // outer margin. Once it becomes a child of the permanent stable root that margin would be applied a
        // second time. Home and Workflow must share the same local origin inside DiezStableMainRoot.
        overlay.Margin = new Avalonia.Thickness(0);
        overlay.ClipToBounds = true;

        // Never use root opacity as a navigation switch. On classic Win32 the Avalonia property could become 1
        // while the long-lived CompositionVisual remained at opacity 0, leaving both pixels and pointer hit-test
        // stale. Home instead gets an opaque surface matching the window/root background so it can cover the
        // always-opaque Workflow when Home owns the higher Z-order.
        homeRoot.Background ??= window.Background ?? border.Background ?? Avalonia.Media.Brushes.White;
        overlay.Background = null;

        // No Workflow backstop is required anymore. The backstop was introduced only to prevent pointer input
        // from falling through to Home while both surfaces were hittable. Home is now explicitly removed from
        // hit testing whenever Workflow is active, so a full-area Border here can only become an unnecessary
        // terminal hit target in front of the real page subtree.
        homeRoot.IsVisible = true;
        homeRoot.Opacity = 1;
        homeRoot.IsEnabled = true;
        homeRoot.IsHitTestVisible = true;
        overlay.IsVisible = true;
        overlay.Opacity = 1;
        overlay.IsEnabled = true;
        overlay.IsHitTestVisible = true;
        overlay.ZIndex = 0;
        overlay.PropertyChanged += (_, e) =>
        {
            if (!string.Equals(e.Property.Name, "IsVisible", StringComparison.Ordinal) || overlay.IsVisible) return;
            overlay.IsVisible = true;
            SafeStartupTrace.Write(
                "stable-root-visibility-guard | surface=workflow | requested=false | coerced=true");
        };

        var stableRoot = new Grid
        {
            Name = RootName,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true
        };
        stableRoot.Children.Add(homeRoot);
        stableRoot.Children.Add(overlay);
        border.Child = stableRoot;

        var state = new RootState(border, stableRoot, homeRoot, overlay);
        States[window] = state;
        ActivateHomeCore(state);

        window.Closed += (_, _) => States.Remove(window);
        SafeStartupTrace.Write(
            "stable-root-installed | borderChild=" + stableRoot.Name +
            " | homeParent=" + (homeRoot.Parent?.GetType().Name ?? "<null>") +
            " | workflowParent=" + (overlay.Parent?.GetType().Name ?? "<null>") +
            " | workflowMargin=" + overlay.Margin +
            " | workflowVisibleBeforeFirstParent=true" +
            " | workflowVisibilityOwnedByStableRoot=true" +
            " | workflowPermanentlyHitTestable=true" +
            " | surfacesPermanentlyOpaque=true" +
            " | inactiveHomeHitGate=true" +
            " | inputOwnership=z-order-home-gate-no-opacity" +
            " | workflowRootHitSurface=null" +
            " | workflowBackstop=none-home-gated" +
            " | runtime-reparenting=false");
        TraceCompositionOrder(state, "home-installed");
    }

    public static void ActivateWorkflow(MainWindow window)
    {
        if (!States.TryGetValue(window, out var state)) return;
        RestoreHomeChildren(state.HomeRoot);

        state.HomeRoot.IsVisible = true;
        state.HomeRoot.Opacity = 1;
        state.HomeRoot.IsEnabled = true;
        state.HomeRoot.IsHitTestVisible = false;
        state.HomeRoot.ZIndex = 0;

        state.Overlay.IsVisible = true;
        state.Overlay.Opacity = 1;
        state.Overlay.IsEnabled = true;
        state.Overlay.IsHitTestVisible = true;
        state.Overlay.ZIndex = 1;
        state.WorkflowActive = true;

        Invalidate(state.StableRoot);
        SafeStartupTrace.Write(
            "stable-root-state | active=workflow" +
            " | rootBounds=" + state.StableRoot.Bounds +
            " | homeBounds=" + state.HomeRoot.Bounds +
            " | workflowBounds=" + state.Overlay.Bounds +
            " | workflowMargin=" + state.Overlay.Margin +
            " | workflowPermanentlyHitTestable=true" +
            " | surfacesPermanentlyOpaque=true" +
            " | inactiveHomeHitGate=true" +
            " | inputOwnership=z-order-home-gate-no-opacity" +
            " | homeHit=" + state.HomeRoot.IsHitTestVisible +
            " | workflowHit=" + state.Overlay.IsHitTestVisible +
            " | homeOpacity=" + state.HomeRoot.Opacity +
            " | workflowOpacity=" + state.Overlay.Opacity +
            " | workflowRootHitSurface=null" +
            " | workflowBackstop=none-home-gated" +
            " | runtime-reparenting=false");
        TraceCompositionOrder(state, "workflow-immediate");
        Dispatcher.UIThread.Post(() => TraceCompositionOrder(state, "workflow-render"), DispatcherPriority.Render);
        Dispatcher.UIThread.Post(() => TraceCompositionOrder(state, "workflow-background"), DispatcherPriority.Background);
    }

    public static void ActivateHome(MainWindow window)
    {
        if (!States.TryGetValue(window, out var state)) return;
        ActivateHomeCore(state);
        Invalidate(state.StableRoot);
        SafeStartupTrace.Write(
            "stable-root-state | active=home" +
            " | rootBounds=" + state.StableRoot.Bounds +
            " | homeBounds=" + state.HomeRoot.Bounds +
            " | workflowBounds=" + state.Overlay.Bounds +
            " | workflowMargin=" + state.Overlay.Margin +
            " | workflowPermanentlyHitTestable=true" +
            " | surfacesPermanentlyOpaque=true" +
            " | inactiveHomeHitGate=true" +
            " | inputOwnership=z-order-home-gate-no-opacity" +
            " | homeHit=" + state.HomeRoot.IsHitTestVisible +
            " | workflowHit=" + state.Overlay.IsHitTestVisible +
            " | homeOpacity=" + state.HomeRoot.Opacity +
            " | workflowOpacity=" + state.Overlay.Opacity +
            " | workflowRootHitSurface=null" +
            " | workflowBackstop=none-home-gated" +
            " | runtime-reparenting=false");
        TraceCompositionOrder(state, "home-immediate");
        Dispatcher.UIThread.Post(() => TraceCompositionOrder(state, "home-render"), DispatcherPriority.Render);
    }

    public static bool IsWorkflowActive(MainWindow window)
    {
        return States.TryGetValue(window, out var state) &&
               state.WorkflowActive &&
               window.Content is Border border &&
               ReferenceEquals(border.Child, state.StableRoot) &&
               ReferenceEquals(state.HomeRoot.Parent, state.StableRoot) &&
               ReferenceEquals(state.Overlay.Parent, state.StableRoot) &&
               state.Overlay.IsVisible && state.Overlay.IsHitTestVisible && state.Overlay.IsEnabled && state.Overlay.Opacity == 1 &&
               state.HomeRoot.IsVisible && !state.HomeRoot.IsHitTestVisible && state.HomeRoot.IsEnabled && state.HomeRoot.Opacity == 1 &&
               state.Overlay.ZIndex > state.HomeRoot.ZIndex;
    }

    public static bool IsInstalled(MainWindow window)
    {
        return States.TryGetValue(window, out var state) &&
               window.Content is Border border && ReferenceEquals(border.Child, state.StableRoot);
    }

    public static Grid? HomeRoot(MainWindow window) => States.TryGetValue(window, out var state) ? state.HomeRoot : null;
    public static Grid? WorkflowRoot(MainWindow window) => States.TryGetValue(window, out var state) ? state.Overlay : null;
    public static Grid? StableRoot(MainWindow window) => States.TryGetValue(window, out var state) ? state.StableRoot : null;

    private static void ActivateHomeCore(RootState state)
    {
        RestoreHomeChildren(state.HomeRoot);
        state.HomeRoot.IsVisible = true;
        state.HomeRoot.Opacity = 1;
        state.HomeRoot.IsEnabled = true;
        state.HomeRoot.IsHitTestVisible = true;
        state.HomeRoot.ZIndex = 1;

        // Workflow remains visible, opaque, enabled and hit-test registered below Home for its entire lifetime.
        // Navigation never asks the compositor to synchronize a 0 -> 1 root opacity transition again.
        state.Overlay.IsVisible = true;
        state.Overlay.Opacity = 1;
        state.Overlay.IsEnabled = true;
        state.Overlay.IsHitTestVisible = true;
        state.Overlay.ZIndex = 0;
        state.WorkflowActive = false;
    }

    private static void RestoreHomeChildren(Grid homeRoot)
    {
        // SingleWindowOverlayFlowHost historically hides these direct Home children when showing a page.
        // Under the stable-root model Home itself is selected by Z-order, so its children remain visible and
        // measurable to avoid a collapsed-subtree problem on return.
        foreach (var child in homeRoot.Children.OfType<Control>())
            child.IsVisible = true;
    }

    private static void Invalidate(Control control)
    {
        control.InvalidateMeasure();
        control.InvalidateArrange();
        control.InvalidateVisual();
    }

    private static void TraceCompositionOrder(RootState state, string phase)
    {
        try
        {
            var compositionProperty = typeof(Avalonia.Visual).GetProperty(
                "CompositionVisual",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var stableComposition = compositionProperty?.GetValue(state.StableRoot);
            var homeComposition = compositionProperty?.GetValue(state.HomeRoot);
            var workflowComposition = compositionProperty?.GetValue(state.Overlay);
            if (stableComposition is null)
            {
                SafeStartupTrace.Write("stable-root-compositor-order | phase=" + phase + " | stableComp=null");
                return;
            }

            var childrenProperty = FindProperty(stableComposition.GetType(), "Children");
            var children = childrenProperty?.GetValue(stableComposition) as IEnumerable;
            if (children is null)
            {
                SafeStartupTrace.Write("stable-root-compositor-order | phase=" + phase + " | children=unavailable");
                return;
            }

            var list = children.Cast<object>().ToList();
            var homeIndex = list.FindIndex(c => ReferenceEquals(c, homeComposition));
            var workflowIndex = list.FindIndex(c => ReferenceEquals(c, workflowComposition));
            SafeStartupTrace.Write(
                "stable-root-compositor-order | phase=" + phase +
                " | count=" + list.Count +
                " | homeIndex=" + homeIndex +
                " | workflowIndex=" + workflowIndex +
                " | compositorTop=" + (homeIndex > workflowIndex ? "home" : workflowIndex > homeIndex ? "workflow" : "unknown") +
                " | homeZ=" + state.HomeRoot.ZIndex +
                " | workflowZ=" + state.Overlay.ZIndex +
                " | homeHit=" + state.HomeRoot.IsHitTestVisible +
                " | workflowHit=" + state.Overlay.IsHitTestVisible);
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write(
                "stable-root-compositor-order | phase=" + phase +
                " | error=" + ex.GetBaseException().Message.Replace('|', '/'));
        }
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property is not null) return property;
        }
        return null;
    }

    private static T? Field<T>(object host, string name) where T : class =>
        host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as T;

    private sealed class RootState
    {
        public RootState(Border border, Grid stableRoot, Grid homeRoot, Grid overlay)
        {
            Border = border;
            StableRoot = stableRoot;
            HomeRoot = homeRoot;
            Overlay = overlay;
        }

        public Border Border { get; }
        public Grid StableRoot { get; }
        public Grid HomeRoot { get; }
        public Grid Overlay { get; }
        public bool WorkflowActive { get; set; }
    }
}
