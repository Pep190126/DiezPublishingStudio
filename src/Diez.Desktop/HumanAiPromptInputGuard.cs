using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps the human-facing "DEVE FARE / NON DEVE FARE" editors writable.
/// These fields are injected on top of older AI windows, so other compatibility
/// layers must never leave them read-only, disabled or unable to receive input.
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
        // The original request box is the DEVE FARE editor in all three AI windows.
        if (GetPrivate<TextBox>(window, "_request") is TextBox request)
            MakeEditable(request);

        // NON DEVE FARE is injected dynamically, therefore find it by its visible
        // label/watermark instead of relying on a private field that does not exist.
        foreach (var box in Descendants(window).OfType<TextBox>())
        {
            if (IsTechnicalReadOnlyBox(window, box)) continue;
            if (HasHumanPromptLabel(box) || HasHumanPromptWatermark(box))
                MakeEditable(box);
        }
    }

    private static bool IsTechnicalReadOnlyBox(Window window, TextBox box)
    {
        var prompt = GetPrivate<TextBox>(window, "_prompt");
        var instructions = GetPrivate<TextBox>(window, "_instructions");
        return ReferenceEquals(box, prompt) || ReferenceEquals(box, instructions);
    }

    private static bool HasHumanPromptWatermark(TextBox box)
    {
        var text = box.Watermark ?? string.Empty;
        return text.Contains("deve ottenere", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("deve evitare", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("non deve fare", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("cosa vuoi che l'AI faccia", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("cosa deve evitare", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("concreto ciò che vuoi ottenere", StringComparison.OrdinalIgnoreCase);
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
                text.Equals("NON DEVE FARE", StringComparison.OrdinalIgnoreCase))
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
