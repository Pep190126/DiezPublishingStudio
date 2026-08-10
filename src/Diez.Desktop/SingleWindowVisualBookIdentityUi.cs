using System.Reflection;
using Avalonia.Controls;

namespace DiezPublishingStudio;

/// <summary>
/// The current image-series host was born as the Coloring vertical slice.
/// This adapter keeps its visible identity aligned when the same mechanics are reused
/// by Image Collection or Illustrated Book, without duplicating navigation code.
/// </summary>
internal static class SingleWindowVisualBookIdentityUi
{
    private static readonly HashSet<TextBox> AttachedPromptEditors = [];

    public static void Attach(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost is null) return;
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty) Apply(window);
        };
        window.Closed += (_, _) => AttachedPromptEditors.Clear();
        Apply(window);
    }

    internal static void Apply(MainWindow window)
    {
        if (!TryProject(window, out var project)) return;
        var type = BookTypeProfileService.Get(project);
        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(type, BookTypeProfileService.ImageCollection, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase)) return;

        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page) return;

        var isIllustrated = string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase);
        var visibleName = isIllustrated ? "Libro illustrato · Illustrazioni" : "Raccolta immagini";

        var title = host.GetType().GetField("_title", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as TextBlock;
        if (title is not null && (title.Text ?? string.Empty).Contains("Coloring Book", StringComparison.Ordinal))
            title.Text = (title.Text ?? string.Empty).Replace("Coloring Book", visibleName, StringComparison.Ordinal);

        foreach (var text in Descendants(page).OfType<TextBlock>())
        {
            var value = text.Text ?? string.Empty;
            if (value.Contains("Coloring Book — quantità e coerenza", StringComparison.Ordinal))
                text.Text = isIllustrated
                    ? "Libro illustrato — quantità e coerenza delle illustrazioni"
                    : "Raccolta immagini — quantità e coerenza";
            else if (value.Contains("Coloring Book", StringComparison.Ordinal) && value.Contains("quantità", StringComparison.OrdinalIgnoreCase))
                text.Text = value.Replace("Coloring Book", visibleName, StringComparison.Ordinal);
        }

        var editors = Descendants(page).OfType<TextBox>()
            .Where(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly)
            .ToList();
        if (editors.Count >= 3)
        {
            var prompt = editors[2];
            RewritePrompt(prompt, isIllustrated);
            if (AttachedPromptEditors.Add(prompt))
                prompt.TextChanged += (_, _) => RewritePrompt(prompt, isIllustrated);
        }
    }

    private static void RewritePrompt(TextBox prompt, bool illustrated)
    {
        var text = prompt.Text ?? string.Empty;
        if (!text.Contains("Coloring Book", StringComparison.OrdinalIgnoreCase)) return;

        var rewritten = illustrated
            ? text.Replace("per un Coloring Book", "per le illustrazioni di un Libro illustrato", StringComparison.OrdinalIgnoreCase)
                  .Replace("Output Coloring Book", "Output illustrazioni Libro illustrato", StringComparison.OrdinalIgnoreCase)
            : text.Replace("per un Coloring Book", "per una Raccolta immagini", StringComparison.OrdinalIgnoreCase)
                  .Replace("Output Coloring Book", "Output Raccolta immagini", StringComparison.OrdinalIgnoreCase);

        // Never carry Coloring-only binary-color instructions into a non-Coloring project.
        rewritten = RemoveColoringBinaryLines(rewritten);
        if (!string.Equals(rewritten, text, StringComparison.Ordinal)) prompt.Text = rewritten;
    }

    private static string RemoveColoringBinaryLines(string text)
    {
        var lines = text.Split('\n');
        var kept = lines.Where(line =>
        {
            var s = line.Trim();
            if (s.Contains("ESATTAMENTE due colori", StringComparison.OrdinalIgnoreCase)) return false;
            if (s.Contains("ESATTAMENTE DUE SOLI COLORI", StringComparison.OrdinalIgnoreCase)) return false;
            if (s.Contains("Vietati senza eccezioni", StringComparison.OrdinalIgnoreCase) && s.Contains("grigi", StringComparison.OrdinalIgnoreCase)) return false;
            if (s.Contains("VINCOLO CROMATICO ASSOLUTO", StringComparison.OrdinalIgnoreCase)) return false;
            if (s.Contains("nessun terzo valore cromatico", StringComparison.OrdinalIgnoreCase)) return false;
            if (s.Contains("normalizzato/binarizzato", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        });
        return string.Join("\n", kept).Trim();
    }

    private static bool TryProject(MainWindow window, out PreviewProject project)
    {
        project = typeof(MainWindow).GetField("_project", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as PreviewProject ?? null!;
        return project is not null;
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
                case Panel p:
                    for (var i = p.Children.Count - 1; i >= 0; i--) stack.Push(p.Children[i]);
                    break;
                case Border b when b.Child is Control child: stack.Push(child); break;
                case ScrollViewer s when s.Content is Control child: stack.Push(child); break;
                case ContentControl c when c.Content is Control child: stack.Push(child); break;
            }
        }
    }
}
