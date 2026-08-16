namespace DiezPublishingStudio;

/// <summary>
/// Canonical framework-wide book-type contract. This class is deliberately UI-neutral:
/// every frontend and every book family must agree on these normalized values.
/// </summary>
internal static class BookTypeProfileService
{
    private const string EntityKind = "DiezBookType";
    private const string WordSearchNodeKind = "WordSearchPuzzle";

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

    public static readonly string[] All =
    [
        ColoringBook,
        ImageCollection,
        IllustratedBook,
        EssayManual,
        WordSearch,
        Crossword,
        Quiz,
        Novel,
        DataCollection,
        Other
    ];

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
