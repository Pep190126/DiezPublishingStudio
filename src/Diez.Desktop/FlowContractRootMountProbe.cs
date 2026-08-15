using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// CI/diagnostic helper only. Ensures structural flow contracts run with the workflow physically mounted
/// through the same visible Home entry used by a real user, instead of mutating a detached pageHost.
/// </summary>
internal static class FlowContractRootMountProbe
{
    public static async Task EnsureMountedAsync(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var overlay = host.GetType().GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as Grid
            ?? throw new InvalidOperationException("Workflow root non disponibile nel contract mount probe.");

        if (window.Content is Border currentBorder && ReferenceEquals(currentBorder.Child, overlay) && ReferenceEquals(overlay.Parent, currentBorder))
            return;

        var entry = Descendants(window).OfType<Button>().FirstOrDefault(button =>
            string.Equals(button.Name, SingleWindowNativeEntryBridgeUi.NativeEntryName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Ingresso Percorso libro nativo non disponibile nel contract mount probe.");

        if (!entry.IsVisible || !entry.IsEnabled || !entry.IsHitTestVisible)
            throw new InvalidOperationException(
                $"Ingresso Percorso libro non operativo nel contract mount probe: visible={entry.IsVisible}, enabled={entry.IsEnabled}, hitTest={entry.IsHitTestVisible}.");

        entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Yield();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

        if (window.Content is not Border border || !ReferenceEquals(border.Child, overlay) || !ReferenceEquals(overlay.Parent, border))
            throw new InvalidOperationException(
                "Il workflow non è stato montato fisicamente tramite DiezNativeBookFlowEntry prima del contract strutturale.");

        SafeStartupTrace.Write(
            "flow-contract-root-mount | mounted=true | route=DiezNativeBookFlowEntry" +
            " | overlayBounds=" + overlay.Bounds);
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
