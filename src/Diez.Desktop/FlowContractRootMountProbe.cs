using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// CI/diagnostic helper only. Ensures structural flow contracts enter Workflow through the same visible
/// Home button used by a real user. Under the permanent-root architecture this validates active input
/// ownership, not Border.Child replacement.
/// </summary>
internal static class FlowContractRootMountProbe
{
    public static async Task EnsureMountedAsync(MainWindow window)
    {
        if (StableWorkflowRootUi.IsWorkflowActive(window)) return;

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

        if (!StableWorkflowRootUi.IsInstalled(window) || !StableWorkflowRootUi.IsWorkflowActive(window))
            throw new InvalidOperationException(
                "Il workflow non ha ottenuto l'input nella radice stabile tramite DiezNativeBookFlowEntry.");

        var stable = StableWorkflowRootUi.StableRoot(window);
        var home = StableWorkflowRootUi.HomeRoot(window);
        var workflow = StableWorkflowRootUi.WorkflowRoot(window);
        SafeStartupTrace.Write(
            "flow-contract-root-mount | mounted=true | route=DiezNativeBookFlowEntry | stableRoot=true" +
            " | rootBounds=" + (stable?.Bounds.ToString() ?? "<null>") +
            " | homeBounds=" + (home?.Bounds.ToString() ?? "<null>") +
            " | workflowBounds=" + (workflow?.Bounds.ToString() ?? "<null>"));
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
