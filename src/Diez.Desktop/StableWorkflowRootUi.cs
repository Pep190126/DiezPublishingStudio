using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Permanent single-window visual root. Home and Workflow are parented once during startup and are never
/// reparented during navigation. The inactive surface stays measurable (IsVisible=true) but cannot paint or
/// receive input. This replaces runtime Border.Child swapping and the pre-layout detached-root workaround.
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
        // second time, giving rendering and platform pointer input different nested coordinate origins.
        // Home and Workflow must share the same local origin inside DiezStableMainRoot.
        overlay.Margin = new Avalonia.Thickness(0);
        // The workflow root owns layout/input state for its subtree but must not become the terminal hit surface.
        // A painted Grid background caused InputHitTest to stop here instead of descending to editable controls.
        overlay.Background = null;
        overlay.ClipToBounds = true;

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
            " | workflowHitSurface=transparent" +
            " | runtime-reparenting=false");
    }

    public static void ActivateWorkflow(MainWindow window)
    {
        if (!States.TryGetValue(window, out var state)) return;
        RestoreHomeChildren(state.HomeRoot);
        state.HomeRoot.IsVisible = true;
        state.HomeRoot.Opacity = 0;
        state.HomeRoot.IsEnabled = false;
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
            " | workflowHitSurface=transparent" +
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
            " | workflowHitSurface=transparent" +
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
               state.Overlay.IsVisible && state.Overlay.IsHitTestVisible && state.Overlay.IsEnabled &&
               !state.HomeRoot.IsHitTestVisible && !state.HomeRoot.IsEnabled;
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

        // Keep workflow in the visual/layout tree even on Home. Opacity and input ownership, not
        // IsVisible/reparenting, select the active surface.
        state.Overlay.IsVisible = true;
        state.Overlay.Opacity = 0;
        state.Overlay.IsEnabled = false;
        state.Overlay.IsHitTestVisible = false;
        state.Overlay.ZIndex = 0;
        state.WorkflowActive = false;
    }

    private static void RestoreHomeChildren(Grid homeRoot)
    {
        // SingleWindowOverlayFlowHost historically hides these direct Home children when showing a page.
        // Under the stable-root model Home itself is hidden by opacity/input state, so its children remain
        // visible and measurable to avoid the same collapsed-subtree problem on return.
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
