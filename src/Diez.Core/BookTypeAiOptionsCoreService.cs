namespace DiezPublishingStudio;

internal enum BookTypeAiOptionKind
{
    Text,
    Number,
    Choice,
    Toggle
}

internal sealed record BookTypeAiOptionDefinition(
    string Key,
    string Label,
    BookTypeAiOptionKind Kind,
    string DefaultValue,
    IReadOnlyList<string>? Choices = null,
    string Help = "");

/// <summary>
/// UI-neutral editorial/AI option contract shared by all Diez frontends.
/// The values are stored per canonical book type so frantic routing cannot leak
/// options from one family into another.
/// </summary>
internal static class BookTypeAiOptionsCoreService
{
    private const string EntityKind = "DiezAiOption";
    private const string StructureDecisionKey = "StructureDecision";
    private const string StructureKnown = "Known";
    private const string StructureFromProject = "FromProject";

    public static IReadOnlyList<BookTypeAiOptionDefinition> Definitions(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        return type switch
        {
            BookTypeProfileService.WordSearch =>
            [
                N("PuzzleCount", "Numero di puzzle", "100"),
                N("WordsPerPuzzle", "Parole per puzzle", "20"),
                C("Language", "Lingua", "Come il progetto", "Come il progetto", "Italiano", "Inglese", "Spagnolo", "Francese", "Tedesco"),
                T("UseAvailableCategories", "Usa categorie, sottocategorie e serie disponibili", true),
                T("NoDuplicates", "Evita parole duplicate tra i puzzle", true),
                T("AllowPhrases", "Consenti anche frasi brevi", true),
                N("MaxWordLength", "Lunghezza massima parola/frase", "22")
            ],
            BookTypeProfileService.Crossword =>
            [
                N("PuzzleCount", "Numero di cruciverba", "1"),
                C("Language", "Lingua", "Come il progetto", "Come il progetto", "Italiano", "Inglese", "Spagnolo", "Francese", "Tedesco"),
                X("Theme", "Tema / criterio editoriale", ""),
                T("GenerateDefinitionCandidates", "Genera più definizioni candidate per parola", true),
                T("PrepareQxwHandoff", "Prepara i contenuti per l'handoff Qxw", true)
            ],
            BookTypeProfileService.ColoringBook =>
            [
                N("ImageCount", "Numero di tavole", "50"),
                C("PageFormat", "Formato pagina", "8.5 x 11 in", "8.5 x 11 in", "8 x 10 in", "A4", "Quadrato", "Personalizzato nel box"),
                C("Orientation", "Orientamento", "Verticale", "Verticale", "Quadrato", "Orizzontale"),
                C("Resolution", "Qualità / risoluzione", "300 DPI", "300 DPI", "HD", "4K", "Personalizzata nel box"),
                C("Background", "Sfondo", "Bianco", "Bianco", "Trasparente", "Altro nel box"),
                C("LineStyle", "Tratto", "Linee pulite", "Linee pulite", "Linee spesse", "Linee sottili", "Molto dettagliato"),
                T("SeriesConsistency", "Mantieni coerente tutta la raccolta", true)
            ],
            BookTypeProfileService.ImageCollection =>
            [
                N("ImageCount", "Numero di immagini", "50"),
                C("Orientation", "Orientamento", "Verticale", "Verticale", "Quadrato", "Orizzontale", "Misto"),
                C("Resolution", "Qualità / risoluzione", "Alta", "Alta", "300 DPI", "HD", "4K", "Personalizzata nel box"),
                C("FileFormat", "Formato immagine preferito", "PNG", "PNG", "JPG", "WebP"),
                T("SeriesConsistency", "Mantieni coerente tutta la raccolta", true),
                T("CreateDescription", "Crea anche una descrizione per ogni immagine", false),
                C("DescriptionLength", "Lunghezza descrizione", "Dettagliata", "Breve", "Dettagliata", "Lunga", "Molto lunga / migliaia di parole")
            ],
            BookTypeProfileService.IllustratedBook =>
            [
                N("PageCount", "Numero indicativo di pagine", "32"),
                N("ImageCount", "Numero indicativo di illustrazioni", "16"),
                C("Orientation", "Orientamento", "Verticale", "Verticale", "Quadrato", "Orizzontale"),
                C("TextAmount", "Quantità di testo per pagina", "Media", "Molto breve", "Breve", "Media", "Lunga"),
                T("CharacterConsistency", "Mantieni coerenti personaggi e ambienti ricorrenti", true),
                T("KeepOriginalImages", "Mantieni sempre gli originali separati", true)
            ],
            BookTypeProfileService.EssayManual =>
            [
                N("TargetWords", "Lunghezza indicativa totale (parole)", "40000"),
                N("PageCount", "Numero indicativo di pagine", "180"),
                N("ChapterCount", "Numero indicativo di capitoli", "12"),
                C("Structure", "Struttura", "Capitoli + sezioni", "Capitoli", "Capitoli + sezioni", "Parti + capitoli + sezioni", "Definisci dal progetto"),
                X("Tone", "Tono / registro", ""),
                T("FactContinuity", "Mantieni coerenza terminologica e fattuale", true),
                T("IllustrationPlan", "Pianifica eventuali figure / illustrazioni", true)
            ],
            BookTypeProfileService.Novel =>
            [
                X("Genre", "Genere", ""),
                N("TargetWords", "Lunghezza indicativa totale (parole)", "70000"),
                N("PageCount", "Numero indicativo di pagine", "300"),
                N("ChapterCount", "Numero indicativo di capitoli", "20"),
                C("Structure", "Struttura", "Capitoli + scene", "Capitoli", "Parti + capitoli", "Capitoli + scene", "Parti + capitoli + scene"),
                C("PointOfView", "Punto di vista", "Terza persona limitata", "Prima persona", "Terza persona limitata", "Terza persona onnisciente", "Multiplo", "Decidi nel box"),
                C("VerbTense", "Tempo verbale", "Passato", "Passato", "Presente", "Misto", "Decidi nel box"),
                X("Tone", "Tono", ""),
                T("Continuity", "Mantieni coerenza di personaggi, luoghi, eventi e fili narrativi", true)
            ],
            BookTypeProfileService.Quiz =>
            [
                N("QuestionCount", "Numero di domande", "100"),
                N("AnswersPerQuestion", "Risposte per domanda", "4"),
                C("Difficulty", "Difficoltà", "Mista", "Facile", "Media", "Difficile", "Mista"),
                X("Categories", "Categorie", ""),
                T("NoDuplicates", "Evita domande duplicate", true),
                T("Explanations", "Aggiungi spiegazione della risposta", false)
            ],
            BookTypeProfileService.DataCollection =>
            [
                N("TargetRows", "Numero indicativo di elementi", "500"),
                X("RequiredColumns", "Colonne / campi desiderati", ""),
                T("Deduplicate", "Unisci e rimuovi i doppioni", true),
                T("Normalize", "Uniforma valori e formati", true),
                T("KeepProvenance", "Mantieni l'origine dei dati", true)
            ],
            BookTypeProfileService.Other =>
            [
                N("ItemCount", "Numero indicativo di elementi", "1"),
                C("Language", "Lingua", "Come il progetto", "Come il progetto", "Italiano", "Inglese", "Spagnolo", "Francese", "Tedesco")
            ],
            _ =>
            [
                N("ItemCount", "Numero indicativo di elementi", "1"),
                C("Language", "Lingua", "Come il progetto", "Come il progetto", "Italiano", "Inglese", "Spagnolo", "Francese", "Tedesco")
            ]
        };
    }

