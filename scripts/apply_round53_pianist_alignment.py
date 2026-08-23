from pathlib import Path

path = Path(__file__).resolve().parents[1] / "tests/Diez.VisualBook.Pianist/Program.cs"
text = path.read_text(encoding="utf-8")
old = '''    var aggregateSetup = SaveSetup(NewProject(BookTypeCatalog.ColoringBook), BookTypeCatalog.ColoringBook, 3, "3 soggetti di Halloween");
    var aggregateProject = JsonSerializer.Deserialize<PreviewProject>(aggregateSetup.ProjectJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    PromptMasterStateStore.SaveDraft(aggregateProject, 3, "3 images: 1 per ogni soggetto", "un'unica image con 3 illustrazioni ognuna", string.Empty);
    var aggregatePrompt = VisualHardPromptContractCompiler.Build(aggregateProject, new AiExchangeWorkUnit
    {
        Position = 1,
        Code = "IMG-001",
        ContentType = AiExchangeContentTypes.Image
    });
    Require(aggregatePrompt.Contains("SERIES SUBJECT ASSIGNMENT — HARD", StringComparison.OrdinalIgnoreCase),
        "Un soggetto aggregato di serie deve essere materializzato come assegnazione atomica per Work Unit.");
    Require(!aggregatePrompt.Contains("PRIMARY SUBJECT — HARD LOCK: 3 soggetti di Halloween", StringComparison.OrdinalIgnoreCase),
        "Il conteggio globale dei soggetti non deve più diventare il PRIMARY SUBJECT della singola immagine.");
    Require(aggregatePrompt.Contains("item 1 of 3", StringComparison.OrdinalIgnoreCase),
        "Il prompt atomico deve dichiarare la posizione della Work Unit nel piano soggetti.");
    Require(!aggregatePrompt.Contains("USER REQUIREMENT — HARD: 3 images", StringComparison.OrdinalIgnoreCase),
        "Le istruzioni sul numero di immagini appartengono all'orchestrazione del lotto, non ai pixel della singola Work Unit.");
    Require(aggregatePrompt.Contains("Do not combine multiple requested series illustrations into one canvas", StringComparison.OrdinalIgnoreCase),
        "Il divieto utente di accorpare le illustrazioni deve diventare un vincolo atomico non ambiguo.");
'''
new = '''    var aggregateSetup = SaveSetup(NewProject(BookTypeCatalog.ColoringBook), BookTypeCatalog.ColoringBook, 3, "3 soggetti di Halloween");
    var aggregateProject = JsonSerializer.Deserialize<PreviewProject>(aggregateSetup.ProjectJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    PromptMasterStateStore.SaveDraft(aggregateProject, 3, "3 images: 1 per ogni soggetto", "un'unica image con 3 illustrazioni ognuna", string.Empty);
    try
    {
        _ = VisualHardPromptContractCompiler.Build(aggregateProject, new AiExchangeWorkUnit
        {
            Position = 1,
            Code = "IMG-001",
            ContentType = AiExchangeContentTypes.Image
        });
        throw new InvalidOperationException("Un soggetto aggregato irrisolto non deve essere delegato al renderer.");
    }
    catch (InvalidOperationException ex)
    {
        Require(ex.Message.Contains("Piano soggetti non risolto", StringComparison.OrdinalIgnoreCase),
            "Il gate semantico deve bloccare il tema aggregato prima del renderer: " + ex.Message);
    }
'''
if old not in text:
    raise SystemExit("Round 5.3 aggregate Pianist block not found")
path.write_text(text.replace(old, new, 1), encoding="utf-8")
print("ROUND53_PIANIST_ALIGNMENT_APPLIED")
