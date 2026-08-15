using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps the visible book title field human-friendly without changing ProjectId semantics.
/// When no edition title exists yet, it starts from the project name and remains fully editable.
/// </summary>
internal static class SingleWindowBookTitleUsabilityUi
{
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<TextBox> WiredTitles = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;

        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost")
            ?? throw new InvalidOperationException("PageHost non disponibile per il titolo libro.");

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            Dispatcher.UIThread.Post(() => Apply(window, pageHost), DispatcherPriority.Loaded);
        };

        Apply(window, pageHost);
        window.Closed += (_, _) => Attached.Remove(window);
    }

    private static void Apply(MainWindow window, ContentControl pageHost)
    {
        var project = Field<PreviewProject>(window, "_project");
        if (project is null || pageHost.Content is not Control page) return;

        var title = Descendants(page).OfType<TextBox>().FirstOrDefault(c => c.Name == "DiezBookTitle");
        var frame = Descendants(page).OfType<Border>().FirstOrDefault(c => c.Name == "DiezBookTitleFrame");
        var field = Descendants(page).OfType<StackPanel>().FirstOrDefault(c => c.Name == "DiezBookTitleField");
        if (title is null || frame is null || field is null) return;

        if (string.IsNullOrWhiteSpace(project.EditionMetadata.Title))
        {
            project.EditionMetadata.Title = project.Name;
            title.Text = project.Name;
        }
        else if (string.IsNullOrWhiteSpace(title.Text))
        {
            title.Text = project.EditionMetadata.Title;
        }

        const double width = 620;
        field.Width = width;
        field.HorizontalAlignment = HorizontalAlignment.Left;
        field.IsEnabled = true;
        field.IsHitTestVisible = true;
        frame.Width = width;
        frame.MaxWidth = width;
        frame.HorizontalAlignment = HorizontalAlignment.Left;
        frame.IsEnabled = true;
        frame.IsHitTestVisible = true;
        title.HorizontalAlignment = HorizontalAlignment.Stretch;
        title.TextAlignment = TextAlignment.Left;

        // Do not rely on inherited/default TextBox input state for this late-added field. The installed Win32
        // failure rendered the field correctly while it behaved like a label. Make its edit contract explicit.
        title.IsReadOnly = false;
        title.IsEnabled = true;
        title.IsHitTestVisible = true;
        title.Focusable = true;
        title.IsUndoEnabled = true;

        if (WiredTitles.Add(title))
        {
            frame.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
            {
                var focused = title.Focus();
                SafeStartupTrace.Write(
                    "book-title-input | event=pointer-pressed" +
                    " | focusRequested=" + focused +
                    " | enabled=" + title.IsEnabled +
                    " | hitTest=" + title.IsHitTestVisible +
                    " | readOnly=" + title.IsReadOnly);
            }, RoutingStrategies.Tunnel, handledEventsToo: true);

            title.GotFocus += (_, _) => SafeStartupTrace.Write(
                "book-title-input | event=got-focus | focused=" + title.IsFocused);
            title.TextChanged += (_, _) => SafeStartupTrace.Write(
                "book-title-input | event=text-changed | length=" + (title.Text?.Length ?? 0));
        }

        foreach (var label in field.Children.OfType<TextBlock>())
            label.HorizontalAlignment = HorizontalAlignment.Left;

        SingleWindowQuantityUsabilityUi.ForceWin32Frame(window, "book-title-input-ready");
        SafeStartupTrace.Write(
            "book-title-usability | title=" + (title.Text ?? string.Empty) +
            " | source=" + (string.Equals(title.Text, project.Name, StringComparison.Ordinal) ? "project-name" : "edition-title") +
            " | alignment=left" +
            " | editable=" + (!title.IsReadOnly && title.IsEnabled && title.IsHitTestVisible && title.Focusable));
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
