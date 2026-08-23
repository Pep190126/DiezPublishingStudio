using System.Text.Json;
using System.Text.Json.Nodes;
using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string NewProject()
{
    var root = new JsonObject
    {
        ["Format"] = "diez-project-package",
        ["SchemaVersion"] = 10,
        ["Name"] = "Semantic regression",
        ["SavedAtLocal"] = "",
        ["ProjectId"] = Guid.NewGuid().ToString(),
        ["EditionMetadata"] = new JsonObject { ["Title"] = "Semantic regression", ["Language"] = "it" },
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
                ["Name"] = BookTypeCatalog.ColoringBook,
                ["IsCandidate"] = false,
                ["Notes"] = ""
            }
        },
        ["Relations"] = new JsonArray(),
        ["BibleEntries"] = new JsonArray(),
        ["ConsistencyFacts"] = new JsonArray(),
        ["ConsistencyIssues"] = new JsonArray(),
        ["ConsistencyResolutions"] = new JsonArray(),
        ["RevisionCandidates"] = new JsonArray()
    };
    return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

static string Save(string json, string subject)
{
    return DiezVisualBookFrontendBridge.SaveColoring(
        json,
        3,
        subject,
        "Halloween",
        consistent: false,
        string.Empty,
        new DiezColoringProfileDto(
            "Cute & Playful",
            BoldEasy: true,
            Cozy: true,
            "Bambini 6–9 anni",
            "Facile",
            "Medio",
            "Bassa",
            "Bassa",
            "Dettagliato",
            "Ampio",
            ClosedAreas: true,
            AvoidTinyAreas: true,
            CleanContours: true,
            NoTextInsideImage: true,
            SubjectClearlySeparated: true,
            "")).ProjectJson;
}

static void RequireUnresolved(string subject)
{
    var json = Save(NewProject(), subject);
    try
    {
        _ = DiezVisualBookFrontendBridge.BuildPromptPack(json);
        throw new InvalidOperationException("Un tema aggregato non risolto non deve arrivare al renderer.");
    }
    catch (InvalidOperationException ex)
    {
        Require(ex.Message.Contains("Piano soggetti non risolto", StringComparison.OrdinalIgnoreCase),
            "Il blocco deve spiegare che il piano soggetti va risolto in Diez. Messaggio: " + ex.Message);
    }
}

RequireUnresolved("3 soggetti di Halloween");
RequireUnresolved("3 images di soggetti per Halloween");

var structured = Save(NewProject(), "3 soggetti di Halloween");
var configured = DiezVisualSceneFrontendBridge.ConfigureSubjects(structured, true, 3);
structured = configured.ProjectJson;
var state = configured.State;
var names = new[]
{
    "Zucca jack-o'-lantern simpatica",
    "Fantasma amichevole e sorridente",
    "Gatto con cappello da strega"
};
for (var i = 0; i < 3; i++)
{
    var mutation = DiezVisualSceneFrontendBridge.SaveSubject(
        structured,
        state.Subjects[i].SubjectId,
        names[i],
        names[i] + " come singolo soggetto focale riconoscibile.");
    structured = mutation.ProjectJson;
    state = mutation.State;
}

var pack = DiezVisualBookFrontendBridge.BuildPromptPack(structured);
Require(pack.Items.Count == 3, "Tre soggetti risolti devono produrre tre prompt atomici.");
foreach (var item in pack.Items)
{
    Require(!item.Prompt.Contains("SERIES SUBJECT ASSIGNMENT", StringComparison.OrdinalIgnoreCase),
        "Il renderer non deve più ricevere istruzioni per scegliere il soggetto della serie.");
    Require(!item.Prompt.Contains("3 images di soggetti", StringComparison.OrdinalIgnoreCase),
        "Il residuo legacy non deve contaminare il renderer.");
}

var jobs = DiezVisualJobFrontendBridge.SyncReadyJobs(structured);
Require(jobs.Success, "Le Work Unit strutturate devono poter essere sincronizzate: " + jobs.Message);
var packagePrompt = DiezPromptPackBatchFrontendBridge.BuildPackagePrompt(jobs.ProjectJson);
Require(!packagePrompt.Contains("crea PRIMA un piano soggetti", StringComparison.OrdinalIgnoreCase),
    "PROMPT.md non deve delegare al provider la pianificazione dei soggetti.");
Require(!packagePrompt.Contains("Il lotto contiene", StringComparison.OrdinalIgnoreCase),
    "Le istruzioni provider-facing del Prompt Pack devono essere in inglese tecnico, non UI italiana.");
Require(packagePrompt.Contains("project_id", StringComparison.OrdinalIgnoreCase) &&
        packagePrompt.Contains("request_snapshot_id", StringComparison.OrdinalIgnoreCase),
    "Il contratto Response deve richiedere esplicitamente gli identificatori di trasporto.");

Console.WriteLine("VISUAL_SEMANTIC_REGRESSION_OK");
