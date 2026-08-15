using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DiezPublishingStudio;

/// <summary>
/// Owns only the permanent workflow page ContentControl layout policy. The stable top-level root is already
/// physically measured on classic Win32; this module keeps each dynamically replaced page stretched inside
/// that measured host and records the actual presenter/child geometry without forcing Measure/Arrange manually.
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
            Dispatcher.UIThread.Post(() => Trace(pageHost), DispatcherPriority.Render);
        };

        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => Trace(pageHost), DispatcherPriority.Render);
        window.Closed += (_, _) => Attached.Remove(window);

        SafeStartupTrace.Write(
            "stable-page-content-host-attached" +
            " | horizontal=Stretch | vertical=Stretch | manual-arrange=false");
    }

    private static void Trace(ContentControl pageHost)
    {
        try
        {
            var page = pageHost.Content as Control;
            var visualParent = page?.GetVisualParent() as Control;
            var presenter = pageHost.Presenter;
            var presenterContentMatches = presenter is not null && ReferenceEquals(presenter.Content, page);

            SafeStartupTrace.Write(
                "stable-page-content-layout" +
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
            SafeStartupTrace.Write("stable-page-content-layout | trace-error=" + ex.GetBaseException().Message);
        }
    }
}
