using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps the logical book workflow detached until the user enters it.
/// On some real Win32/Avalonia combinations a Grid first measured as an invisible child can remain
/// stuck at 0x0 after runtime reparenting. Detaching before the first Window layout avoids carrying
/// that stale layout state into the active Border root. A zero-size recovery is retained as a guard.
/// </summary>
internal static class DetachedWorkflowRootUi
{
    private static readonly HashSet<MainWindow> Attached = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;

        var host = SingleWindowEntryPointUi.GetHost(window);
        var overlay = Field<Grid>(host, "_overlay")
            ?? throw new InvalidOperationException("Workflow root non disponibile per il pre-detach.");
        var pageHost = Field<ContentControl>(host, "_pageHost")
            ?? throw new InvalidOperationException("PageHost non disponibile per il pre-detach.");

        var previousParent = overlay.Parent?.GetType().FullName ?? "<null>";
        if (overlay.Parent is Panel panel)
            panel.Children.Remove(overlay);
        else if (overlay.Parent is not null)
            throw new InvalidOperationException("Il workflow root ha un parent inatteso prima del primo layout: " + previousParent);

        overlay.IsVisible = false;
        overlay.HorizontalAlignment = HorizontalAlignment.Stretch;
        overlay.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, 1);

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            Dispatcher.UIThread.Post(() => RecoverMountedLayout(window, overlay, pageHost), DispatcherPriority.Render);
        };

        window.Closed += (_, _) => Attached.Remove(window);
        SafeStartupTrace.Write(
            "workflow-root-pre-detached | previousParent=" + previousParent +
            " | currentParent=" + (overlay.Parent?.GetType().FullName ?? "<null>") +
            " | before-first-window-layout=true");
    }

    private static void RecoverMountedLayout(MainWindow window, Grid overlay, ContentControl pageHost)
    {
        if (window.Content is not Border border ||
            !ReferenceEquals(border.Child, overlay) ||
            !overlay.IsVisible)
            return;

        border.InvalidateMeasure();
        border.InvalidateArrange();
        overlay.InvalidateMeasure();
        overlay.InvalidateArrange();
        pageHost.InvalidateMeasure();
        pageHost.InvalidateArrange();

        Dispatcher.UIThread.Post(() =>
        {
            if (overlay.Bounds.Width > 1 && overlay.Bounds.Height > 1)
            {
                SafeStartupTrace.Write(
                    "workflow-layout-recovery | needed=false" +
                    " | overlayBounds=" + overlay.Bounds +
                    " | pageHostBounds=" + pageHost.Bounds);
                return;
            }

            var width = Math.Max(100,
                border.Bounds.Width - border.Padding.Left - border.Padding.Right);
            var height = Math.Max(100,
                border.Bounds.Height - border.Padding.Top - border.Padding.Bottom);
            var size = new Size(width, height);

            overlay.Measure(size);
            overlay.Arrange(new Rect(0, 0, width, height));
            overlay.InvalidateVisual();

            SafeStartupTrace.Write(
                "workflow-layout-recovery | needed=true | forced=true" +
                " | available=" + width.ToString("0.##") + "x" + height.ToString("0.##") +
                " | overlayBounds=" + overlay.Bounds +
                " | pageHostBounds=" + pageHost.Bounds);

            Dispatcher.UIThread.Post(() => SafeStartupTrace.Write(
                "workflow-layout-recovery-after-render" +
                " | overlayBounds=" + overlay.Bounds +
                " | pageHostBounds=" + pageHost.Bounds +
                " | pageBounds=" + ((pageHost.Content as Control)?.Bounds.ToString() ?? "<none>")),
                DispatcherPriority.Render);
        }, DispatcherPriority.Render);
    }

    private static T? Field<T>(object host, string name) where T : class =>
        host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as T;
}
