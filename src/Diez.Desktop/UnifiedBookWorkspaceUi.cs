using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// One MainWindow, one permanent set of book tabs. Product-specific workspaces
/// only replace the controls hosted inside those tabs; they never create a new
/// top-level window.
/// </summary>
internal static class UnifiedBookWorkspaceUi
{
    private static readonly string[] Headers = ["Database", "Tipo libro", "Controlli", "AI", "Esporta"];

    public static void Attach(MainWindow window)
    {
        TabControl? host = null;
        List<TabItem>? tabs = null;
        List<object?>? wordSearchContents = null;
        ImageCollectionTabWorkspace? imageWorkspace = null;
        string activeProfile = string.Empty;
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
            if (!string.Equals(profile, activeProfile, StringComparison.Ordinal))
            {
                ApplyCanonicalHeaders(tabs);
                foreach (var tab in tabs) tab.IsVisible = true;

                switch (profile)
                {
                    case "word-search":
                        if (wordSearchContents is not null)
                            SetContents(tabs, wordSearchContents);
                        break;

                    case "images":
                        imageWorkspace ??= new ImageCollectionTabWorkspace(window);
                        SetContents(tabs, imageWorkspace.Contents.Cast<object?>().ToList());
                        break;

                    case "novel":
                    case "illustrated":
                        if (project is not null)
                            SetContents(tabs, BuildNarrativeContents(window, project, profile == "illustrated"));
                        break;

                    default:
                        SetContents(tabs, BuildGenericContents(project));
                        break;
                }

                activeProfile = profile;
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
        if (string.Equals(type, BookTypeProfileService.Novel, StringComparison.OrdinalIgnoreCase)) return "novel";
        if (string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase)) return "illustrated";
        if (BookTypeRecognition.IsWordSearch(project)) return "word-search";
        return "generic";
    }

    private static IReadOnlyList<object?> BuildNarrativeContents(MainWindow window, PreviewProject project, bool illustrated)
    {
        var typeName = illustrated ? "Libro illustrato" : "Romanzo / racconto";
        var manuscripts = project.Materials
            .Where(m => !IllustrationPlanService.IsImage(m))
            .OrderBy(m => m.ImportedAtLocal, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var chapters = project.ContentNodes.Count(n => string.Equals(n.Kind, "Chapter", StringComparison.OrdinalIgnoreCase));
        var scenes = project.ContentNodes.Count(n => string.Equals(n.Kind, "Scene", StringComparison.OrdinalIgnoreCase));
        var images = project.Materials.Count(IllustrationPlanService.IsImage);

        Control Database()
        {
            var list = new ListBox
            {
                ItemsSource = manuscripts.Count == 0
                    ? ["Nessun manoscritto importato."]
                    : manuscripts.Select((m, i) => $"{i + 1}. {m.FileName} · {FormatBytes(m.SizeBytes)}").ToList()
            };
            return new Grid
            {
                Margin = new Thickness(10),
                RowDefinitions = new RowDefinitions("Auto,Auto,*"),
                RowSpacing = 8,
                Children =
                {
                    new TextBlock { Text = illustrated ? "Manoscritti e materiali del libro illustrato" : "Manoscritti del romanzo", FontSize = 20 },
                    new TextBlock
                    {
                        Text = $"Materiali testuali: {manuscripts.Count} · Capitoli riconosciuti: {chapters} · Scene riconosciute: {scenes}" + (illustrated ? $" · Immagini: {images}" : string.Empty),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }.WithGridRow(1),
                    list.WithGridRow(2)
                }
            };
        }

        Control TypeBook()
        {
            var editor = BookTypeAiOptionsService.BuildEditor(project, () => SaveCurrent(window, project));
            return new ScrollViewer
            {
                Margin = new Thickness(8),
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = typeName, FontSize = 22 },
                        new TextBlock
                        {
                            Text = "I controlli compaiono solo quando servono. Se non conosci ancora struttura e pagine, Diez le ricaverà dal progetto invece di obbligarti a inventare numeri prima del tempo.",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        editor
                    }
                }
            };
        }

        Control Checks() => MessagePanel(
            "Controlli del progetto",
            chapters == 0
                ? "Quando avrai aggiunto i manoscritti, qui Diez mostrerà struttura, continuità, ordine, contraddizioni e punti che richiedono una decisione, usando capitolo/scena/paragrafo invece di ID tecnici."
                : $"Struttura corrente: {chapters} capitoli e {scenes} scene. I controlli specifici del romanzo verranno mostrati qui, nello stesso spazio, senza aprire altre finestre.");

        Control Ai() => MessagePanel(
            "AI",
            "DEVE FARE / NON DEVE FARE, scelta API o prompt pack e provider disponibili devono vivere qui. Le proposte AI restano da controllare e non sostituiscono automaticamente il testo del progetto.");

        Control Export() => MessagePanel(
            "Esporta",
            "Documento modificabile (DOCX), Google Documenti e gli altri output del progetto vengono gestiti qui. La destinazione non cambia la versione editoriale da cui nasce l'output.");

        return [Database(), TypeBook(), Checks(), Ai(), Export()];
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

    private static void SaveCurrent(MainWindow window, PreviewProject project)
    {
        var path = typeof(MainWindow).GetField("_currentProjectPath", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as string;
        if (string.IsNullOrWhiteSpace(path)) return;
        _ = SaveSafeAsync(path, project);
    }

    private static async Task SaveSafeAsync(string path, PreviewProject project)
    {
        try { await ProjectFileStore.SaveAsync(path, project); }
        catch { }
    }

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

    private static string FormatBytes(long value) => value switch
    {
        < 1024 => $"{value} B",
        < 1024 * 1024 => $"{value / 1024d:0.0} KB",
        _ => $"{value / 1024d / 1024d:0.0} MB"
    };

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
