using System.Reflection;
using Avalonia.Controls;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps the image-specification controls attached to the quantity/content page without activating
/// the historical prompt-text injector. Provider-facing prompt text is owned exclusively by
/// PromptEngineeringCompiler / SingleWindowPromptTargetAiUi.
/// </summary>
internal static class SingleWindowImageSpecsQuantityOnlyUi
{
    public static void Attach(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost is null) return;
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty) EnsureQuantityPage(window, pageHost);
        };
        EnsureQuantityPage(window, pageHost);
    }

    private static void EnsureQuantityPage(MainWindow window, ContentControl pageHost)
    {
        if (pageHost.Content is not Control page) return;
        if (!Descendants(page).OfType<TextBlock>().Any(t =>
                (t.Text ?? string.Empty).Contains("Quante immagini vuoi creare?", StringComparison.Ordinal)))
            return;
        SingleWindowImageSpecsUi.EnsureCurrentPage(window);
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
                case Border border when border.Child is Control child: stack.Push(child); break;
                case ScrollViewer scroll when scroll.Content is Control child: stack.Push(child); break;
                case ContentControl content when content.Content is Control child: stack.Push(child); break;
            }
        }
    }
}
