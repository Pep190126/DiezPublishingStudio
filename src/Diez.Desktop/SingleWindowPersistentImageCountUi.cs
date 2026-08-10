using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps the exact image quantity visible in the single-window header on every
/// 1/4..4/4 visual-production page. The quantity page remains the editor/source;
/// later pages show the same value as persistent navigation context.
/// </summary>
internal static partial class SingleWindowPersistentImageCountUi
{
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<Control> WiredEditors = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost");
        if (pageHost is null) return;

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Loaded);
            Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Background);
        };
        window.Closed += (_, _) => Attached.Remove(window);
        Refresh(window);
    }

    internal static void Refresh(MainWindow window)
    {
        object host;
        try { host = SingleWindowEntryPointUi.GetHost(window); }
        catch { return; }

        var title = Field<TextBlock>(host, "_title");
        var pageHost = Field<ContentControl>(host, "_pageHost");
        if (title is null || pageHost?.Content is not Control page) return;
        if (!IsFourStepVisualTitle(title.Text)) return;

        var exactNumber = Descendants(page).OfType<NumericUpDown>()
            .FirstOrDefault(x => string.Equals(x.Name, "ExactImageCount", StringComparison.Ordinal));
        if (exactNumber is not null && WiredEditors.Add(exactNumber))
        {
            exactNumber.ValueChanged += (_, _) => Render(title, CountFromNumber(exactNumber) ?? ReadHostCount(host));
        }

        var legacyCount = Descendants(page).OfType<TextBox>().FirstOrDefault(x =>
            !x.AcceptsReturn && int.TryParse((x.Text ?? string.Empty).Trim(), out var n) && n is >= 1 and <= 500);
        if (legacyCount is not null && WiredEditors.Add(legacyCount))
        {
            legacyCount.TextChanged += (_, _) => Render(title, ParseCount(legacyCount.Text) ?? ReadHostCount(host));
        }

        Render(title, CountFromNumber(exactNumber) ?? ReadHostCount(host));
    }

    private static void Render(TextBlock title, int? count)
    {
        if (!count.HasValue || count.Value < 1) return;
        var baseText = CountSuffixRegex().Replace(title.Text ?? string.Empty, string.Empty).TrimEnd();
        title.Text = $"{baseText} · {count.Value} {(count.Value == 1 ? "immagine" : "immagini")}";
    }

    private static bool IsFourStepVisualTitle(string? text)
    {
        var value = text ?? string.Empty;
        return value.Contains("/4", StringComparison.Ordinal) &&
               (value.Contains("Coloring", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("immagin", StringComparison.OrdinalIgnoreCase));
    }

    private static int? ReadHostCount(object host)
    {
        var coloring = host.GetType().GetField("_coloring", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
        var text = coloring?.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public)?.GetValue(coloring)?.ToString();
        return ParseCount(text);
    }

    private static int? CountFromNumber(NumericUpDown? number)
    {
        if (number?.Value is not decimal value) return null;
        var n = (int)value;
        return n is >= 1 and <= 500 ? n : null;
    }

    private static int? ParseCount(string? text) =>
        int.TryParse((text ?? string.Empty).Trim(), out var n) && n is >= 1 and <= 500 ? n : null;

    private static T? Field<T>(object host, string name) where T : class =>
        host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as T;

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

    [GeneratedRegex(@"\s*·\s*\d+\s+immagin(?:e|i)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CountSuffixRegex();
}