    public static string Get(PreviewProject project, BookTypeAiOptionDefinition definition)
    {
        var type = BookTypeProfileService.Get(project);
        var key = StorageKey(type, definition.Key);
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Name, key, StringComparison.OrdinalIgnoreCase));
        return entity is null ? definition.DefaultValue : entity.Notes ?? definition.DefaultValue;
    }

    public static void Set(PreviewProject project, BookTypeAiOptionDefinition definition, string? value)
    {
        var type = BookTypeProfileService.Get(project);
        var key = StorageKey(type, definition.Key);
        SetRaw(project, key, NormalizeValue(definition, value));
    }

    public static IReadOnlyList<string> PromptLines(PreviewProject project)
    {
        var lines = new List<string>();
        var type = BookTypeProfileService.Get(project);
        if (UsesStructureQuestion(type) && !StructureIsKnown(project))
        {
            lines.Add("Struttura e numero di pagine: da definire in base al progetto e ai materiali disponibili");
            return lines;
        }

        foreach (var definition in Definitions(project))
        {
            var value = Get(project, definition);
            if (definition.Kind == BookTypeAiOptionKind.Toggle)
            {
                lines.Add($"{definition.Label}: {(string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ? "sì" : "no")}");
                continue;
            }
            if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{definition.Label}: {value}");
        }
        return lines;
    }

    public static bool UsesStructureQuestion(string type) =>
        string.Equals(type, BookTypeProfileService.Novel, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, BookTypeProfileService.EssayManual, StringComparison.OrdinalIgnoreCase);

    public static bool StructureIsKnown(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        var value = GetRaw(project, StorageKey(type, StructureDecisionKey));
        return string.Equals(value, StructureKnown, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetStructureDecision(PreviewProject project, bool known)
    {
        var type = BookTypeProfileService.Get(project);
        SetRaw(project, StorageKey(type, StructureDecisionKey), known ? StructureKnown : StructureFromProject);
    }

    private static string? GetRaw(PreviewProject project, string key) =>
        project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Name, key, StringComparison.OrdinalIgnoreCase))?.Notes;

    private static void SetRaw(PreviewProject project, string key, string value)
    {
        var matches = project.Entities.Where(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();
        var entity = matches.FirstOrDefault();
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = EntityKind,
                Name = key,
                Notes = value,
                IsCandidate = false
            };
            project.Entities.Add(entity);
        }
        else entity.Notes = value;
        foreach (var duplicate in matches.Skip(1)) project.Entities.Remove(duplicate);
    }

    private static string NormalizeValue(BookTypeAiOptionDefinition definition, string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (definition.Kind == BookTypeAiOptionKind.Toggle)
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
        if (definition.Kind == BookTypeAiOptionKind.Number && !string.IsNullOrWhiteSpace(text))
            return int.TryParse(text, out var number) ? Math.Max(0, number).ToString() : definition.DefaultValue;
        return text;
    }

    private static string StorageKey(string type, string key) => $"{type}|{key}";

    private static BookTypeAiOptionDefinition N(string key, string label, string value) =>
        new(key, label, BookTypeAiOptionKind.Number, value);
    private static BookTypeAiOptionDefinition X(string key, string label, string value) =>
        new(key, label, BookTypeAiOptionKind.Text, value);
    private static BookTypeAiOptionDefinition T(string key, string label, bool value) =>
        new(key, label, BookTypeAiOptionKind.Toggle, value ? "true" : "false");
    private static BookTypeAiOptionDefinition C(string key, string label, string value, params string[] choices) =>
        new(key, label, BookTypeAiOptionKind.Choice, value, choices);
}
