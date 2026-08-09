using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// One MainWindow, one permanent set of book tabs. Product-specific workspaces
/// only replace controls hosted inside those tabs; stable sub-tabs organize
/// complex product areas without introducing new top-level windows.
/// </summary>
internal static class UnifiedBookWorkspaceUi
{
    private static readonly string[] Headers = ["Database", "Tipo libro", "Controlli", "AI", "Esporta"];

    public static void Attach(MainWindow window)
    {
        CrosswordThemeUi.Attach(window);

        TabControl? host = null;
        List<TabItem>? tabs = null;
        List<object?>? wordSearchContents = null;
        ImageCollectionTabWorkspace? imageWorkspace = null;
        string activeProfile = string.Empty;
        string activeSignature = string.Empty;
        bool refreshing = false;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        timer.Tick += async (_, _) =>
        {
            if (host is null || tabs is null)
            {
                host = FindBookTabs(window, out tabs);
                if (host is null || tabs is null) return;
                wordSearchContents = tabs.Select(t => t.Content).ToList();
                ApplyCanonicalHeaders(tabs);
            }

            var project = TryGetProject(window);
            var profile = ResolveProfile(project);
            var signature = ContentSignature(project, profile);
            var shouldRebuild = !string.Equals(profile, activeProfile, StringComparison.Ordinal) ||
                                ((profile is "novel" or "illustrated" or "generic" or "none") &&
                                 !string.Equals(signature, activeSignature, StringComparison.Ordinal));

            if (shouldRebuild)
            {
                ApplyCanonicalHeaders(tabs);
                foreach (var tab in tabs) tab.IsVisible = true;

                switch (profile)
                {
                    case "word-search":
                        if (wordSearchContents is not null) SetContents(tabs, wordSearchContents);
                        break;

                    case "images":
                        imageWorkspace ??= new ImageCollectionTabWorkspace(window);
                        SetContents(tabs, imageWorkspace.Contents.Cast<object?>().ToList());
                        break;

                    case "novel":
                    case "illustrated":
                        if (project is not null)
                            SetContents(tabs, NarrativeWorkspaceSubtabs.Build(window, project, profile == "illustrated"));
                        break;

                    case "crossword":
                        if (project is not null)
                            SetContents(tabs, CrosswordWorkspaceUi.Build(window, project));
                        break;

                    default:
                        SetContents(tabs, BuildGenericContents(project));
                        break;
                }

                activeProfile = profile;
                activeSignature = signature;
                if (host.SelectedIndex < 0) host.SelectedIndex = 0;
            }

            if (profile == "images" && imageWorkspace is not null && !refreshing)
            {
                refreshing = true;
                try { await imageWorkspace.RefreshAsync(); }
                finally { refreshing = false; }
            }
        };

        window.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private static string ResolveProfile(PreviewProject? project)
    {
        if (project is null) return "none";
        var type = BookTypeProfileService.Get(project);
        if (BookTypeProfileService.IsImageCollection(project) ||
            string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
            return "images";
        if (string.Equals(type, BookTypeProfileService.Crossword, StringComparison.OrdinalIgnoreCase)) return "crossword";
        if (string.Equals(type, BookTypeProfileService.Novel, StringComparison.OrdinalIgnoreCase)) return "novel";
        if (string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase)) return "illustrated";
        if (BookTypeRecognition.IsWordSearch(project)) return "word-search";
        return "generic";
    }

    private static string ContentSignature(PreviewProject? project, string profile)
    {
        if (project is null) return "none";
        if (profile is not ("novel" or "illustrated" or "generic")) return profile;
        return string.Join("|",
            project.ProjectId,
            project.Materials.Count,
            project.ContentNodes.Count,
            project.Entities.Count,
            project.BibleEntries.Count,
            project.Relations.Count,
            project.ConsistencyFacts.Count,
            project.ConsistencyIssues.Count,
            BookTypeProfileService.Get(project));
    }

    private static IReadOnlyList<object?> BuildGenericContents(PreviewProject? project)
    {
        var prefix = project is null
            ? "Crea o apri un progetto per vedere gli strumenti del Tipo libro."
            : $"Tipo libro: {BookTypeProfileService.Get(project)}.";
        return
        [
            MessagePanel("Database", prefix + " I dati e i materiali pertinenti compariranno qui."),
            MessagePanel("Tipo libro", prefix + " I controlli specifici compaiono solo quando sono pertinenti."),
            MessagePanel("Controlli", prefix + " Qui compariranno problemi e verifiche con riferimenti comprensibili."),
            MessagePanel("AI", prefix + " Qui restano le scelte AI, API o prompt pack, senza finestre operative separate."),
            MessagePanel("Esporta", prefix + " Qui restano le opzioni di output e destinazione.")
        ];
    }

    private static Control MessagePanel(string title, string text) => new StackPanel
    {
        Margin = new Thickness(12),
        Spacing = 10,
        Children =
        {
            new TextBlock { Text = title, FontSize = 21 },
            new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
        }
    };

    private static void ApplyCanonicalHeaders(IReadOnlyList<TabItem> tabs)
    {
        for (var i = 0; i < tabs.Count && i < Headers.Length; i++) tabs[i].Header = Headers[i];
    }

    private static void SetContents(IReadOnlyList<TabItem> tabs, IReadOnlyList<object?> contents)
    {
        for (var i = 0; i < tabs.Count && i < contents.Count; i++) tabs[i].Content = contents[i];
    }

    private static TabControl? FindBookTabs(Control root, out List<TabItem>? bookTabs)
    {
        foreach (var tabControl in Descendants(root).OfType<TabControl>())
        {
            if (tabControl.ItemsSource is not IEnumerable<TabItem> source) continue;
            var items = source.ToList();
            if (items.Count < 6 || !string.Equals(items[0].Header?.ToString(), "Progetto", StringComparison.Ordinal)) continue;
            bookTabs = items.Skip(1).Take(5).ToList();
            return tabControl;
        }
        bookTabs = null;
        return null;
    }

    private static PreviewProject? TryGetProject(MainWindow window) =>
        typeof(MainWindow).GetField("_project", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as PreviewProject;

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
