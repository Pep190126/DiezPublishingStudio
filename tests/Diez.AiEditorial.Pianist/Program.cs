using System.Text.Json;
using System.Text.Json.Nodes;
using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string NewProject(string name, string bookType)
{
    var root = new JsonObject
    {
        ["Format"] = "diez-project-package",
        ["SchemaVersion"] = 10,
        ["Name"] = name,
        ["ProjectId"] = Guid.NewGuid().ToString(),
        ["SavedAtLocal"] = "",
        ["EditionMetadata"] = new JsonObject
        {
            ["Title"] = name,
            ["Language"] = "it"
        },
        ["AiProduction"] = new JsonObject
        {
            ["SchemaVersion"] = 1,
            ["ProjectBrief"] = "",
            ["FutureAiSetting"] = "preserve-me"
        },
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
                ["Notes"] = ""
            }
        },
        ["Relations"] = new JsonArray(),
        ["BibleEntries"] = new JsonArray(),
        ["ConsistencyFacts"] = new JsonArray(),
        ["ConsistencyIssues"] = new JsonArray(),
        ["ConsistencyResolutions"] = new JsonArray(),
        ["RevisionCandidates"] = new JsonArray(),
        ["FutureRoot"] = new JsonObject
        {
            ["Marker"] = "must-survive",
            ["Version"] = 123
        }
    };
    return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

static JsonObject Root(string json) => JsonNode.Parse(json)!.AsObject();

// --- Long-form / generic Master promotion ---
var novelJson = NewProject("Pianista promozione romanzo", BookTypeCatalog.Novel);
var novelJob = DiezAiExchangeBridge.CreateReadyJob(
    novelJson,
    "Capitolo AI",
    "Text",
    "Scrivi una breve scena pronta per il Master.");
Require(novelJob.Job.WorkUnitId.HasValue, "Il job long-form deve avere una Work Unit.");

var novelV1 = await DiezAiExchangeBridge.IngestTextResultAsync(
    novelJob.ProjectJson,
    novelJob.Job.WorkUnitId.Value,
    "Prima versione editoriale approvabile.",
    candidateVersion: 1);
Require(novelV1.Version is { VersionNumber: 1, CanApprove: true }, "La v1 testo completa deve essere approvabile.");

var premature = DiezAiEditorialBridge.PromoteApprovedVersion(novelV1.ProjectJson, novelV1.Version!.VersionId);
Require(premature.Status == "NOT_APPROVED", "Una candidate non approvata non deve entrare nel libro.");
Require(Root(premature.ProjectJson)["ContentNodes"]!.AsArray().Count == 0,
    "La promozione prematura non deve creare contenuti editoriali.");

var novelApproved1 = DiezAiExchangeBridge.ApproveVersion(novelV1.ProjectJson, novelV1.Version.VersionId);
Require(novelApproved1.Status == "APPROVED", "La v1 long-form deve potersi approvare.");
var novelApplied1 = DiezAiEditorialBridge.PromoteApprovedVersion(novelApproved1.ProjectJson, novelV1.Version.VersionId);
Require(novelApplied1.Status == "APPLIED" && novelApplied1.ContentId.HasValue,
    "La v1 approvata deve creare una destinazione editoriale nel Master.");
Require(novelApplied1.Surface.Contains("Romanzo", StringComparison.OrdinalIgnoreCase),
    "La superficie restituita deve riflettere la famiglia long-form.");
var novelAfterV1 = Root(novelApplied1.ProjectJson);
Require(novelAfterV1["FutureRoot"]?["Marker"]?.GetValue<string>() == "must-survive",
    "La promozione non deve cancellare estensioni JSON future alla root.");
Require(novelAfterV1["AiProduction"]?["FutureAiSetting"]?.GetValue<string>() == "preserve-me",
    "La promozione non deve cancellare estensioni future di AiProduction.");
