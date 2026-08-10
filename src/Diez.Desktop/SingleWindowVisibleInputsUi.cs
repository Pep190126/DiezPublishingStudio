using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Makes editable controls visually unmistakable in the single-window workflow.
/// The Fluent theme can otherwise make an empty TextBox look like plain background
/// on some Windows/theme combinations. This adapter is visual only: it never changes
/// the user's content or the project model.
/// </summary>
internal static class SingleWindowVisibleInputsUi
{
    private static readonly HashSet<MainWindow> Attached = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = PageHost(host);
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            Dispatcher.UIThread.Post(() => Apply(window), DispatcherPriority.Loaded);
            Dispatcher.UIThread.Post(() => Apply(window), DispatcherPriority.Background);
        };
        window.Closed += (_, _) => Attached.Remove(window);
        Apply(window);
    }

    internal static void Apply(MainWindow window)
    {
        object host;
        try { host = SingleWindowEntryPointUi.GetHost(window); }
        catch { return; }
        if (PageHost(host).Content is not Control page) return;

        foreach (var box in Descendants(page).OfType<TextBox>())
        {
            if (!box.IsEnabled || box.IsReadOnly) continue;
            box.Opacity = 1;
            box.Background = Brushes.White;
            box.Foreground = Brushes.Black;
            box.BorderBrush = Brushes.Gray;
            box.BorderThickness = new Thickness(2);
            box.Padding = new Thickness(9, 7);
            box.MinHeight = Math.Max(box.MinHeight, box.AcceptsReturn ? 70 : 38);
            if (double.IsNaN(box.Width))
                box.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        foreach (var number in Descendants(page).OfType<NumericUpDown>())
        {
            if (!number.IsEnabled) continue;
            number.Opacity = 1;
            number.Background = Brushes.White;
            number.Foreground = Brushes.Black;
            number.BorderBrush = Brushes.Gray;
            number.BorderThickness = new Thickness(2);
            number.MinHeight = Math.Max(number.MinHeight, 38);
        }
    }

    private static ContentControl PageHost(object host) =>
        host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
        ?? throw new InvalidOperationException("PageHost single-window non disponibile.");

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
