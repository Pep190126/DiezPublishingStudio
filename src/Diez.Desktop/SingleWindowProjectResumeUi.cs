using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// The project loaded in MainWindow remains the active project until the user creates
/// or opens another one (or exits). Returning Home only leaves the workflow view.
/// Re-entering the workflow always resumes from Book Type instead of New/Open project.
/// </summary>
internal static class SingleWindowProjectResumeUi
{
    private static readonly HashSet<MainWindow> Attached = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        if (!TryCommandRow(window, out var row)) return;

        var legacy = row.Children.OfType<Button>().FirstOrDefault(b =>
            string.Equals(b.Content?.ToString(), "Percorso libro", StringComparison.OrdinalIgnoreCase));
        if (legacy is null) return;

        var index = row.Children.IndexOf(legacy);
        var resume = new Button
        {
            Name = "DiezResumeBookWorkflow",
            Width = Math.Max(165, legacy.Width),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ToolTip.SetTip(resume,
            "Il progetto aperto resta attivo. Dalla Home riprendi dalla scelta del Tipo libro, senza creare o riaprire il progetto.");

        void RefreshLabel() => resume.Content = TrySession(window)
            ? "Avanti · Tipo libro"
            : "Percorso libro";

        resume.Click += (_, _) =>
        {
            RefreshLabel();
            if (!TrySession(window))
            {
                SetStatus(window, "Prima crea o apri un progetto .diez.");
                return;
            }
            SingleWindowNativeV11Ui.ShowStart(window);
        };
        resume.PointerEntered += (_, _) => RefreshLabel();
        resume.GotFocus += (_, _) => RefreshLabel();

        row.Children.RemoveAt(index);
        row.Children.Insert(index, resume);

        foreach (var projectButton in row.Children.OfType<Button>().Where(b =>
                     string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(b.Content?.ToString(), "Apri progetto", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(b.Content?.ToString(), "Apri .diez", StringComparison.OrdinalIgnoreCase)))
        {
            projectButton.Click += (_, _) => Dispatcher.UIThread.Post(RefreshLabel, DispatcherPriority.Background);
        }

        window.Opened += (_, _) => RefreshLabel();
        window.Closed += (_, _) => Attached.Remove(window);
        RefreshLabel();
    }

    internal static void Resume(MainWindow window)
    {
        if (TrySession(window)) SingleWindowNativeV11Ui.ShowStart(window);
    }

    internal static bool HasActiveProject(MainWindow window) => TrySession(window);

    private static bool TrySession(MainWindow window)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject;
        var path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private static void SetStatus(MainWindow window, string text)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        if (typeof(MainWindow).GetField("_status", flags)?.GetValue(window) is TextBlock status)
            status.Text = text;
    }

    private static bool TryCommandRow(MainWindow window, out StackPanel row)
    {
        row = null!;
        foreach (var panel in Descendants(window).OfType<StackPanel>())
        {
            if (panel.Orientation != Orientation.Horizontal) continue;
            if (!panel.Children.OfType<Button>().Any(b =>
                    string.Equals(b.Content?.ToString(), "Percorso libro", StringComparison.OrdinalIgnoreCase))) continue;
            row = panel;
            return true;
        }
        return false;
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