var novelNodesV1 = novelAfterV1["ContentNodes"]!.AsArray().OfType<JsonObject>().ToList();
Require(novelNodesV1.Count == 1, "La prima promozione deve creare un solo nodo editoriale.");
var stableContentId = novelNodesV1[0]["ContentId"]!.GetValue<string>();
Require(novelNodesV1[0]["SourceLocator"]?.GetValue<string>()?.StartsWith("ai:", StringComparison.OrdinalIgnoreCase) == true,
    "Il contenuto AI-owned deve avere un locator stabile basato sulla Work Unit.");
Require(novelNodesV1[0]["Body"]?.GetValue<string>() == "Prima versione editoriale approvabile.",
    "Il Master deve contenere esattamente il testo approvato.");

var repeatV1 = DiezAiEditorialBridge.PromoteApprovedVersion(novelApplied1.ProjectJson, novelV1.Version.VersionId);
Require(repeatV1.Status == "ALREADY_APPLIED", "Ripromuovere la stessa versione deve essere idempotente.");
Require(Root(repeatV1.ProjectJson)["ContentNodes"]!.AsArray().Count == 1,
    "L'idempotenza non deve duplicare il nodo editoriale.");

var novelV2 = await DiezAiExchangeBridge.IngestTextResultAsync(
    repeatV1.ProjectJson,
    novelJob.Job.WorkUnitId.Value,
    "Seconda versione editoriale definitiva.",
    candidateVersion: 2);
var novelApproved2 = DiezAiExchangeBridge.ApproveVersion(novelV2.ProjectJson, novelV2.Version!.VersionId);
Require(novelApproved2.Status == "APPROVED", "La v2 long-form deve poter sostituire la v1 approvata.");
var novelApplied2 = DiezAiEditorialBridge.PromoteApprovedVersion(novelApproved2.ProjectJson, novelV2.Version.VersionId);
Require(novelApplied2.Status == "APPLIED" && novelApplied2.ContentId?.ToString() == stableContentId,
    "Una nuova versione della stessa Work Unit deve aggiornare la stessa destinazione editoriale.");
var novelNodesV2 = Root(novelApplied2.ProjectJson)["ContentNodes"]!.AsArray().OfType<JsonObject>().ToList();
Require(novelNodesV2.Count == 1, "La v2 non deve creare un secondo nodo per la stessa Work Unit.");
Require(novelNodesV2[0]["Body"]?.GetValue<string>() == "Seconda versione editoriale definitiva.",
    "La v2 promossa deve aggiornare il corpo dello stesso nodo.");
var novelJobs = Root(novelApplied2.ProjectJson)["AiProductionJobs"]!.AsArray().OfType<JsonObject>().ToList();
Require(novelJobs.Single()["Status"]?.GetValue<string>() == "Applied",
    "Dopo la promozione il job legacy deve risultare Applied.");

// --- Word Search structured-data promotion ---
var wordSearchJson = NewProject("Pianista promozione Word Search", BookTypeCatalog.WordSearch);
var wordSearchJob = DiezAiExchangeBridge.CreateReadyJob(
    wordSearchJson,
    "Database puzzle AI",
    "Data",
    "Genera due puzzle Word Search strutturati.");
const string wordSearchTable = "ID;Titolo;Tema;Parola 01;Parola 02;Parola 03;Parola 04;Parola 05\nPUZ-001;Mare;Oceano;ONDA;CORALLO;SABBIA;VELA;PORTO\nPUZ-002;Bosco;Natura;ALBERO;FOGLIA;CERVO;MUSCHIO;SENTIERO";
var wordSearchCandidate = await DiezAiExchangeBridge.IngestTextResultAsync(
    wordSearchJob.ProjectJson,
    wordSearchJob.Job.WorkUnitId!.Value,
    wordSearchTable,
    candidateVersion: 1);
var wordSearchApproved = DiezAiExchangeBridge.ApproveVersion(wordSearchCandidate.ProjectJson, wordSearchCandidate.Version!.VersionId);
Require(wordSearchApproved.Status == "APPROVED", "I dati Word Search devono essere approvati prima della promozione.");
var wordSearchApplied = DiezAiEditorialBridge.PromoteApprovedVersion(wordSearchApproved.ProjectJson, wordSearchCandidate.Version.VersionId);
Require(wordSearchApplied.Status == "APPLIED" && wordSearchApplied.Surface == "Database Word Search",
    "I dati Word Search riconoscibili devono andare nel database puzzle, non in una sezione generica.");
