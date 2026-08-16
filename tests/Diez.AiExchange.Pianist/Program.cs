using System.Text.Json;
using System.Text.Json.Nodes;
using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var projectId = Guid.NewGuid();
var bookEntityId = Guid.NewGuid();
var exchangeEntityId = Guid.NewGuid();
var root = new JsonObject
{
    ["Format"] = "diez-project-package",
    ["SchemaVersion"] = 10,
    ["Name"] = "Pianista AI Exchange",
    ["ProjectId"] = projectId.ToString(),
    ["SavedAtLocal"] = "",
    ["EditionMetadata"] = new JsonObject { ["Title"] = "Pianista AI Exchange", ["Language"] = "it" },
    ["AiProduction"] = new JsonObject
    {
        ["SchemaVersion"] = 1,
        ["ProjectBrief"] = "",
        ["FutureAiFlag"] = "must-survive"
    },
    ["AiProductionJobs"] = new JsonArray(),
    ["Materials"] = new JsonArray(),
    ["ContentNodes"] = new JsonArray(),
    ["IllustrationPlacements"] = new JsonArray(),
    ["Entities"] = new JsonArray
    {
        new JsonObject
        {
            ["EntityId"] = bookEntityId.ToString(),
            ["Kind"] = "DiezBookType",
            ["Name"] = BookTypeCatalog.ColoringBook,
            ["IsCandidate"] = false,
            ["Notes"] = "",
            ["FutureBookField"] = "keep-me"
        },
        new JsonObject
        {
            ["EntityId"] = exchangeEntityId.ToString(),
            ["Kind"] = "DiezAiExchangeState",
            ["Name"] = "AI Exchange",
            ["IsCandidate"] = false,
            ["Notes"] = "",
            ["FutureExchangeField"] = 77
        }
    },
    ["Relations"] = new JsonArray(),
    ["BibleEntries"] = new JsonArray(),
    ["ConsistencyFacts"] = new JsonArray(),
    ["ConsistencyIssues"] = new JsonArray(),
    ["ConsistencyResolutions"] = new JsonArray(),
    ["RevisionCandidates"] = new JsonArray(),
    ["FutureSection"] = new JsonObject
    {
        ["Nested"] = "preserve-this",
        ["Version"] = 42
    }
};

var originalJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
var preparedVisualPrompt = "ART DIRECTION — SYNTHESIZED\nCozy: ON\nBold & Easy: ON\nsingle composition";

var first = DiezAiExchangeBridge.CreateReadyJob(
    originalJson,
    "Tavola 1",
    "Image",
    preparedVisualPrompt,
    "Brief comune del progetto");

Require(first.Job.Code == "IMG-001", "Il Core deve assegnare il codice IMG-001 al primo job immagine.");
Require(first.Job.WorkUnitId.HasValue, "Ogni job creato dal bridge deve avere una Work Unit AI Exchange.");
Require(first.Job.Prompt == preparedVisualPrompt, "Il prompt provider-facing deve restare byte-for-byte invariato.");
Require(!first.Job.Prompt.Contains("JOB DIEZ", StringComparison.OrdinalIgnoreCase), "Il bridge non deve iniettare metadata JOB DIEZ nel prompt preparato.");
Require(first.Job.DisplayType == "Immagine", "La UI deve ricevere il tipo leggibile dal Core.");
Require(first.Job.DisplayStatus == "Pronto da generare", "La UI deve ricevere lo stato leggibile dal Core.");
Require(first.ExchangeWorkUnitCount == 1, "Il primo job deve produrre una Work Unit.");

var afterFirst = JsonNode.Parse(first.ProjectJson)!.AsObject();
Require(afterFirst["FutureSection"]?["Nested"]?.GetValue<string>() == "preserve-this", "Le sezioni JSON sconosciute devono sopravvivere.");
Require(afterFirst["AiProduction"]?["FutureAiFlag"]?.GetValue<string>() == "must-survive", "I campi AI futuri/sconosciuti devono sopravvivere.");
var rawEntities = afterFirst["Entities"]!.AsArray().OfType<JsonObject>().ToList();
var rawBook = rawEntities.Single(e => e["Kind"]?.GetValue<string>() == "DiezBookType");
Require(rawBook["FutureBookField"]?.GetValue<string>() == "keep-me", "Il bridge non deve riscrivere l'entità tipo libro.");
var rawExchange = rawEntities.Single(e => e["Kind"]?.GetValue<string>() == "DiezAiExchangeState");
Require(rawExchange["FutureExchangeField"]?.GetValue<int>() == 77, "I campi sconosciuti dell'entità AI Exchange devono sopravvivere.");
Require(!string.IsNullOrWhiteSpace(rawExchange["Notes"]?.GetValue<string>()), "Lo stato AI Exchange deve essere persistito nell'entità canonica.");

var second = DiezAiExchangeBridge.CreateReadyJob(
    first.ProjectJson,
    "Capitolo sintetico",
    "Text",
    "Scrivi una sintesi editoriale semplice.");
Require(second.Job.Code == "TXT-001", "Il primo job testo deve usare il prefisso canonico TXT.");
Require(second.Job.WorkUnitId.HasValue && second.Job.WorkUnitId != first.Job.WorkUnitId, "Job distinti devono avere Work Unit distinte.");
Require(second.ExchangeWorkUnitCount == 2, "Due job devono produrre due Work Unit senza duplicazioni.");

for (var i = 0; i < 12; i++)
{
    var type = i % 3 == 0 ? "Image" : i % 3 == 1 ? "Text" : "Data";
    var mutation = DiezAiExchangeBridge.CreateReadyJob(
        second.ProjectJson,
        $"Stress {i}",
        type,
        $"Prompt stress {i}");
    second = mutation;
}

var jobs = DiezAiExchangeBridge.ReadJobs(second.ProjectJson);
Require(jobs.Count == 14, "Il bridge deve conservare tutti i job creati durante lo stress.");
Require(jobs.Select(j => j.JobId).Distinct().Count() == jobs.Count, "I JobId non devono duplicarsi.");
Require(jobs.Select(j => j.WorkUnitId).Where(x => x.HasValue).Distinct().Count() == jobs.Count, "Ogni job deve restare associato a una Work Unit unica.");
Require(jobs.All(j => !string.IsNullOrWhiteSpace(j.Code)), "Ogni job deve avere un codice canonico.");

var finalRoot = JsonNode.Parse(second.ProjectJson)!.AsObject();
Require(finalRoot["FutureSection"]?["Version"]?.GetValue<int>() == 42, "Lo stress AI non deve cancellare sezioni future del progetto.");
Require(finalRoot["AiProduction"]?["ProjectBrief"]?.GetValue<string>() == "Brief comune del progetto", "Il brief Core deve sopravvivere ai job successivi.");

Console.WriteLine("AI EXCHANGE PIANIST PASS: canonical jobs/work units, clean prepared prompts and unknown JSON fields survived noisy frontend use.");
