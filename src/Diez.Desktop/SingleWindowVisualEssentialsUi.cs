using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Native, always-visible essentials for every visual-book quantity page.
/// These controls are deliberately independent from later profile decorators so
/// the user always has a concrete image count plus subject/environment instructions.
/// Per-image exceptions can be written directly in the multiline boxes, e.g.
/// "Immagine 3: ambiente cucina".
/// </summary>
internal static class SingleWindowVisualEssentialsUi
{
    private const string PanelName = "DiezVisualEssentialsPanel";
    private static readonly HashSet<MainWindow> Attached = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = PageHost(host);
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            Dispatcher.UIThread.Post(() => EnsureCurrentPage(window), DispatcherPriority.Loaded);
            Dispatcher.UIThread.Post(() => EnsureCurrentPage(window), DispatcherPriority.Background);
        };
        window.Closed += (_, _) => Attached.Remove(window);
        EnsureCurrentPage(window);
    }

    internal static void EnsureCurrentPage(MainWindow window)
    {
        if (!TrySession(window, out var project, out var path)) return;
        if (!BookTypeProfileService.IsImageCollection(project)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        if (PageHost(host).Content is not Control page) return;
        if (!Descendants(page).OfType<TextBlock>().Any(t => (t.Text ?? string.Empty).Contains("Quante immagini vuoi creare?", StringComparison.Ordinal))) return;

        var root = Descendants(page).OfType<StackPanel>().FirstOrDefault(p =>
            p.Children.OfType<TextBlock>().Any(t => (t.Text ?? string.Empty).Contains("quantità", StringComparison.OrdinalIgnoreCase)));
        if (root is null) return;

        if (Descendants(page).Any(c => string.Equals(c.Name, PanelName, StringComparison.Ordinal)))
        {
            HideLegacyProfileEditors(page);
            return;
        }

        var legacyCountLabel = root.Children.OfType<TextBlock>().FirstOrDefault(t =>
            (t.Text ?? string.Empty).Contains("Quante immagini vuoi creare?", StringComparison.Ordinal));
        var legacyCount = FindLegacyCount(root, legacyCountLabel);
        var initialCount = ParseCount(legacyCount?.Text, ReadHostCount(host));
        var count = new NumericUpDown
        {
            Name = "ExactImageCount",
            Value = initialCount,
            Minimum = 1,
            Maximum = 500,
            Increment = 1,
            FormatString = "0",
            Width = 180,
            MinHeight = 42,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(2)
        };

        var (subjectValue, environmentValue) = LoadDescriptions(project);
        var subject = Editor(
            "VisualSubjectInstructions",
            subjectValue,
            "Descrivi personaggio/i o soggetto/i. Puoi anche indicare eccezioni per singola immagine, es. “Immagine 3: la bambina indossa un grembiule; immagine 5: compare anche il gatto”.");
        var environment = Editor(
            "VisualEnvironmentInstructions",
            environmentValue,
            "Descrivi ambientazione/scenario. Puoi indicare variazioni locali, es. “Immagine 1: parco; immagine 3: cucina; immagini 6–8: palestra”.");

        void PushCount()
        {
            var value = Math.Clamp((int)(count.Value ?? 1), 1, 500);
            SetHostCount(host, value.ToString());
            if (legacyCount is not null && legacyCount.Text != value.ToString())
                legacyCount.Text = value.ToString();
        }

        void SaveDescriptionsInModel()
        {
            var subjectText = subject.Text ?? string.Empty;
            var environmentText = environment.Text ?? string.Empty;
            if (string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
            {
                var p = BookTypePromptProfileService.LoadColoring(project);
                p.SubjectDescription = subjectText;
                p.EnvironmentDescription = environmentText;
                BookTypePromptProfileService.SaveColoring(project, p);
            }
            else
            {
                var p = ImageCollectionPromptProfileService.Load(project);
                p.SubjectDescription = subjectText;
                p.EnvironmentDescription = environmentText;
                ImageCollectionPromptProfileService.Save(project, p);
            }
        }

        void SyncHiddenProfileEditors()
        {
            var subjectName = string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)
                ? "ColoringSubjectDescription" : "ImageCollectionSubject";
            var environmentName = string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)
                ? "ColoringEnvironmentDescription" : "ImageCollectionEnvironment";
            var legacySubject = Descendants(page).OfType<TextBox>().FirstOrDefault(x => x.Name == subjectName);
            var legacyEnvironment = Descendants(page).OfType<TextBox>().FirstOrDefault(x => x.Name == environmentName);
            if (legacySubject is not null && legacySubject.Text != subject.Text) legacySubject.Text = subject.Text;
            if (legacyEnvironment is not null && legacyEnvironment.Text != environment.Text) legacyEnvironment.Text = environment.Text;
        }

        async Task PersistDescriptionsAsync(string source)
        {
            SaveDescriptionsInModel();
            SyncHiddenProfileEditors();
            await SafeProjectAutosave.SaveAsync(path, project, source);
        }

        count.ValueChanged += (_, _) => PushCount();
        subject.TextChanged += (_, _) => SaveDescriptionsInModel();
        environment.TextChanged += (_, _) => SaveDescriptionsInModel();
        subject.LostFocus += async (_, _) => await PersistDescriptionsAsync("visual-subject");
        environment.LostFocus += async (_, _) => await PersistDescriptionsAsync("visual-environment");

        var panel = new Border
        {
            Name = PanelName,
            Padding = new Thickness(12),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(2),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Dati essenziali delle immagini", FontSize = 20 },
                    new TextBlock
                    {
                        Text = "Questi campi sono sempre disponibili per Coloring, Raccolta immagini e Libro illustrato. Le indicazioni “Immagine N” sono eccezioni locali e valgono solo per quella immagine.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    Labeled("Numero esatto di immagini da generare", count),
                    Labeled("Personaggio/i, soggetto/i ed eventuali variazioni per singola immagine", subject),
                    Labeled("Ambientazione / scenario ed eventuali variazioni per singola immagine", environment)
                }
            }
        };

        var insertAt = legacyCountLabel is null ? Math.Min(2, root.Children.Count) : root.Children.IndexOf(legacyCountLabel);
        root.Children.Insert(Math.Max(0, insertAt), panel);
        if (legacyCountLabel is not null) legacyCountLabel.IsVisible = false;
        if (legacyCount is not null) legacyCount.IsVisible = false;
        PushCount();
        HideLegacyProfileEditors(page);

        var actions = root.Children.OfType<StackPanel>().LastOrDefault(p =>
            p.Orientation == Orientation.Horizontal && p.Children.OfType<Button>().Any());
        var next = actions?.Children.OfType<Button>().FirstOrDefault(b =>
            (b.Content?.ToString() ?? string.Empty).Contains("Avanti", StringComparison.OrdinalIgnoreCase));
        if (next is not null)
            next.Click += async (_, _) => await PersistDescriptionsAsync("visual-essentials-next");
    }

    private static TextBox? FindLegacyCount(StackPanel root, TextBlock? label)
    {
        if (label is not null)
        {
            var index = root.Children.IndexOf(label);
            for (var i = index + 1; i < root.Children.Count && i <= index + 3; i++)
                if (root.Children[i] is TextBox box && !box.AcceptsReturn) return box;
        }
        return root.Children.OfType<TextBox>().FirstOrDefault(x => !x.AcceptsReturn && !x.IsReadOnly);
    }

    private static void HideLegacyProfileEditors(Control page)
    {
        foreach (var name in new[] { "ColoringSubjectDescription", "ColoringEnvironmentDescription", "ImageCollectionSubject", "ImageCollectionEnvironment" })
        {
            var box = Descendants(page).OfType<TextBox>().FirstOrDefault(x => x.Name == name);
            if (box is null) continue;
            box.IsVisible = false;
            if (box.Parent is StackPanel parent)
            {
                var index = parent.Children.IndexOf(box);
                if (index > 0 && parent.Children[index - 1] is TextBlock label &&
                    ((label.Text ?? string.Empty).Contains("Soggetto", StringComparison.OrdinalIgnoreCase) ||
                     (label.Text ?? string.Empty).Contains("Ambiente", StringComparison.OrdinalIgnoreCase)))
                    label.IsVisible = false;
            }
        }
    }

    private static (string Subject, string Environment) LoadDescriptions(PreviewProject project)
    {
        if (string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            var p = BookTypePromptProfileService.LoadColoring(project);
            return (p.SubjectDescription, p.EnvironmentDescription);
        }
        var i = ImageCollectionPromptProfileService.Load(project);
        return (i.SubjectDescription, i.EnvironmentDescription);
    }

    private static TextBox Editor(string name, string value, string watermark) => new()
    {
        Name = name,
        Text = value,
        MinHeight = 105,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Watermark = watermark,
        IsReadOnly = false,
        IsEnabled = true,
        IsHitTestVisible = true,
        Focusable = true,
        IsUndoEnabled = true,
        Background = Brushes.White,
        Foreground = Brushes.Black,
        BorderBrush = Brushes.Gray,
        BorderThickness = new Thickness(2),
        Padding = new Thickness(9, 7),
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static StackPanel Labeled(string label, Control control) => new()
    {
        Spacing = 4,
        Children = { new TextBlock { Text = label, FontSize = 15, TextWrapping = TextWrapping.Wrap }, control }
    };

    private static int ParseCount(string? first, string? second)
    {
        if (int.TryParse((first ?? string.Empty).Trim(), out var n) && n is >= 1 and <= 500) return n;
        if (int.TryParse((second ?? string.Empty).Trim(), out n) && n is >= 1 and <= 500) return n;
        return 1;
    }

    private static string ReadHostCount(object host)
    {
        var coloring = host.GetType().GetField("_coloring", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
        return coloring?.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public)?.GetValue(coloring)?.ToString() ?? string.Empty;
    }

    private static void SetHostCount(object host, string value)
    {
        var coloring = host.GetType().GetField("_coloring", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
        coloring?.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public)?.SetValue(coloring, value);
    }

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
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