var wsNodes = Root(wordSearchApplied.ProjectJson)["ContentNodes"]!.AsArray().OfType<JsonObject>()
    .Where(n => string.Equals(n["Kind"]?.GetValue<string>(), "WordSearchPuzzle", StringComparison.OrdinalIgnoreCase))
    .ToList();
Require(wsNodes.Count == 2, "La tabella AI deve produrre due nodi WordSearchPuzzle.");
Require(wsNodes.Select(n => n["SourceLocator"]?.GetValue<string>()).OrderBy(x => x).SequenceEqual(new[] { "PUZ-001", "PUZ-002" }),
    "Gli ID puzzle strutturati devono essere conservati.");
Require(wsNodes.All(n => n["Body"]?.GetValue<string>()?.Contains("Creato con AI", StringComparison.OrdinalIgnoreCase) == true),
    "I puzzle promossi devono mantenere una provenienza AI leggibile nel payload canonico.");

// --- Crossword structured-data promotion ---
var crosswordJson = NewProject("Pianista promozione Cruciverba", BookTypeCatalog.Crossword);
var crosswordJob = DiezAiExchangeBridge.CreateReadyJob(
    crosswordJson,
    "Definizioni cruciverba AI",
    "Data",
    "Proponi definizioni controllabili per le parole del cruciverba.");
const string crosswordTable = "PAROLA;DEFINIZIONE 1;DEFINIZIONE 2;NOTE\nMARE;Grande distesa d'acqua salata;Può essere calmo o mosso;Voce comune\nLUNA;Satellite naturale della Terra;Brilla nel cielo notturno;Controllare il contesto";
var crosswordCandidate = await DiezAiExchangeBridge.IngestTextResultAsync(
    crosswordJob.ProjectJson,
    crosswordJob.Job.WorkUnitId!.Value,
    crosswordTable,
    candidateVersion: 1);
var crosswordApproved = DiezAiExchangeBridge.ApproveVersion(crosswordCandidate.ProjectJson, crosswordCandidate.Version!.VersionId);
Require(crosswordApproved.Status == "APPROVED", "I dati Cruciverba devono essere approvati prima della promozione.");
var crosswordApplied = DiezAiEditorialBridge.PromoteApprovedVersion(crosswordApproved.ProjectJson, crosswordCandidate.Version.VersionId);
Require(crosswordApplied.Status == "APPLIED" && crosswordApplied.Surface == "Cruciverba · definizioni",
    "Le definizioni strutturate devono entrare nella superficie Cruciverba canonica.");
var crosswordRoot = Root(crosswordApplied.ProjectJson);
var crosswordWords = crosswordRoot["Entities"]!.AsArray().OfType<JsonObject>()
    .Where(e => string.Equals(e["Kind"]?.GetValue<string>(), "CrosswordWord", StringComparison.OrdinalIgnoreCase))
    .ToList();
Require(crosswordWords.Count == 2, "La promozione Cruciverba deve creare/toccare due parole canoniche.");
Require(crosswordWords.Select(e => e["Name"]!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(new[] { "MARE", "LUNA" }),
    "Le soluzioni del cruciverba non devono essere rinominate.");
var bible = crosswordRoot["BibleEntries"]!.AsArray().OfType<JsonObject>().ToList();
Require(bible.Any(b => b["Value"]?.GetValue<string>() == "Grande distesa d'acqua salata"),
    "La prima definizione deve finire nelle BibleEntries canoniche del Cruciverba.");
Require(bible.Any(b => b["Value"]?.GetValue<string>() == "Satellite naturale della Terra"),
    "Le definizioni della seconda parola devono essere persistite.");
Require(crosswordRoot["FutureRoot"]?["Marker"]?.GetValue<string>() == "must-survive",
    "La promozione puzzle non deve cancellare estensioni JSON future.");

Console.WriteLine("AI EDITORIAL PIANIST PASS: approval stayed separate from application, Work Unit destinations were stable across versions, and structured puzzle results reached their canonical editorial surfaces.");
