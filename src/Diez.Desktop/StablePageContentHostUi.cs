using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DiezPublishingStudio;

/// <summary>
/// Owns only the permanent workflow page ContentControl layout policy. The stable top-level root is already
/// physically measured on classic Win32. When Content changes, classic Win32 can leave the new page and its
/// ContentPresenter measure-invalid at 0x0 even though pageHost already has valid bounds. Reschedule Avalonia's
/// normal layout from the nearest already-measured workflow ancestors; never call Measure/Arrange manually and
/// never reparent Home/Workflow at runtime.
/// </summary>
internal static class StablePageContentHostUi
{
    private static readonly HashSet<MainWindow> Attached = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;

        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
            ?? throw new InvalidOperationException("PageHost non disponibile per la policy stable content host.");

        pageHost.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        pageHost.VerticalContentAlignment = VerticalAlignment.Stretch;

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;

            var presenter = pageHost.Presenter;
            SafeStartupTrace.Write(
                "stable-page-content-invalidate" +
                " | hostMeasureValidBefore=" + pageHost.IsMeasureValid +
                " | hostArrangeValidBefore=" + pageHost.IsArrangeValid +
                " | presenterMeasureValidBefore=" + (presenter?.IsMeasureValid.ToString() ?? "<none>") +
                " | presenterArrangeValidBefore=" + (presenter?.IsArrangeValid.ToString() ?? "<none>") +
                " | action=InvalidateMeasuredWorkflowChain");

            InvalidateWorkflowChain(window, pageHost);

            // ContentPresenter realization itself can happen after the Content property notification. Repeat the
            // same normal invalidation once at Loaded so the newly realized presenter/page participates in the
            // next layout pass. This is deliberately not a manual Measure/Arrange workaround.
            Dispatcher.UIThread.Post(() =>
            {
                InvalidateWorkflowChain(window, pageHost);
                Trace(pageHost, "loaded");
                Dispatcher.UIThread.Post(() => Trace(pageHost, "render"), DispatcherPriority.Render);
            }, DispatcherPriority.Loaded);
        };

        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => Trace(pageHost, "opened"), DispatcherPriority.Render);
        window.Closed += (_, _) => Attached.Remove(window);

        SafeStartupTrace.Write(
            "stable-page-content-host-attached" +
            " | horizontal=Stretch | vertical=Stretch | manual-arrange=false" +
            " | content-invalidation=measured-workflow-chain");
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
}
