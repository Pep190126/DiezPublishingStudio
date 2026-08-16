namespace DiezPublishingStudio;

/// <summary>
/// Canonical framework-wide book-type contract. This class is deliberately UI-neutral:
/// every frontend and every book family must agree on these normalized values.
/// </summary>
internal static class BookTypeProfileService
{
    private const string EntityKind = "DiezBookType";
    private const string WordSearchNodeKind = "WordSearchPuzzle";

    public const string WordSearch = BookTypeCatalog.WordSearch;
    public const string Crossword = BookTypeCatalog.Crossword;
    public const string Quiz = BookTypeCatalog.Quiz;
    public const string ColoringBook = BookTypeCatalog.ColoringBook;
    public const string ImageCollection = BookTypeCatalog.ImageCollection;
    public const string Novel = BookTypeCatalog.Novel;
    public const string EssayManual = BookTypeCatalog.EssayManual;
    public const string IllustratedBook = BookTypeCatalog.IllustratedBook;
    public const string DataCollection = BookTypeCatalog.DataCollection;
    public const string Other = BookTypeCatalog.Other;

    public static readonly string[] All = BookTypeCatalog.All.ToArray();

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
        var previous = Get(project);
        if (!string.Equals(previous, normalized, StringComparison.OrdinalIgnoreCase))
            VisualPromptSessionService.OnBookTypeChanging(project, previous, normalized);

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
    /// illustration controls with Image Collection while retaining their own book type.
    /// </summary>
    public static bool IsImageCollection(PreviewProject project) =>
        BookTypeCatalog.IsVisual(Get(project));

    public static bool IsWordSearch(PreviewProject project) =>
        string.Equals(Get(project), WordSearch, StringComparison.OrdinalIgnoreCase);

    public static bool IsCrossword(PreviewProject project) =>
        string.Equals(Get(project), Crossword, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? value) => BookTypeCatalog.Normalize(value);

    private static string Infer(PreviewProject project)
    {
        if (project.Entities.Any(e => string.Equals(e.Kind, "CrosswordWord", StringComparison.OrdinalIgnoreCase))) return Crossword;
        if (project.ContentNodes.Any(n => string.Equals(n.Kind, WordSearchNodeKind, StringComparison.OrdinalIgnoreCase))) return WordSearch;
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
