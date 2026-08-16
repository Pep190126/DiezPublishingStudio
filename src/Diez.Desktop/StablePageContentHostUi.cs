using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DiezPublishingStudio;

/// <summary>
/// Owns only the permanent workflow page ContentControl layout policy. The stable top-level root is already
/// physically measured on classic Win32. Avalonia 11.3.18 queues invalidated layout through MediaContext's
/// next-render callback; on the affected Win32 path that callback can be delayed while the new Content page
/// remains attached but measure-invalid at 0x0. We first use normal invalidation, then execute Avalonia's own
/// queued LayoutManager pass only when the page is still layout-invalid after Loaded. The same narrow fallback
/// is used when an already-mounted page reveals hidden controls. We also make sure templates are applied after
/// a page is mounted: a TemplatedControl with no template visual children and no draw list cannot participate
/// in compositor hit-testing even when its logical bounds are valid. We never manually Measure/Arrange controls
/// and never reparent Home/Workflow at runtime.
/// </summary>
internal static class StablePageContentHostUi
{
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly Dictionary<MainWindow, HashSet<Control>> VisibilityWired = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        VisibilityWired[window] = [];

        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
            ?? throw new InvalidOperationException("PageHost non disponibile per la policy stable content host.");

        pageHost.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        pageHost.VerticalContentAlignment = VerticalAlignment.Stretch;

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;

            if (pageHost.Content is Control currentPage)
            {
                var applied = ApplyTemplates(currentPage);
                SafeStartupTrace.Write(
                    "stable-page-template-apply" +
                    " | phase=content-change" +
                    " | applied=" + applied +
                    " | page=" + (currentPage.Name ?? currentPage.GetType().Name));
                WireDynamicVisibilityLayout(window, pageHost, currentPage);
            }

            var presenter = pageHost.Presenter;
            SafeStartupTrace.Write(
                "stable-page-content-invalidate" +
                " | hostMeasureValidBefore=" + pageHost.IsMeasureValid +
                " | hostArrangeValidBefore=" + pageHost.IsArrangeValid +
                " | presenterMeasureValidBefore=" + (presenter?.IsMeasureValid.ToString() ?? "<none>") +
                " | presenterArrangeValidBefore=" + (presenter?.IsArrangeValid.ToString() ?? "<none>") +
                " | action=InvalidateMeasuredWorkflowChain");

            InvalidateWorkflowChain(window, pageHost);

