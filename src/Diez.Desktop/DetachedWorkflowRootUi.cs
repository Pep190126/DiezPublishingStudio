using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps the logical book workflow detached until the user enters it.
/// On some real Win32/Avalonia combinations a Grid first measured as an invisible child can remain
/// stuck at 0x0 after runtime reparenting. Detaching before the first Window layout avoids carrying
/// that stale layout state into the active Border root. The detached root is kept Visible while it
/// has no parent so the later Border.Child assignment performs its first measure as a visible root.
/// Every page replacement is also validated physically: root, page host and current page must all
/// have real bounds. Win32 repaint is requested after the layout pass so the pixels shown by Windows
/// cannot remain one navigation step behind the Avalonia hit-test tree.
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

        overlay.IsVisible = true;
        overlay.HorizontalAlignment = HorizontalAlignment.Stretch;
        overlay.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, 1);

        // ContentControl defaults must not allow a freshly swapped logical page to keep a stale 0x0
        // arrangement while its host already has valid bounds.
        pageHost.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        pageHost.VerticalContentAlignment = VerticalAlignment.Stretch;

        // ShowHome sets the detached root back to IsVisible=false. For every later physical mouse entry,
        // restore visibility during PointerPressed, before Button.Click performs the root swap.
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!IsNativeEntryPointer(e.Source)) return;
            if (overlay.Parent is not null) return;
            overlay.IsVisible = true;
            overlay.InvalidateMeasure();
            overlay.InvalidateArrange();
            SafeStartupTrace.Write(
                "workflow-root-before-click | visible=true | parent=<null> | physical-pointer=true");
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            Dispatcher.UIThread.Post(() => RecoverMountedLayout(window, overlay, pageHost), DispatcherPriority.Render);
        };

        window.Closed += (_, _) => Attached.Remove(window);
        SafeStartupTrace.Write(
            "workflow-root-pre-detached | previousParent=" + previousParent +
            " | currentParent=" + (overlay.Parent?.GetType().FullName ?? "<null>") +
            " | visibleWhileDetached=" + overlay.IsVisible +
            " | before-first-window-layout=true");
    }

    private static bool IsNativeEntryPointer(object? source)
    {
        var current = source as Control;
        while (current is not null)
        {
            if (current is Button button &&
                string.Equals(button.Name, SingleWindowNativeEntryBridgeUi.NativeEntryName, StringComparison.Ordinal))
                return true;
            current = current.Parent as Control;
        }
        return false;
    }

    private static void RecoverMountedLayout(MainWindow window, Grid overlay, ContentControl pageHost)
    {
        if (window.Content is not Border border ||
            !ReferenceEquals(border.Child, overlay) ||
            !overlay.IsVisible)
            return;

        var page = pageHost.Content as Control;
        InvalidateLayout(window, border, overlay, pageHost, page);

        Dispatcher.UIThread.Post(() =>
        {
            page = pageHost.Content as Control;
            var overlayHealthy = HasArea(overlay);
            var hostHealthy = HasArea(pageHost);
            var pageHealthy = page is null || HasArea(page);

            if (!overlayHealthy || !hostHealthy || !pageHealthy)
            {
                var width = Math.Max(100,
                    border.Bounds.Width - border.Padding.Left - border.Padding.Right);
                var height = Math.Max(100,
                    border.Bounds.Height - border.Padding.Top - border.Padding.Bottom);
                var size = new Size(width, height);

                // Re-run the complete workflow layout even if only the current ContentControl child is 0x0.
                // The previous guard looked only at overlay.Bounds and therefore incorrectly returned
                // needed=false while pageBounds was still 0x0 on real Win32 machines.
                overlay.Measure(size);
                overlay.Arrange(new Rect(0, 0, width, height));

                page = pageHost.Content as Control;
                if (page is not null && !HasArea(page) && HasArea(pageHost))
                {
                    page.HorizontalAlignment = HorizontalAlignment.Stretch;
                    page.VerticalAlignment = VerticalAlignment.Stretch;
                    page.InvalidateMeasure();
                    page.InvalidateArrange();
                    var pageSize = new Size(pageHost.Bounds.Width, pageHost.Bounds.Height);
                    page.Measure(pageSize);
                    page.Arrange(new Rect(0, 0, pageSize.Width, pageSize.Height));
                }

                InvalidateVisuals(window, border, overlay, pageHost, page);
                SafeStartupTrace.Write(
                    "workflow-layout-recovery | needed=true | forced=true" +
                    " | reason=" + RecoveryReason(overlay, pageHost, page) +
                    " | available=" + width.ToString("0.##") + "x" + height.ToString("0.##") +
                    " | overlayBounds=" + overlay.Bounds +
                    " | pageHostBounds=" + pageHost.Bounds +
                    " | pageBounds=" + (page?.Bounds.ToString() ?? "<none>"));
            }
            else
            {
                InvalidateVisuals(window, border, overlay, pageHost, page);
                SafeStartupTrace.Write(
                    "workflow-layout-recovery | needed=false" +
                    " | overlayBounds=" + overlay.Bounds +
                    " | pageHostBounds=" + pageHost.Bounds +
                    " | pageBounds=" + (page?.Bounds.ToString() ?? "<none>"));
            }

            RequestNativeRepaint(window, "after-layout");

            Dispatcher.UIThread.Post(() =>
            {
                page = pageHost.Content as Control;
                // One final layout/visual invalidation after the renderer turn closes the race where the
                // ContentPresenter template realizes only after the first forced parent arrange.
                if (page is not null && !HasArea(page) && HasArea(pageHost))
                {
                    var pageSize = new Size(pageHost.Bounds.Width, pageHost.Bounds.Height);
                    page.InvalidateMeasure();
                    page.InvalidateArrange();
                    page.Measure(pageSize);
                    page.Arrange(new Rect(0, 0, pageSize.Width, pageSize.Height));
                }
                InvalidateVisuals(window, border, overlay, pageHost, page);
                RequestNativeRepaint(window, "after-render");
                SafeStartupTrace.Write(
                    "workflow-layout-recovery-after-render" +
                    " | overlayBounds=" + overlay.Bounds +
                    " | pageHostBounds=" + pageHost.Bounds +
                    " | pageBounds=" + (page?.Bounds.ToString() ?? "<none>") +
                    " | pageHealthy=" + (page is null || HasArea(page)));
            }, DispatcherPriority.Render);
        }, DispatcherPriority.Render);
    }

    private static void InvalidateLayout(
        MainWindow window,
        Border border,
        Grid overlay,
        ContentControl pageHost,
        Control? page)
    {
        window.InvalidateMeasure();
        window.InvalidateArrange();
        border.InvalidateMeasure();
        border.InvalidateArrange();
        overlay.InvalidateMeasure();
        overlay.InvalidateArrange();
        pageHost.InvalidateMeasure();
        pageHost.InvalidateArrange();
        page?.InvalidateMeasure();
        page?.InvalidateArrange();
    }

    private static void InvalidateVisuals(
        MainWindow window,
        Border border,
        Grid overlay,
        ContentControl pageHost,
        Control? page)
    {
        page?.InvalidateVisual();
        pageHost.InvalidateVisual();
        overlay.InvalidateVisual();
        border.InvalidateVisual();
        window.InvalidateVisual();
    }

    private static bool HasArea(Control control) => control.Bounds.Width > 1 && control.Bounds.Height > 1;

    private static string RecoveryReason(Grid overlay, ContentControl pageHost, Control? page)
    {
        var reasons = new List<string>();
        if (!HasArea(overlay)) reasons.Add("overlay-zero");
        if (!HasArea(pageHost)) reasons.Add("pagehost-zero");
        if (page is not null && !HasArea(page)) reasons.Add("page-zero");
        return reasons.Count == 0 ? "none" : string.Join(',', reasons);
    }

    private static void RequestNativeRepaint(MainWindow window, string phase)
    {
        if (!OperatingSystem.IsWindows()) return;
        var platform = window.TryGetPlatformHandle();
        if (platform is null || platform.Handle == IntPtr.Zero) return;

        var ok = RedrawWindow(
            platform.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            RdwInvalidate | RdwInternalPaint | RdwErase | RdwAllChildren | RdwUpdateNow);
        SafeStartupTrace.Write(
            "workflow-native-repaint | phase=" + phase +
            " | hwnd=0x" + platform.Handle.ToInt64().ToString("X") +
            " | success=" + ok);
    }

    private static T? Field<T>(object host, string name) where T : class =>
        host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as T;

    private const uint RdwInvalidate = 0x0001;
    private const uint RdwInternalPaint = 0x0002;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(
        IntPtr hWnd,
        IntPtr updateRect,
        IntPtr updateRegion,
        uint flags);
}
