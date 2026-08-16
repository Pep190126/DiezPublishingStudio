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

var currentRoot = JsonNode.Parse(second.ProjectJson)!.AsObject();
var rawTextJob = currentRoot["AiProductionJobs"]!.AsArray().OfType<JsonObject>()
    .Single(x => x["JobId"]?.GetValue<string>() == second.Job.JobId.ToString());
rawTextJob["FutureJobField"] = "keep-job-extension";
var currentJson = currentRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

var invalidImagePaste = await DiezAiExchangeBridge.IngestTextResultAsync(
    currentJson,
    first.Job.WorkUnitId!.Value,
    "questo non deve entrare nel flusso immagine");
Require(invalidImagePaste.Status == "INVALID", "Un risultato immagine non deve entrare dal bridge testuale.");
Require(DiezAiExchangeBridge.ReadVersions(invalidImagePaste.ProjectJson, first.Job.WorkUnitId.Value).Count == 0,
    "Il tentativo testuale su immagine non deve creare versioni spurie.");
currentJson = invalidImagePaste.ProjectJson;

var incomplete = await DiezAiExchangeBridge.IngestTextResultAsync(
    currentJson,
    second.Job.WorkUnitId!.Value,
    "",
    candidateVersion: 1,
    resultStatus: "INCOMPLETE");
Require(incomplete.Status == "INCOMPLETE", "Una risposta vuota/incompleta deve restare incompleta.");
Require(incomplete.Version is { CanApprove: false }, "Una versione incompleta non deve essere approvabile.");
currentJson = incomplete.ProjectJson;

var blocked = DiezAiExchangeBridge.ApproveVersion(currentJson, incomplete.Version!.VersionId);
Require(blocked.Status == "BLOCKED", "L'approvazione di una versione incompleta deve essere bloccata.");
currentJson = blocked.ProjectJson;

const string completeText = "Questa è la risposta editoriale completa e revisionabile.";
var completed = await DiezAiExchangeBridge.IngestTextResultAsync(
    currentJson,
    second.Job.WorkUnitId.Value,
    completeText,
    candidateVersion: 1);
Require(completed.Status == "UPDATED", "La stessa candidate incompleta deve potersi completare senza creare una nuova versione.");
Require(completed.Version is { CanApprove: true, VersionNumber: 1 }, "La candidate completata deve diventare approvabile.");
Require(completed.Job?.DisplayStatus == "Da controllare", "Il job legacy sincronizzato deve passare a Da controllare.");
currentJson = completed.ProjectJson;

var duplicate = await DiezAiExchangeBridge.IngestTextResultAsync(
    currentJson,
    second.Job.WorkUnitId.Value,
    completeText,
    candidateVersion: 1);
Require(duplicate.Status == "DUPLICATE", "La stessa risposta per la stessa candidate deve essere idempotente.");
currentJson = duplicate.ProjectJson;

var conflict = await DiezAiExchangeBridge.IngestTextResultAsync(
    currentJson,
    second.Job.WorkUnitId.Value,
    "Testo diverso per la stessa candidate.",
    candidateVersion: 1);
Require(conflict.Status == "CONFLICT", "Testi diversi con stessa Work Unit/versione devono produrre un conflitto.");
Require(conflict.Version?.TextContent == completeText, "Un conflitto non deve sovrascrivere il testo già importato.");
currentJson = conflict.ProjectJson;

var approved1 = DiezAiExchangeBridge.ApproveVersion(currentJson, completed.Version!.VersionId);
Require(approved1.Status == "APPROVED", "La prima candidate completa deve poter essere approvata.");
Require(approved1.Job?.DisplayStatus == "Approvato", "Lo stato leggibile del job deve seguire l'approvazione.");
currentJson = approved1.ProjectJson;

var secondCandidate = await DiezAiExchangeBridge.IngestTextResultAsync(
    currentJson,
    second.Job.WorkUnitId.Value,
    "Seconda versione editoriale.",
    candidateVersion: 2);
Require(secondCandidate.Status == "IMPORTED", "Una nuova candidate deve creare una nuova versione.");
currentJson = secondCandidate.ProjectJson;
var approved2 = DiezAiExchangeBridge.ApproveVersion(currentJson, secondCandidate.Version!.VersionId);
Require(approved2.Status == "APPROVED", "La seconda candidate completa deve poter sostituire la precedente.");
currentJson = approved2.ProjectJson;

var versions = DiezAiExchangeBridge.ReadVersions(currentJson, second.Job.WorkUnitId.Value);
Require(versions.Count == 2, "Devono esistere esattamente due versioni testuali.");
Require(versions.Single(v => v.VersionNumber == 1).Status == "STALE", "La versione approvata precedente deve diventare STALE.");
Require(versions.Single(v => v.VersionNumber == 2).Status == "APPROVED", "La versione più recente deve restare APPROVED.");

var afterApproval = JsonNode.Parse(currentJson)!.AsObject();
var preservedRawTextJob = afterApproval["AiProductionJobs"]!.AsArray().OfType<JsonObject>()
    .Single(x => x["JobId"]?.GetValue<string>() == second.Job.JobId.ToString());
Require(preservedRawTextJob["FutureJobField"]?.GetValue<string>() == "keep-job-extension",
    "L'ingest/approval non deve cancellare campi futuri del job raw.");

DiezAiFrontendMutation lastMutation = second;
for (var i = 0; i < 12; i++)
{
    var type = i % 3 == 0 ? "Image" : i % 3 == 1 ? "Text" : "Data";
    lastMutation = DiezAiExchangeBridge.CreateReadyJob(
        currentJson,
        $"Stress {i}",
        type,
        $"Prompt stress {i}");
    currentJson = lastMutation.ProjectJson;
}

var jobs = DiezAiExchangeBridge.ReadJobs(currentJson);
Require(jobs.Count == 14, "Il bridge deve conservare tutti i job creati durante lo stress.");
Require(jobs.Select(j => j.JobId).Distinct().Count() == jobs.Count, "I JobId non devono duplicarsi.");
Require(jobs.Select(j => j.WorkUnitId).Where(x => x.HasValue).Distinct().Count() == jobs.Count, "Ogni job deve restare associato a una Work Unit unica.");
Require(jobs.All(j => !string.IsNullOrWhiteSpace(j.Code)), "Ogni job deve avere un codice canonico.");

var finalRoot = JsonNode.Parse(currentJson)!.AsObject();
Require(finalRoot["FutureSection"]?["Version"]?.GetValue<int>() == 42, "Lo stress AI non deve cancellare sezioni future del progetto.");
Require(finalRoot["AiProduction"]?["ProjectBrief"]?.GetValue<string>() == "Brief comune del progetto", "Il brief Core deve sopravvivere ai job successivi.");
Require(finalRoot["AiProduction"]?["FutureAiFlag"]?.GetValue<string>() == "must-survive", "I campi AI sconosciuti devono sopravvivere anche a ingest e approval.");

Console.WriteLine("AI EXCHANGE PIANIST PASS: canonical jobs/work units, clean prompts, text ingest, duplicate/conflict handling, approval history and unknown JSON survived noisy frontend use.");
