using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Permanent single-window visual root. Home and Workflow are parented once during startup and are never
/// reparented during navigation. Workflow stays permanently registered for pointer input; Home stays mounted
/// and measurable but is removed from hit testing only while Workflow is active. Workflow uses a separate
/// transparent backstop behind its content so empty-area clicks cannot fall through to Home.
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

        // Workflow must be registered for input from its first parent onward; that was the critical branch that
        // previously failed when enabled late. Home is different: it is born interactive and already passes the
        // physical Win32 click probe, so while Workflow owns the surface Home can safely be gated out of hit
        // testing without ever unregistering Workflow. A transparent full-area backstop remains behind Workflow
        // content so empty-area pointer input cannot fall through when Home is gated.
        homeRoot.Background ??= Avalonia.Media.Brushes.Transparent;
        overlay.Background = null;
        var workflowBackstop = new Border
        {
            Background = Avalonia.Media.Brushes.Transparent,
            IsHitTestVisible = true,
            IsEnabled = true,
            ZIndex = -100
        };
        Grid.SetRow(workflowBackstop, 0);
        Grid.SetRowSpan(workflowBackstop, Math.Max(1, overlay.RowDefinitions.Count));
        overlay.Children.Insert(0, workflowBackstop);

        homeRoot.IsVisible = true;
        homeRoot.IsEnabled = true;
        homeRoot.IsHitTestVisible = true;
        overlay.IsVisible = true;
        overlay.Opacity = 0;
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

        var state = new RootState(border, stableRoot, homeRoot, overlay, workflowBackstop);
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
            " | inactiveHomeHitGate=true" +
            " | inputOwnership=workflow-permanent-home-gated" +
            " | workflowRootHitSurface=null" +
            " | workflowBackstop=transparent-behind-content" +
            " | runtime-reparenting=false");
    }

    public static void ActivateWorkflow(MainWindow window)
    {
        if (!States.TryGetValue(window, out var state)) return;
        RestoreHomeChildren(state.HomeRoot);

        state.HomeRoot.IsVisible = true;
        state.HomeRoot.Opacity = 0;
        state.HomeRoot.IsEnabled = true;
        state.HomeRoot.IsHitTestVisible = false;
        state.HomeRoot.ZIndex = 0;

        state.Overlay.IsVisible = true;
        state.Overlay.Opacity = 1;
        state.Overlay.IsEnabled = true;
        state.Overlay.IsHitTestVisible = true;
        state.Overlay.ZIndex = 1;
        state.WorkflowBackstop.IsVisible = true;
        state.WorkflowBackstop.IsEnabled = true;
        state.WorkflowBackstop.IsHitTestVisible = true;
        state.WorkflowActive = true;

        Invalidate(state.StableRoot);
        SafeStartupTrace.Write(
            "stable-root-state | active=workflow" +
            " | rootBounds=" + state.StableRoot.Bounds +
            " | homeBounds=" + state.HomeRoot.Bounds +
            " | workflowBounds=" + state.Overlay.Bounds +
            " | workflowMargin=" + state.Overlay.Margin +
            " | workflowPermanentlyHitTestable=true" +
            " | inactiveHomeHitGate=true" +
            " | inputOwnership=workflow-permanent-home-gated" +
            " | homeHit=" + state.HomeRoot.IsHitTestVisible +
            " | workflowHit=" + state.Overlay.IsHitTestVisible +
            " | workflowRootHitSurface=null" +
            " | workflowBackstopBounds=" + state.WorkflowBackstop.Bounds +
            " | runtime-reparenting=false");
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
            " | inactiveHomeHitGate=true" +
            " | inputOwnership=workflow-permanent-home-gated" +
            " | homeHit=" + state.HomeRoot.IsHitTestVisible +
            " | workflowHit=" + state.Overlay.IsHitTestVisible +
            " | workflowRootHitSurface=null" +
            " | workflowBackstopBounds=" + state.WorkflowBackstop.Bounds +
            " | runtime-reparenting=false");
    }

    public static bool IsWorkflowActive(MainWindow window)
    {
        return States.TryGetValue(window, out var state) &&
               state.WorkflowActive &&
               window.Content is Border border &&
               ReferenceEquals(border.Child, state.StableRoot) &&
               ReferenceEquals(state.HomeRoot.Parent, state.StableRoot) &&
               ReferenceEquals(state.Overlay.Parent, state.StableRoot) &&
               ReferenceEquals(state.WorkflowBackstop.Parent, state.Overlay) &&
               state.Overlay.IsVisible && state.Overlay.IsHitTestVisible && state.Overlay.IsEnabled &&
               state.WorkflowBackstop.IsVisible && state.WorkflowBackstop.IsHitTestVisible && state.WorkflowBackstop.IsEnabled &&
               state.HomeRoot.IsVisible && !state.HomeRoot.IsHitTestVisible && state.HomeRoot.IsEnabled &&
               state.Overlay.ZIndex > state.HomeRoot.ZIndex && state.Overlay.Opacity > state.HomeRoot.Opacity;
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

        // Workflow remains fully registered for input below the Home surface. Home is a full transparent hit
        // surface while active, so pointer input cannot reach the lower Workflow subtree. Activating Workflow
        // never requires re-registering it with the platform; only Home's already-proven hit gate changes.
        state.Overlay.IsVisible = true;
        state.Overlay.Opacity = 0;
        state.Overlay.IsEnabled = true;
        state.Overlay.IsHitTestVisible = true;
        state.Overlay.ZIndex = 0;
        state.WorkflowBackstop.IsVisible = true;
        state.WorkflowBackstop.IsEnabled = true;
        state.WorkflowBackstop.IsHitTestVisible = true;
        state.WorkflowActive = false;
    }

    private static void RestoreHomeChildren(Grid homeRoot)
    {
        // SingleWindowOverlayFlowHost historically hides these direct Home children when showing a page.
        // Under the stable-root model Home itself is hidden by opacity/Z-order, so its children remain visible
        // and measurable to avoid a collapsed-subtree problem on return.
        foreach (var child in homeRoot.Children.OfType<Control>())
            child.IsVisible = true;
    }

    private static void Invalidate(Control control)
    {
        control.InvalidateMeasure();
        control.InvalidateArrange();
        control.InvalidateVisual();
    }

    private static T? Field<T>(object host, string name) where T : class =>
        host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as T;

    private sealed class RootState
    {
        public RootState(Border border, Grid stableRoot, Grid homeRoot, Grid overlay, Border workflowBackstop)
        {
            Border = border;
            StableRoot = stableRoot;
            HomeRoot = homeRoot;
            Overlay = overlay;
            WorkflowBackstop = workflowBackstop;
        }

        public Border Border { get; }
        public Grid StableRoot { get; }
        public Grid HomeRoot { get; }
        public Grid Overlay { get; }
        public Border WorkflowBackstop { get; }
        public bool WorkflowActive { get; set; }
    }
}
