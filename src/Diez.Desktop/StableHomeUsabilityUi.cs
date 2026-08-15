using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps the permanent Home surface in sync after external Windows-dialog mutations and makes
/// the Workflow -> Home transition synchronous at the stable-root level. It does not change
/// project data or the file-dialog implementation.
/// </summary>
internal static class StableHomeUsabilityUi
{
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<Button> WiredHomeButtons = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;

        var status = Field<TextBlock>(window, "_status")
            ?? throw new InvalidOperationException("Status Home non disponibile.");

        status.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBlock.TextProperty) return;
            var text = status.Text ?? string.Empty;
            if (text.StartsWith("Importati ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Aperto:", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Creato pacchetto", StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    RefreshMaterials(window, selectLatest: text.StartsWith("Importati ", StringComparison.OrdinalIgnoreCase));
                    SingleWindowQuantityUsabilityUi.ForceWin32Frame(window,
                        text.StartsWith("Aperto:", StringComparison.OrdinalIgnoreCase) ? "home-project-opened" :
                        text.StartsWith("Importati ", StringComparison.OrdinalIgnoreCase) ? "home-materials-imported" :
                        "home-project-created");
                }, DispatcherPriority.Loaded);
            }
        };

        WireHomeProjectButton(window);
        RefreshMaterials(window, selectLatest: false);

        window.Closed += (_, _) => Attached.Remove(window);
        SafeStartupTrace.Write("stable-home-usability | attached=true | material-refresh=explicit | home-return=synchronous | win32-refresh=project-mutations");
    }

    private static void WireHomeProjectButton(MainWindow window)
    {
        var workflowRoot = StableWorkflowRootUi.WorkflowRoot(window);
        if (workflowRoot is null) return;

        var button = Descendants(workflowRoot).OfType<Button>().FirstOrDefault(b =>
            string.Equals(b.Content?.ToString(), "Home progetto", StringComparison.OrdinalIgnoreCase));
        if (button is null || !WiredHomeButtons.Add(button)) return;

        // The legacy host handler clears page/history first. This later handler immediately selects Home
        // in the permanent root, instead of waiting for a posted Content=null callback.
        button.Click += (_, _) =>
        {
            StableWorkflowRootUi.ActivateHome(window);
            RefreshMaterials(window, selectLatest: false);
            SingleWindowQuantityUsabilityUi.ForceWin32Frame(window, "home-project-button");
            SafeStartupTrace.Write("stable-home-usability | action=home-project | activeHome=true");
        };
    }

    private static void RefreshMaterials(MainWindow window, bool selectLatest)
    {
        var project = Field<PreviewProject>(window, "_project");
        var list = Field<ListBox>(window, "_materialsList");
        if (list is null) return;

        if (project is null)
        {
            list.ItemsSource = null;
            list.SelectedIndex = -1;
            return;
        }

        var previous = list.SelectedIndex;
        list.ItemsSource = project.Materials
            .Select(m => $"{(m.IsEmbedded ? "●" : "○")}  {m.Kind}  ·  {m.FileName}  ·  {m.Summary}")
            .ToList();

        if (project.Materials.Count > 0)
        {
            if (selectLatest) list.SelectedIndex = project.Materials.Count - 1;
            else if (previous >= 0 && previous < project.Materials.Count) list.SelectedIndex = previous;
        }

        list.InvalidateMeasure();
        list.InvalidateArrange();
        list.InvalidateVisual();
        StableWorkflowRootUi.HomeRoot(window)?.InvalidateVisual();

        SafeStartupTrace.Write(
            "stable-home-materials | count=" + project.Materials.Count +
            " | selectedIndex=" + list.SelectedIndex +
            " | homeActive=" + !StableWorkflowRootUi.IsWorkflowActive(window) +
            " | listBounds=" + list.Bounds);
    }

    private static T? Field<T>(object owner, string name) where T : class =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) as T;

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
