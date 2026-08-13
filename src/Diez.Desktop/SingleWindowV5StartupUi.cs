using Avalonia.Controls;
using Avalonia.Input;

namespace DiezPublishingStudio;

/// <summary>
/// Starts the native SW-FLOW-11 logical workflow inside the existing physical MainWindow.
/// Normal desktop startup stays on Home and enters the workflow only from the explicit navigation command.
/// Headless CI keeps automatic entry so deterministic contracts retain coverage.
/// </summary>
internal static class SingleWindowV5StartupUi
{
    public const string Marker = SingleWindowNativeV11Ui.Marker;

    public static void Attach(MainWindow window)
    {
        window.KeyDown += HandleEditorShortcuts;
        window.Closed += (_, _) => window.KeyDown -= HandleEditorShortcuts;

        if (Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--ui-headless-ci", StringComparison.OrdinalIgnoreCase)))
            window.Opened += (_, _) => ShowStart(window);
    }

    internal static void ShowStart(MainWindow window)
    {
        SingleWindowNativeV11Ui.ShowStart(window);
        ReplaceMarker(window);
    }

    private static void HandleEditorShortcuts(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
        if (e.Source is not TextBox editor || editor.IsReadOnly || !editor.IsEnabled || !editor.IsUndoEnabled) return;
        if (e.Key == Key.Z)
        {
            if (editor.CanUndo) editor.Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y)
        {
            if (editor.CanRedo) editor.Redo();
            e.Handled = true;
        }
    }

    private static void ReplaceMarker(MainWindow window)
    {
        foreach (var text in Descendants(window).OfType<TextBlock>())
        {
            var value = text.Text ?? string.Empty;
            if (value.Contains("Diez single-window · SW-FLOW-", StringComparison.Ordinal))
                text.Text = $"Diez single-window · {Marker}";
        }
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
