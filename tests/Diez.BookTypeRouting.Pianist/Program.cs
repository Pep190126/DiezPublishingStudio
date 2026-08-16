using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var expected = new[]
{
    BookTypeCatalog.ColoringBook,
    BookTypeCatalog.ImageCollection,
    BookTypeCatalog.IllustratedBook,
    BookTypeCatalog.EssayManual,
    BookTypeCatalog.WordSearch,
    BookTypeCatalog.Crossword,
    BookTypeCatalog.Quiz,
    BookTypeCatalog.Novel,
    BookTypeCatalog.DataCollection,
    BookTypeCatalog.Other
};

Require(BookTypeCatalog.All.Count == 10, "Il catalogo deve contenere esattamente le dieci tipologie consolidate.");
Require(BookTypeCatalog.All.SequenceEqual(expected), "Ordine o identità delle tipologie canoniche cambiati.");
Require(BookTypeCatalog.All.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 10, "Tipologie duplicate nel catalogo.");

var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["coloring"] = BookTypeCatalog.ColoringBook,
    ["image collection"] = BookTypeCatalog.ImageCollection,
    ["libro illustrato"] = BookTypeCatalog.IllustratedBook,
    ["manuale pratico"] = BookTypeCatalog.EssayManual,
    ["wordsearch"] = BookTypeCatalog.WordSearch,
    ["crossword"] = BookTypeCatalog.Crossword,
    ["trivia"] = BookTypeCatalog.Quiz,
    ["romanzo"] = BookTypeCatalog.Novel,
    ["catalogo"] = BookTypeCatalog.DataCollection,
    ["qualcosa di nuovo"] = BookTypeCatalog.Other
};

foreach (var pair in aliases)
    Require(BookTypeCatalog.Normalize(pair.Key) == pair.Value, $"Normalizzazione errata: {pair.Key} → {BookTypeCatalog.Normalize(pair.Key)}");

Require(BookTypeCatalog.IsVisual(BookTypeCatalog.ColoringBook), "Coloring deve essere visuale.");
Require(BookTypeCatalog.IsVisual(BookTypeCatalog.ImageCollection), "Raccolta immagini deve essere visuale.");
Require(BookTypeCatalog.IsVisual(BookTypeCatalog.IllustratedBook), "Libro illustrato deve essere visuale.");
Require(!BookTypeCatalog.IsVisual(BookTypeCatalog.Quiz), "Quiz non deve essere instradato nel visuale.");
Require(BookTypeCatalog.IsLongForm(BookTypeCatalog.Novel), "Romanzo deve essere long-form.");
Require(BookTypeCatalog.IsLongForm(BookTypeCatalog.EssayManual), "Saggio/manuale deve essere long-form.");
Require(!BookTypeCatalog.IsLongForm(BookTypeCatalog.Quiz), "Quiz non deve essere instradato nella narrativa.");
Require(!BookTypeCatalog.IsLongForm(BookTypeCatalog.DataCollection), "Catalogo non deve essere instradato nella narrativa.");

var quiz = BookTypeAiOptionsCoreService.DefinitionsFor(BookTypeCatalog.Quiz);
Require(quiz.Any(x => x.Key == "QuestionCount"), "Quiz deve avere Numero di domande.");
Require(quiz.Any(x => x.Key == "AnswersPerQuestion"), "Quiz deve avere Risposte per domanda.");
Require(quiz.Any(x => x.Key == "Difficulty"), "Quiz deve avere Difficoltà.");
Require(quiz.Any(x => x.Key == "Categories"), "Quiz deve avere Categorie.");
Require(!quiz.Any(x => x.Key == "TargetWords"), "Quiz non deve ricevere le opzioni da Romanzo/Saggio.");

var catalog = BookTypeAiOptionsCoreService.DefinitionsFor(BookTypeCatalog.DataCollection);
Require(catalog.Any(x => x.Key == "TargetRows"), "Catalogo deve avere Numero indicativo di elementi.");
Require(catalog.Any(x => x.Key == "RequiredColumns"), "Catalogo deve avere Colonne/campi.");
Require(catalog.Any(x => x.Key == "Deduplicate"), "Catalogo deve avere deduplica.");
Require(catalog.Any(x => x.Key == "KeepProvenance"), "Catalogo deve poter mantenere la provenienza.");
Require(!catalog.Any(x => x.Key == "TargetWords"), "Catalogo non deve ricevere le opzioni da Romanzo/Saggio.");

var other = BookTypeAiOptionsCoreService.DefinitionsFor(BookTypeCatalog.Other);
Require(other.Any(x => x.Key == "ItemCount"), "Altro deve avere un contratto estensibile minimo.");
Require(other.Any(x => x.Key == "Language"), "Altro deve avere una lingua configurabile.");

for (var round = 0; round < 100; round++)
{
    foreach (var type in BookTypeCatalog.All.OrderBy(_ => Guid.NewGuid()))
    {
        var definitions = BookTypeAiOptionsCoreService.DefinitionsFor(type);
        Require(definitions.Count > 0, $"Nessuna opzione editoriale per {type}.");
        Require(definitions.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == definitions.Count,
            $"Chiavi duplicate per {type}.");
    }
}

Console.WriteLine("BOOK TYPE ROUTING PIANIST: PASS — 10 famiglie, routing e opzioni isolate.");
