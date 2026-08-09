using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps DEVE FARE / NON DEVE FARE / PROMPT writable and keyboard-editable.
/// Avalonia TextBox provides native copy, undo and redo shortcuts when the box is
/// editable and IsUndoEnabled is true.
/// </summary>
internal static class HumanAiPromptInputGuard
{
    public static void Attach(MainWindow mainWindow)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) =>
        {
            foreach (var window in desktop.Windows.ToList())
            {
                if (window is not (AiProductionWindow or AiJobEditorWindow or SimpleAiCreationWindow)) continue;
                Repair(window);
            }
        };
        mainWindow.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    internal static void Repair(Window window)
    {
        // DEVE FARE.
        if (GetPrivate<TextBox>(window, "_request") is TextBox request)
            MakeEditable(request);

        // PROMPT / ISTRUZIONI: now intentionally editable by the user.
        if (GetPrivate<TextBox>(window, "_prompt") is TextBox prompt)
            MakeEditable(prompt);
        if (GetPrivate<TextBox>(window, "_instructions") is TextBox instructions)
            MakeEditable(instructions);

        // NON DEVE FARE is injected dynamically, so identify it by label/watermark.
        foreach (var box in Descendants(window).OfType<TextBox>())
        {
            if (HasHumanPromptLabel(box) || HasHumanPromptWatermark(box))
                MakeEditable(box);
        }
    }

    private static bool HasHumanPromptWatermark(TextBox box)
    {
        var text = box.Watermark ?? string.Empty;
        return text.Contains("deve ottenere", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("deve evitare", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("non deve fare", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("cosa vuoi che l'AI faccia", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("cosa deve evitare", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("concreto ciò che vuoi ottenere", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Prompt pronto", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasHumanPromptLabel(TextBox box)
    {
        foreach (var panel in AncestorPanels(box))
        {
            var index = panel.Children.IndexOf(box);
            if (index <= 0) continue;
            if (panel.Children[index - 1] is not TextBlock label) continue;
            var text = (label.Text ?? string.Empty).Trim();
            if (text.Equals("DEVE FARE", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("NON DEVE FARE", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("istruzioni", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("prompt", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal static void MakeEditable(TextBox box)
    {
        box.IsReadOnly = false;
        box.IsEnabled = true;
        box.IsHitTestVisible = true;
        box.Focusable = true;
        box.IsUndoEnabled = true;
    }

    private static IEnumerable<Panel> AncestorPanels(Control control)
    {
        var current = control.Parent;
        while (current is not null)
        {
            if (current is Panel panel) yield return panel;
            current = current.Parent;
        }
    }

    private static T? GetPrivate<T>(object instance, string fieldName) where T : class =>
        instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as T;

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        if (root is Panel panel)
            foreach (var child in panel.Children.SelectMany(Descendants)) yield return child;
        if (root is Border border && border.Child is Control borderChild)
            foreach (var child in Descendants(borderChild)) yield return child;
        if (root is ScrollViewer scroll && scroll.Content is Control scrollChild)
            foreach (var child in Descendants(scrollChild)) yield return child;
        if (root is ContentControl content && content.Content is Control contentChild)
            foreach (var child in Descendants(contentChild)) yield return child;
    }
}
