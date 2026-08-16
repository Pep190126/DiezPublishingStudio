namespace DiezPublishingStudio;

/// <summary>
/// Public, UI-neutral catalog of the canonical Diez book types.
/// Frontends use these values for routing while persistence/services remain free
/// to keep their stronger project-specific contracts internal.
/// </summary>
public static class BookTypeCatalog
{
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

    public static IReadOnlyList<string> All { get; } =
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

    public static bool IsVisual(string? type) =>
        Equals(type, ColoringBook) || Equals(type, ImageCollection) || Equals(type, IllustratedBook);

    public static bool IsLongForm(string? type) =>
        Equals(type, Novel) || Equals(type, EssayManual);

    public static string Normalize(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return string.Empty;
        if (Equals(text, Crossword) || text.Contains("cruciverba", StringComparison.OrdinalIgnoreCase) || text.Contains("crossword", StringComparison.OrdinalIgnoreCase)) return Crossword;
        if (text.Equals("Puzzle / giochi di parole", StringComparison.OrdinalIgnoreCase) || text.Contains("word search", StringComparison.OrdinalIgnoreCase) || text.Contains("wordsearch", StringComparison.OrdinalIgnoreCase) || text.Contains("cerca parole", StringComparison.OrdinalIgnoreCase)) return WordSearch;
        if (Equals(text, ColoringBook) || text.Contains("coloring", StringComparison.OrdinalIgnoreCase)) return ColoringBook;
        if (Equals(text, ImageCollection) || text.Contains("raccolta immagini", StringComparison.OrdinalIgnoreCase) || text.Contains("image collection", StringComparison.OrdinalIgnoreCase)) return ImageCollection;
        if (Equals(text, EssayManual) || text.Contains("saggio", StringComparison.OrdinalIgnoreCase) || text.Contains("manuale", StringComparison.OrdinalIgnoreCase) || text.Contains("essay", StringComparison.OrdinalIgnoreCase)) return EssayManual;
        if (Equals(text, Novel) || text.Contains("romanzo", StringComparison.OrdinalIgnoreCase) || text.Contains("racconto", StringComparison.OrdinalIgnoreCase) || text.Contains("novel", StringComparison.OrdinalIgnoreCase)) return Novel;
        if (Equals(text, IllustratedBook) || text.Contains("illustrato", StringComparison.OrdinalIgnoreCase)) return IllustratedBook;
        if (Equals(text, Quiz) || text.Contains("quiz", StringComparison.OrdinalIgnoreCase) || text.Contains("trivia", StringComparison.OrdinalIgnoreCase)) return Quiz;
        if (Equals(text, DataCollection) || text.Contains("raccolta dati", StringComparison.OrdinalIgnoreCase) || text.Contains("catalogo", StringComparison.OrdinalIgnoreCase)) return DataCollection;
        return Other;
    }

    private static bool Equals(string? left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
