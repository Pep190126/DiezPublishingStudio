using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class BookTypeProfileService
{
    private const string EntityKind = "DiezBookType";

    public const string WordSearch = "Word Search";
    public const string Crossword = "Cruciverba";
    public const string Quiz = "Quiz / trivia";
    public const string ColoringBook = "Coloring book";
    public const string ImageCollection = "Raccolta immagini";
    public const string Novel = "Romanzo / racconto";
    public const string EssayManual = "Saggio / manuale";
    public const string IllustratedBook = "Libro illustrato";
    public const string DataCollection = "Catalogo / raccolta dati";
    public const string Other = "Altro";

    public static string Get(PreviewProject project)
    {
        var stored = project.Entities
            .FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (stored is not null && !string.IsNullOrWhiteSpace(stored.Name))
            return Normalize(stored.Name);
        return Infer(project);
    }

    public static void Set(PreviewProject project, string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized)) return;
        var matches = project.Entities
            .Where(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var entity = matches.FirstOrDefault();
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = EntityKind,
                Name = normalized,
                IsCandidate = false,
                Notes = "Tipo di libro scelto dall'utente. Usato per mostrare l'ambiente editoriale corretto."
            };
            project.Entities.Add(entity);
        }
        else
        {
            entity.Name = normalized;
            entity.IsCandidate = false;
        }
        foreach (var duplicate in matches.Skip(1)) project.Entities.Remove(duplicate);
    }

    /// <summary>
    /// Types that need the common image-series workflow. Illustrated books share
    /// the illustration controls with Image Collection, but keep their own book type.
    /// </summary>
    public static bool IsImageCollection(PreviewProject project)
    {
        var type = Get(project);
        return string.Equals(type, ColoringBook, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, ImageCollection, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, IllustratedBook, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsWordSearch(PreviewProject project) =>
        string.Equals(Get(project), WordSearch, StringComparison.OrdinalIgnoreCase);

    public static bool IsCrossword(PreviewProject project) =>
        string.Equals(Get(project), Crossword, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return string.Empty;
        if (text.Equals(Crossword, StringComparison.OrdinalIgnoreCase) ||
            text.Contains("cruciverba", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("crossword", StringComparison.OrdinalIgnoreCase)) return Crossword;
        if (text.Equals("Puzzle / giochi di parole", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("word search", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("wordsearch", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("cerca parole", StringComparison.OrdinalIgnoreCase)) return WordSearch;
        if (text.Equals(ColoringBook, StringComparison.OrdinalIgnoreCase) || text.Contains("coloring", StringComparison.OrdinalIgnoreCase)) return ColoringBook;
        if (text.Equals(ImageCollection, StringComparison.OrdinalIgnoreCase) || text.Contains("raccolta immagini", StringComparison.OrdinalIgnoreCase) || text.Contains("image collection", StringComparison.OrdinalIgnoreCase)) return ImageCollection;
        if (text.Equals(EssayManual, StringComparison.OrdinalIgnoreCase) || text.Contains("saggio", StringComparison.OrdinalIgnoreCase) || text.Contains("manuale", StringComparison.OrdinalIgnoreCase) || text.Contains("essay", StringComparison.OrdinalIgnoreCase)) return EssayManual;
        if (text.Equals(Novel, StringComparison.OrdinalIgnoreCase) || text.Contains("romanzo", StringComparison.OrdinalIgnoreCase) || text.Contains("racconto", StringComparison.OrdinalIgnoreCase)) return Novel;
        if (text.Equals(IllustratedBook, StringComparison.OrdinalIgnoreCase) || text.Contains("illustrato", StringComparison.OrdinalIgnoreCase)) return IllustratedBook;
        if (text.Equals(Quiz, StringComparison.OrdinalIgnoreCase) || text.Contains("quiz", StringComparison.OrdinalIgnoreCase) || text.Contains("trivia", StringComparison.OrdinalIgnoreCase)) return Quiz;
        if (text.Equals(DataCollection, StringComparison.OrdinalIgnoreCase) || text.Contains("raccolta dati", StringComparison.OrdinalIgnoreCase) || text.Contains("catalogo", StringComparison.OrdinalIgnoreCase)) return DataCollection;
        return Other;
    }

    private static string Infer(PreviewProject project)
    {
        if (project.Entities.Any(e => string.Equals(e.Kind, "CrosswordWord", StringComparison.OrdinalIgnoreCase))) return Crossword;
        if (WordSearchWorkspaceService.HasWordSearchDatabase(project)) return WordSearch;
        var combined = $"{project.Name} {project.EditionMetadata?.Title}";
        if (combined.Contains("cruciverba", StringComparison.OrdinalIgnoreCase) || combined.Contains("crossword", StringComparison.OrdinalIgnoreCase)) return Crossword;
        if (combined.Contains("word search", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("wordsearch", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("cerca parole", StringComparison.OrdinalIgnoreCase)) return WordSearch;
        if (combined.Contains("coloring", StringComparison.OrdinalIgnoreCase)) return ColoringBook;
        if (combined.Contains("raccolta immagini", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("image collection", StringComparison.OrdinalIgnoreCase)) return ImageCollection;
        if (combined.Contains("saggio", StringComparison.OrdinalIgnoreCase) || combined.Contains("manuale", StringComparison.OrdinalIgnoreCase) || combined.Contains("essay", StringComparison.OrdinalIgnoreCase)) return EssayManual;
        if (combined.Contains("romanzo", StringComparison.OrdinalIgnoreCase) || combined.Contains("novel", StringComparison.OrdinalIgnoreCase)) return Novel;
        if (combined.Contains("libro illustrato", StringComparison.OrdinalIgnoreCase) || combined.Contains("illustrated book", StringComparison.OrdinalIgnoreCase)) return IllustratedBook;
        if (project.Materials.Any(m =>
                m.FileName.Contains("wordsearch", StringComparison.OrdinalIgnoreCase) ||
                m.FileName.Contains("word_search", StringComparison.OrdinalIgnoreCase) ||
                m.Columns.Any(c => c.Contains("puzzle", StringComparison.OrdinalIgnoreCase) || c.Contains("parola", StringComparison.OrdinalIgnoreCase))))
            return WordSearch;
        return string.Empty;
    }
}

internal static class BookTypeProfileUi
{
    private static readonly HashSet<RadioButton> AttachedChoices = [];
    private static string? _pendingChoice;
    private static bool _saving;

    public static void Attach(MainWindow window)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += async (_, _) =>
        {
            EnsureAdditionalChoices(window);
            foreach (var radio in Descendants(window).OfType<RadioButton>()
                         .Where(r => string.Equals(r.GroupName, "project-type", StringComparison.Ordinal)))
            {
                if (!AttachedChoices.Add(radio)) continue;
                radio.IsCheckedChanged += (_, _) =>
                {
                    if (radio.IsChecked != true) return;
                    _pendingChoice = radio.Content?.ToString();
                    SetGuideProjectType(window, _pendingChoice);
                };
            }

            if (_saving || string.IsNullOrWhiteSpace(_pendingChoice) || !TrySession(window, out var project, out var path)) return;
            var normalized = BookTypeProfileService.Normalize(_pendingChoice);
            if (string.IsNullOrWhiteSpace(normalized)) return;
            if (string.Equals(BookTypeProfileService.Get(project), normalized, StringComparison.OrdinalIgnoreCase))
            {
                _pendingChoice = null;
                return;
            }

            _saving = true;
            try
            {
                BookTypeProfileService.Set(project, normalized);
                await ProjectFileStore.SaveAsync(path, project);
                _pendingChoice = null;
            }
            finally { _saving = false; }
        };
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private static void EnsureAdditionalChoices(MainWindow window)
    {
        foreach (var panel in Descendants(window).OfType<StackPanel>())
        {
            var choices = panel.Children.OfType<RadioButton>()
                .Where(r => string.Equals(r.GroupName, "project-type", StringComparison.Ordinal))
                .ToList();
            if (choices.Count == 0) continue;
            AddChoice(panel, choices, BookTypeProfileService.ImageCollection);
            choices = panel.Children.OfType<RadioButton>().Where(r => string.Equals(r.GroupName, "project-type", StringComparison.Ordinal)).ToList();
            AddChoice(panel, choices, BookTypeProfileService.EssayManual);
            choices = panel.Children.OfType<RadioButton>().Where(r => string.Equals(r.GroupName, "project-type", StringComparison.Ordinal)).ToList();
            AddChoice(panel, choices, BookTypeProfileService.Crossword);
        }
    }

    private static void AddChoice(StackPanel panel, IReadOnlyList<RadioButton> choices, string value)
    {
        if (choices.Any(r => string.Equals(r.Content?.ToString(), value, StringComparison.Ordinal))) return;
        panel.Children.Add(new RadioButton
        {
            Content = value,
            GroupName = "project-type",
            IsChecked = false
        });
    }

    private static void SetGuideProjectType(MainWindow window, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var guide = Descendants(window).OfType<PublisherGuideView>().FirstOrDefault();
        if (guide is null) return;
        typeof(PublisherGuideView).GetField("_projectType", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(guide, value);
    }

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

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
