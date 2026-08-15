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
        var field = Descendants(page).OfType<StackPanel>().FirstOrDefault(c => c.Name == "DiezBookTitleField");
        if (title is null || field is null) return;

        if (string.IsNullOrWhiteSpace(project.EditionMetadata.Title))
        {
            project.EditionMetadata.Title = project.Name;
            title.Text = project.Name;
        }
        else if (string.IsNullOrWhiteSpace(title.Text))
        {
            title.Text = project.EditionMetadata.Title;
        }

        // Never let the visible editor extend beyond the mounted pageHost input region. The previous fixed
        // 620 px width overflowed the real 564 px pageHost on Windows, so the geometric centre used by a
        // physical mouse click could lie outside the page subtree even though the TextBox was visibly drawn.
        const double preferredWidth = 620;
        const double horizontalSafety = 16;
        var mountedWidth = pageHost.Bounds.Width;
        var width = mountedWidth > horizontalSafety
            ? Math.Min(preferredWidth, mountedWidth - horizontalSafety)
            : Math.Min(preferredWidth, 520);

        field.Width = width;
        field.MaxWidth = preferredWidth;
        field.HorizontalAlignment = HorizontalAlignment.Left;
        field.IsEnabled = true;
        field.IsHitTestVisible = true;
        title.Width = width;
        title.MaxWidth = preferredWidth;
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.TextAlignment = TextAlignment.Left;
        title.Background = Brushes.White;
        title.Foreground = Brushes.Black;
        title.BorderBrush = Brushes.Gray;
        title.BorderThickness = new Avalonia.Thickness(2);

        title.IsReadOnly = false;
        title.IsEnabled = true;
        title.IsHitTestVisible = true;
        title.Focusable = true;
        title.IsUndoEnabled = true;

        if (WiredTitles.Add(title))
        {
            title.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
            {
                SafeStartupTrace.Write(
                    "book-title-input | event=pointer-pressed" +
                    " | focused=" + title.IsFocused +
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
            " | mountedWidth=" + mountedWidth.ToString("0.##") +
            " | editorWidth=" + width.ToString("0.##") +
            " | withinMountedPage=" + (mountedWidth <= 0 || width <= mountedWidth) +
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
