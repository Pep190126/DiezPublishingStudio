using System.Text.Json;
using System.Text.Json.Nodes;
using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static JsonObject Root(string json) => JsonNode.Parse(json)!.AsObject();

static string NewProject(string bookType)
{
    var root = new JsonObject
    {
        ["Format"] = "diez-project-package",
        ["SchemaVersion"] = 10,
        ["Name"] = "Pianista frontend puzzle",
        ["ProjectId"] = Guid.NewGuid().ToString(),
        ["SavedAtLocal"] = "",
        ["EditionMetadata"] = new JsonObject { ["Title"] = "Pianista frontend puzzle", ["Language"] = "it" },
        ["AiProduction"] = new JsonObject { ["SchemaVersion"] = 1, ["ProjectBrief"] = "" },
        ["AiProductionJobs"] = new JsonArray(),
        ["Materials"] = new JsonArray(),
        ["ContentNodes"] = new JsonArray(),
        ["IllustrationPlacements"] = new JsonArray(),
        ["Entities"] = new JsonArray
        {
            new JsonObject
            {
                ["EntityId"] = Guid.NewGuid().ToString(),
                ["Kind"] = "DiezBookType",
                ["Name"] = bookType,
                ["IsCandidate"] = false,
                ["Notes"] = "",
                ["FutureBookTypeField"] = "preserve-entity-extension"
            }
        },
        ["Relations"] = new JsonArray(),
        ["BibleEntries"] = new JsonArray(),
        ["ConsistencyFacts"] = new JsonArray(),
        ["ConsistencyIssues"] = new JsonArray(),
        ["ConsistencyResolutions"] = new JsonArray(),
        ["RevisionCandidates"] = new JsonArray(),
        ["UnoUiState"] = new JsonObject
        {
            ["WordSearch.Database"] = "LEGACY WORD SEARCH DRAFT — DO NOT OVERWRITE",
            ["WordSearch.Lexicon"] = "LEGACY LEXICON DRAFT — DO NOT OVERWRITE",
            ["Crossword.Words"] = "LEGACY CROSSWORD DRAFT — DO NOT OVERWRITE",
            ["Crossword.Qxw"] = "LEGACY QXW DRAFT — DO NOT OVERWRITE"
        },
        ["FutureRoot"] = new JsonObject
        {
            ["Marker"] = "must-survive",
            ["Version"] = 42
        }
    };
    return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

// --- Word Search: legacy draft is visible, but canonical Core is authoritative. ---
var wordSearchJson = NewProject(BookTypeCatalog.WordSearch);
var initialWordSearch = DiezPuzzleFrontendBridge.ReadWordSearch(wordSearchJson);
Require(initialWordSearch.Puzzles.Count == 0, "Un progetto nuovo non deve inventare puzzle canonici dalla bozza legacy.");
Require(initialWordSearch.LegacyDatabaseDraft.Contains("LEGACY WORD SEARCH", StringComparison.Ordinal),
    "La bozza legacy deve restare recuperabile durante la migrazione.");
Require(initialWordSearch.LegacyLexiconDraft.Contains("LEGACY LEXICON", StringComparison.Ordinal),
    "La bozza lessico legacy deve restare recuperabile durante la migrazione.");

var ws1 = DiezPuzzleFrontendBridge.SaveWordSearchPuzzle(
    wordSearchJson,
    null,
    "PUZ-001",
    "Mare",
    "Oceano",
    new[] { "ONDA", "CORALLO", "SABBIA", "VELA", "PORTO" },
    "Da controllare",
    "Prima scheda canonica");
Require(ws1.Status == "SAVED" && ws1.SelectedId.HasValue, "Il primo puzzle deve essere scritto nel Core.");

var ws2 = DiezPuzzleFrontendBridge.SaveWordSearchPuzzle(
    ws1.ProjectJson,
    null,
    "PUZ-002",
    "Bosco",
    "Natura",
    new[] { "ALBERO", "FOGLIA", "CERVO", "MUSCHIO", "SENTIERO" },
    "Approvato",
    "Seconda scheda canonica");
Require(ws2.Status == "SAVED" && ws2.SelectedId.HasValue, "Il secondo puzzle deve essere scritto nel Core.");
var wsSnapshot = DiezPuzzleFrontendBridge.ReadWordSearch(ws2.ProjectJson);
Require(wsSnapshot.Puzzles.Count == 2, "Il Core deve contenere esattamente due puzzle.");
Require(wsSnapshot.Puzzles.Select(p => p.PuzzleId).ToHashSet(StringComparer.OrdinalIgnoreCase)
    .SetEquals(new[] { "PUZ-001", "PUZ-002" }), "Gli ID puzzle canonici devono restare quelli dichiarati.");

// Simulate a future schema extension attached to a canonical puzzle, then update through the bridge.
var wsRaw = Root(ws2.ProjectJson);
var wsNode = wsRaw["ContentNodes"]!.AsArray().OfType<JsonObject>()
    .Single(n => n["ContentId"]?.GetValue<string>() == ws1.SelectedId.Value.ToString());
wsNode["FuturePuzzleField"] = "preserve-puzzle-extension";
var extendedWsJson = wsRaw.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
var ws1Updated = DiezPuzzleFrontendBridge.SaveWordSearchPuzzle(
    extendedWsJson,
    ws1.SelectedId,
    "PUZ-001",
    "Mare aggiornato",
    "Oceano",
    new[] { "ONDA", "CORALLO", "SABBIA", "VELA", "PORTO", "FARO" },
    "Approvato",
    "Aggiornamento sulla stessa identità");
Require(ws1Updated.SelectedId == ws1.SelectedId, "Aggiornare un puzzle non deve cambiare ContentId.");
var wsUpdatedRoot = Root(ws1Updated.ProjectJson);
var wsUpdatedNode = wsUpdatedRoot["ContentNodes"]!.AsArray().OfType<JsonObject>()
    .Single(n => n["ContentId"]?.GetValue<string>() == ws1.SelectedId.Value.ToString());
Require(wsUpdatedNode["FuturePuzzleField"]?.GetValue<string>() == "preserve-puzzle-extension",
    "Un update canonico non deve cancellare campi futuri del ContentNode.");
Require(DiezPuzzleFrontendBridge.ReadWordSearch(ws1Updated.ProjectJson).Puzzles.Count == 2,
    "L'update della stessa identità non deve duplicare il puzzle.");

const string lexiconText = "Word;Category;Subcategory;Year\nONDA;Mare;Natura;2020\nFARO;Mare;Luoghi;2021\nABETE;Bosco;Piante;2022";
var lexiconMutation = DiezPuzzleFrontendBridge.ImportWordSearchLexiconText(ws1Updated.ProjectJson, lexiconText);
Require(lexiconMutation.Status == "IMPORTED", "Il lessico classificato deve essere riconosciuto dal Core.");
var wordSearchAfterLexicon = DiezPuzzleFrontendBridge.ReadWordSearch(lexiconMutation.ProjectJson);
Require(wordSearchAfterLexicon.Lexicon.Count == 3, "Il lessico canonico deve contenere le tre voci importate.");
Require(wordSearchAfterLexicon.Lexicon.Any(e => e.Word == "FARO" && e.Category == "Mare"),
    "Categoria e parola del lessico devono essere preservate.");
var wsCsv = DiezPuzzleFrontendBridge.BuildWordSearchCsv(lexiconMutation.ProjectJson);
Require(wsCsv.Contains("PUZ-001", StringComparison.Ordinal) && wsCsv.Contains("PUZ-002", StringComparison.Ordinal),
    "L'export CSV deve essere generato dal database canonico.");

var wsDeleted = DiezPuzzleFrontendBridge.DeleteWordSearchPuzzle(lexiconMutation.ProjectJson, ws2.SelectedId!.Value);
Require(wsDeleted.Status == "DELETED", "Il secondo puzzle deve poter essere eliminato dal Core.");
var wsAfterDelete = DiezPuzzleFrontendBridge.ReadWordSearch(wsDeleted.ProjectJson);
Require(wsAfterDelete.Puzzles.Count == 1 && wsAfterDelete.Puzzles.Single().PuzzleId == "PUZ-001",
    "La cancellazione deve rimuovere solo il puzzle selezionato.");
var wsFinalRoot = Root(wsDeleted.ProjectJson);
Require(wsFinalRoot["FutureRoot"]?["Marker"]?.GetValue<string>() == "must-survive",
    "Le estensioni future alla root devono sopravvivere alle mutazioni Word Search.");
Require(wsFinalRoot["UnoUiState"]?["WordSearch.Database"]?.GetValue<string>() == "LEGACY WORD SEARCH DRAFT — DO NOT OVERWRITE",
    "Il bridge non deve sovrascrivere la bozza Word Search legacy.");
Require(wsFinalRoot["UnoUiState"]?["WordSearch.Lexicon"]?.GetValue<string>() == "LEGACY LEXICON DRAFT — DO NOT OVERWRITE",
    "Il bridge non deve sovrascrivere la bozza lessico legacy.");

// --- Crossword: canonical entities + Bible are the sole live model. ---
var crosswordJson = NewProject(BookTypeCatalog.Crossword);
var initialCrossword = DiezPuzzleFrontendBridge.ReadCrossword(crosswordJson);
Require(initialCrossword.Entries.Count == 0, "La bozza legacy Cruciverba non deve diventare vocabolario canonico automaticamente.");
Require(initialCrossword.LegacyWordsDraft.Contains("LEGACY CROSSWORD", StringComparison.Ordinal),
    "La bozza Cruciverba legacy deve restare recuperabile.");

var settings = DiezPuzzleFrontendBridge.SaveCrosswordSettings(crosswordJson, "Mare e cielo", "Italiano", adaptive: true);
Require(settings.Status == "SAVED", "Le impostazioni Cruciverba devono essere salvate nel Core.");
var cross1 = DiezPuzzleFrontendBridge.SaveCrosswordEntry(
    settings.ProjectJson,
    null,
    "mare",
    "Grande distesa d'acqua salata",
    "Può essere calmo o mosso",
    "Confina con la costa",
    "Elemento del paesaggio marino",
    "Voce comune",
    "Grande distesa d'acqua salata");
Require(cross1.Status == "SAVED" && cross1.SelectedId.HasValue, "MARE deve entrare nel vocabolario canonico.");
var cross2 = DiezPuzzleFrontendBridge.SaveCrosswordEntry(
    cross1.ProjectJson,
    null,
    "luna",
    "Satellite naturale della Terra",
    "Brilla nel cielo notturno",
    "Ha diverse fasi visibili",
    "Orbita intorno alla Terra",
    "Controllata",
    "Satellite naturale della Terra");
Require(cross2.Status == "SAVED" && cross2.SelectedId.HasValue, "LUNA deve entrare nel vocabolario canonico.");

var crossSnapshot = DiezPuzzleFrontendBridge.ReadCrossword(cross2.ProjectJson);
Require(crossSnapshot.Theme == "Mare e cielo" && crossSnapshot.PrimaryLanguage == "Italiano" && crossSnapshot.Adaptive,
    "Le impostazioni devono essere lette dal modello Core.");
Require(crossSnapshot.Entries.Select(e => e.Word).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(new[] { "MARE", "LUNA" }),
    "Le parole devono essere normalizzate e lette dalle GraphEntity canoniche.");
Require(crossSnapshot.Entries.Single(e => e.Word == "MARE").Definition1 == "Grande distesa d'acqua salata",
    "Le definizioni devono essere lette dalle BibleEntries canoniche.");

var crossRaw = Root(cross2.ProjectJson);
var mareEntity = crossRaw["Entities"]!.AsArray().OfType<JsonObject>()
    .Single(e => e["EntityId"]?.GetValue<string>() == cross1.SelectedId.Value.ToString());
mareEntity["FutureCrosswordField"] = "preserve-crossword-extension";
var extendedCrossJson = crossRaw.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
var crossUpdated = DiezPuzzleFrontendBridge.SaveCrosswordEntry(
    extendedCrossJson,
    cross1.SelectedId,
    "MARE",
    "Distesa d'acqua salata molto estesa",
    "Può essere calmo o mosso",
    "Confina con la costa",
    "Elemento del paesaggio marino",
    "Voce aggiornata",
    "Distesa d'acqua salata molto estesa");
Require(crossUpdated.SelectedId == cross1.SelectedId, "Aggiornare MARE non deve cambiare EntityId.");
var crossUpdatedRoot = Root(crossUpdated.ProjectJson);
var mareUpdated = crossUpdatedRoot["Entities"]!.AsArray().OfType<JsonObject>()
    .Single(e => e["EntityId"]?.GetValue<string>() == cross1.SelectedId.Value.ToString());
Require(mareUpdated["FutureCrosswordField"]?.GetValue<string>() == "preserve-crossword-extension",
    "Un update Cruciverba non deve cancellare campi futuri della GraphEntity.");

var collision = DiezPuzzleFrontendBridge.SaveCrosswordEntry(
    crossUpdated.ProjectJson,
    null,
    "mare",
    "Duplicato non ammesso",
    "", "", "", "", "");
Require(collision.Status == "CONFLICT", "Un nuovo record non deve duplicare una parola già presente.");
var qxw = DiezPuzzleFrontendBridge.BuildCrosswordQxwText(crossUpdated.ProjectJson);
Require(qxw.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
    .ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(new[] { "MARE", "LUNA" }),
    "L'handoff Qxw deve derivare dal vocabolario canonico.");

var crossDeleted = DiezPuzzleFrontendBridge.DeleteCrosswordEntry(crossUpdated.ProjectJson, cross2.SelectedId!.Value);
Require(crossDeleted.Status == "DELETED", "LUNA deve poter essere rimossa dal Core.");
var crossAfterDelete = DiezPuzzleFrontendBridge.ReadCrossword(crossDeleted.ProjectJson);
Require(crossAfterDelete.Entries.Count == 1 && crossAfterDelete.Entries.Single().Word == "MARE",
    "La cancellazione deve lasciare intatta MARE.");
var crossFinalRoot = Root(crossDeleted.ProjectJson);
Require(!crossFinalRoot["BibleEntries"]!.AsArray().OfType<JsonObject>()
    .Any(b => b["SubjectEntityId"]?.GetValue<string>() == cross2.SelectedId.Value.ToString()),
    "Eliminare una parola deve eliminare anche le sue BibleEntries.");
Require(crossFinalRoot["UnoUiState"]?["Crossword.Words"]?.GetValue<string>() == "LEGACY CROSSWORD DRAFT — DO NOT OVERWRITE",
    "Il bridge non deve sovrascrivere la bozza parole legacy.");
Require(crossFinalRoot["UnoUiState"]?["Crossword.Qxw"]?.GetValue<string>() == "LEGACY QXW DRAFT — DO NOT OVERWRITE",
    "Il bridge non deve sovrascrivere la bozza Qxw legacy.");
Require(crossFinalRoot["FutureRoot"]?["Marker"]?.GetValue<string>() == "must-survive",
    "Le estensioni future alla root devono sopravvivere alle mutazioni Cruciverba.");
Require(crossFinalRoot["Entities"]!.AsArray().OfType<JsonObject>()
    .Any(e => e["Kind"]?.GetValue<string>() == "DiezBookType" && e["FutureBookTypeField"]?.GetValue<string>() == "preserve-entity-extension"),
    "Le estensioni future di entità non puzzle devono sopravvivere al merge.");

Console.WriteLine("PUZZLE FRONTEND PIANIST PASS: Uno-facing bridge uses canonical Word Search/Crossword state, preserves stable IDs and unknown JSON, and leaves legacy UnoUiState drafts untouched.");
