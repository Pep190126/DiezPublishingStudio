using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;

namespace DiezPublishingStudio;

/// <summary>
/// Starts the native SW-FLOW-11 logical workflow inside the existing physical MainWindow.
/// </summary>
internal static class SingleWindowV5StartupUi
{
    public const string Marker = SingleWindowNativeV11Ui.Marker;

    public static void Attach(MainWindow window)
    {
        window.KeyDown += HandleEditorShortcuts;
        window.Closed += (_, _) => window.KeyDown -= HandleEditorShortcuts;
        window.Opened += async (_, _) =>
        {
            // Headless CI constructs the project/session explicitly inside each contract probe. Do not leave a
            // delayed Opened continuation alive past the isolated Avalonia test session cleanup.
            if (Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--ui-headless-ci", StringComparison.OrdinalIgnoreCase)))
            {
                ShowStart(window);
                return;
            }

            // A project passed on the command line may finish loading just after Window.Opened.
            for (var i = 0; i < 8 && !TrySession(window); i++) await Task.Delay(40);
            ShowStart(window);
        };
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

    private static bool TrySession(MainWindow window)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject;
        var path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string;
        return project is not null && !string.IsNullOrWhiteSpace(path);
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