            Dispatcher.UIThread.Post(() =>
            {
                if (pageHost.Content is Control loadedPage)
                {
                    var applied = ApplyTemplates(loadedPage);
                    SafeStartupTrace.Write(
                        "stable-page-template-apply" +
                        " | phase=loaded" +
                        " | applied=" + applied +
                        " | page=" + (loadedPage.Name ?? loadedPage.GetType().Name));
                }

                InvalidateWorkflowChain(window, pageHost);
                Trace(pageHost, "loaded-before-pass");

                var executed = ExecuteQueuedAvaloniaLayoutPassIfNeeded(window, pageHost);
                SafeStartupTrace.Write(
                    "stable-page-layout-manager-pass" +
                    " | reason=content-change" +
                    " | executed=" + executed +
                    " | pageBounds=" + ((pageHost.Content as Control)?.Bounds.ToString() ?? "<none>") +
                    " | hostMeasureValid=" + pageHost.IsMeasureValid +
                    " | hostArrangeValid=" + pageHost.IsArrangeValid);

                Trace(pageHost, "loaded-after-pass");
                Dispatcher.UIThread.Post(() => Trace(pageHost, "render"), DispatcherPriority.Render);
            }, DispatcherPriority.Loaded);
        };

        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => Trace(pageHost, "opened"), DispatcherPriority.Render);
        window.Closed += (_, _) =>
        {
            Attached.Remove(window);
            VisibilityWired.Remove(window);
        };

        SafeStartupTrace.Write(
            "stable-page-content-host-attached" +
            " | horizontal=Stretch | vertical=Stretch | manual-arrange=false" +
            " | content-invalidation=measured-workflow-chain" +
            " | template-activation=mounted-subtree" +
            " | zero-page-fallback=avalonia-layout-manager-pass" +
            " | dynamic-visibility-fallback=avalonia-layout-manager-pass");
    }

    private static int ApplyTemplates(Control root)
    {
        var applied = 0;
        var knownVisualCount = -1;

        // A template can create more templated controls. Repeat a small bounded number of times so newly
        // materialized visual children receive their own templates without introducing an unbounded walk.
        for (var pass = 0; pass < 4; pass++)
        {
            var controls = Descendants(root)
                .Concat(root.GetVisualDescendants().OfType<Control>())
                .Distinct()
                .ToList();

            foreach (var templated in controls.OfType<TemplatedControl>())
            {
                var before = templated.GetVisualChildren().Count();
                templated.ApplyTemplate();
                var after = templated.GetVisualChildren().Count();
                if (after > before) applied++;
            }

            var visualCount = root.GetVisualDescendants().Count();
            if (visualCount == knownVisualCount) break;
            knownVisualCount = visualCount;
        }

        return applied;
    }

    private static void WireDynamicVisibilityLayout(MainWindow window, ContentControl pageHost, Control page)
    {
        if (!VisibilityWired.TryGetValue(window, out var wired)) return;

        foreach (var control in Descendants(page))
        {
            if (!wired.Add(control)) continue;
            control.PropertyChanged += (_, change) =>
            {
                if (!string.Equals(change.Property.Name, nameof(Control.IsVisible), StringComparison.Ordinal)) return;
                if (!ReferenceEquals(pageHost.Content, page)) return;

                SafeStartupTrace.Write(
                    "stable-page-dynamic-visibility" +
                    " | control=" + (control.Name ?? control.GetType().Name) +
                    " | visible=" + control.IsVisible +
                    " | pageBounds=" + page.Bounds);

                InvalidateWorkflowChain(window, pageHost);
                Dispatcher.UIThread.Post(() =>
                {
                    if (!ReferenceEquals(pageHost.Content, page)) return;
                    ApplyTemplates(page);
                    var executed = ExecuteQueuedAvaloniaLayoutPassIfNeeded(window, pageHost);
                    SafeStartupTrace.Write(
                        "stable-page-layout-manager-pass" +
                        " | reason=visibility-change" +
                        " | executed=" + executed +
                        " | control=" + (control.Name ?? control.GetType().Name) +
                        " | pageBounds=" + page.Bounds +
                        " | pageMeasureValid=" + page.IsMeasureValid +
                        " | pageArrangeValid=" + page.IsArrangeValid);
                }, DispatcherPriority.Loaded);
            };
        }
    }

    private static bool ExecuteQueuedAvaloniaLayoutPassIfNeeded(MainWindow window, ContentControl pageHost)
    {
        var page = pageHost.Content as Control;
        if (page is null || !page.IsAttachedToVisualTree()) return false;
        if (page.Bounds.Width > 0 && page.Bounds.Height > 0 && page.IsMeasureValid && page.IsArrangeValid) return false;

        try
        {
            var layoutManagerProperty = typeof(TopLevel).GetProperty(
                "LayoutManager",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var layoutManager = layoutManagerProperty?.GetValue(window);
            var execute = layoutManager?.GetType().GetMethod(
                "ExecuteLayoutPass",
                BindingFlags.Instance | BindingFlags.Public);

            if (layoutManager is null || execute is null)
            {
                SafeStartupTrace.Write("stable-page-layout-manager-pass | reflection=unavailable");
                return false;
            }

            execute.Invoke(layoutManager, null);
            return true;
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write(
                "stable-page-layout-manager-pass | error=" + ex.GetBaseException().GetType().Name +
                ": " + ex.GetBaseException().Message);
            return false;
        }
    }

    private static void InvalidateWorkflowChain(MainWindow window, ContentControl pageHost)
    {
        var workflowRoot = StableWorkflowRootUi.WorkflowRoot(window);
        var presenter = pageHost.Presenter;
        if (presenter is not null)
        {
            presenter.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            presenter.VerticalContentAlignment = VerticalAlignment.Stretch;
            Invalidate(presenter);
        }

        if (pageHost.Content is Control page)
        {
            page.HorizontalAlignment = HorizontalAlignment.Stretch;
            page.VerticalAlignment = VerticalAlignment.Stretch;
            Invalidate(page);
        }

        Control? current = pageHost;
        var seen = new HashSet<Control>();
        while (current is not null && seen.Add(current))
        {
            Invalidate(current);
            if (workflowRoot is not null && ReferenceEquals(current, workflowRoot)) break;
            current = current.Parent as Control;
        }

        SafeStartupTrace.Write(
            "stable-page-content-chain" +
            " | hostBounds=" + pageHost.Bounds +
            " | presenterBounds=" + (presenter?.Bounds.ToString() ?? "<none>") +
            " | workflowBounds=" + (workflowRoot?.Bounds.ToString() ?? "<none>") +
            " | reachedWorkflowRoot=" + (workflowRoot is not null && seen.Contains(workflowRoot)));
    }

    private static void Invalidate(Control control)
    {
        control.InvalidateMeasure();
        control.InvalidateArrange();
        control.InvalidateVisual();
    }

    private static void Trace(ContentControl pageHost, string phase)
    {
        try
        {
            var page = pageHost.Content as Control;
            var visualParent = page?.GetVisualParent() as Control;
            var presenter = pageHost.Presenter;
            var presenterContentMatches = presenter is not null && ReferenceEquals(presenter.Content, page);

            SafeStartupTrace.Write(
                "stable-page-content-layout" +
                " | phase=" + phase +
                " | hostBounds=" + pageHost.Bounds +
                " | hostDesired=" + pageHost.DesiredSize +
                " | hostVisible=" + pageHost.IsVisible +
                " | hostEffectiveVisible=" + pageHost.IsEffectivelyVisible +
                " | hostMeasureValid=" + pageHost.IsMeasureValid +
                " | hostArrangeValid=" + pageHost.IsArrangeValid +
                " | horizontal=" + pageHost.HorizontalContentAlignment +
                " | vertical=" + pageHost.VerticalContentAlignment +
                " | pageType=" + (page?.GetType().FullName ?? "<none>") +
                " | pageBounds=" + (page?.Bounds.ToString() ?? "<none>") +
                " | pageDesired=" + (page?.DesiredSize.ToString() ?? "<none>") +
                " | pageVisible=" + (page?.IsVisible.ToString() ?? "<none>") +
                " | pageEffectiveVisible=" + (page?.IsEffectivelyVisible.ToString() ?? "<none>") +
                " | pageMeasureValid=" + (page?.IsMeasureValid.ToString() ?? "<none>") +
                " | pageArrangeValid=" + (page?.IsArrangeValid.ToString() ?? "<none>") +
                " | presenterType=" + (presenter?.GetType().FullName ?? "<none>") +
                " | presenterMatchesContent=" + presenterContentMatches +
                " | presenterBounds=" + (presenter?.Bounds.ToString() ?? "<none>") +
                " | presenterDesired=" + (presenter?.DesiredSize.ToString() ?? "<none>") +
                " | presenterVisible=" + (presenter?.IsVisible.ToString() ?? "<none>") +
                " | presenterEffectiveVisible=" + (presenter?.IsEffectivelyVisible.ToString() ?? "<none>") +
                " | presenterMeasureValid=" + (presenter?.IsMeasureValid.ToString() ?? "<none>") +
                " | presenterArrangeValid=" + (presenter?.IsArrangeValid.ToString() ?? "<none>") +
                " | presenterHorizontal=" + (presenter?.HorizontalContentAlignment.ToString() ?? "<none>") +
                " | presenterVertical=" + (presenter?.VerticalContentAlignment.ToString() ?? "<none>") +
                " | visualParentType=" + (visualParent?.GetType().FullName ?? "<none>") +
                " | visualParentSamePresenter=" + ReferenceEquals(visualParent, presenter));
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("stable-page-content-layout | phase=" + phase + " | trace-error=" + ex.GetBaseException().Message);
        }
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
