using System.Text.Json;
using System.Text.Json.Nodes;
using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var selectedStyle = "Kawaii";
var attemptedSoftFailures = new[]
{
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.SubjectMatch, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.SceneParticipantsMatch, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.SingleComposition, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.StyleMatch, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.BoldEasyMatch, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.CozyMatch, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.LineWeightMatch, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle)
};

Require(attemptedSoftFailures.All(x => x.Severity == VisionHardGatePolicy.Hard),
    "A provider/user payload must not be able to downgrade semantic HARD checks to SOFT.");
Require(attemptedSoftFailures.All(x => x.BlocksApproval),
    "Every failed semantic HARD check must block approval.");
var blocked = VisionHardGatePolicy.Aggregate(attemptedSoftFailures);
Require(blocked.OverallStatus == VisionHardGatePolicy.Fail && blocked.BlocksApproval,
    "Any semantic HARD failure must force overall FAIL and block approval.");
Require(blocked.HardFailureCount == attemptedSoftFailures.Length,
    "All attempted downgraded semantic failures must remain counted as HARD failures.");

// Soft quality judgments remain soft after semantic compliance; they may request review but never
// silently become approval-blocking HARD criteria.
var softQuality = new[]
{
    VisionHardGatePolicy.Enforce("style_quality", VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce("composition_readability", VisionHardGatePolicy.Review, VisionHardGatePolicy.Soft, selectedStyle)
};
Require(softQuality.All(x => x.Severity == VisionHardGatePolicy.Soft && !x.BlocksApproval),
    "Aesthetic/readability judgments must remain SOFT when the semantic gates themselves passed.");
var review = VisionHardGatePolicy.Aggregate(softQuality);
Require(review.OverallStatus == VisionHardGatePolicy.Review && !review.BlocksApproval,
    "Soft failure/review must produce REVIEW rather than a false HARD block.");

// style_match is conditional: it is HARD only when Diez has a selected style to enforce.
var noSelectedStyle = VisionHardGatePolicy.Enforce(
    VisionHardGatePolicy.StyleMatch,
    VisionHardGatePolicy.Fail,
    VisionHardGatePolicy.Soft,
    selectedStyle: string.Empty);
Require(noSelectedStyle.Severity == VisionHardGatePolicy.Soft && !noSelectedStyle.BlocksApproval,
    "style_match must not invent a HARD style when no explicit style exists.");

var naScene = VisionHardGatePolicy.Enforce(
    VisionHardGatePolicy.SceneParticipantsMatch,
    VisionHardGatePolicy.NotApplicable,
    VisionHardGatePolicy.Soft,
    selectedStyle);
Require(naScene.Severity == VisionHardGatePolicy.Hard && !naScene.BlocksApproval,
    "A non-applicable structured-scene gate may remain HARD policy without blocking approval.");

var passSet = new[]
{
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.SubjectMatch, VisionHardGatePolicy.Pass, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.SceneParticipantsMatch, VisionHardGatePolicy.NotApplicable, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.SingleComposition, VisionHardGatePolicy.Pass, VisionHardGatePolicy.Hard, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.StyleMatch, VisionHardGatePolicy.Pass, VisionHardGatePolicy.Hard, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.BoldEasyMatch, VisionHardGatePolicy.Pass, VisionHardGatePolicy.Hard, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.CozyMatch, VisionHardGatePolicy.Pass, VisionHardGatePolicy.Hard, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.LineWeightMatch, VisionHardGatePolicy.Pass, VisionHardGatePolicy.Hard, selectedStyle)
};
var passed = VisionHardGatePolicy.Aggregate(passSet);
Require(passed.OverallStatus == VisionHardGatePolicy.Pass && !passed.BlocksApproval,
    "PASS/NA semantic gates must allow approval.");

var instructions = VisionHardGatePolicy.InstructionMarkdown();
foreach (var key in new[]
         {
             VisionHardGatePolicy.SubjectMatch,
             VisionHardGatePolicy.SceneParticipantsMatch,
             VisionHardGatePolicy.SingleComposition,
             VisionHardGatePolicy.StyleMatch,
             VisionHardGatePolicy.BoldEasyMatch,
             VisionHardGatePolicy.CozyMatch,
             VisionHardGatePolicy.LineWeightMatch
         })
    Require(instructions.Contains($"`{key}`", StringComparison.Ordinal),
        $"Prompt Pack instructions must name canonical Vision gate {key}.");
Require(instructions.Contains("One HARD failure forces `overall_status = FAIL`", StringComparison.Ordinal),
    "Prompt Pack instructions must state the same blocking rule enforced by the Core policy.");

// End-to-end public frontend bridge: requirements are derived by the Core, not by the UI.
static string NewProjectJson(string bookType)
{
    var root = new JsonObject
    {
        ["Format"] = "diez-project-package",
        ["SchemaVersion"] = 10,
        ["Name"] = "Vision bridge pianist",
        ["SavedAtLocal"] = "",
        ["ProjectId"] = Guid.NewGuid().ToString(),
        ["EditionMetadata"] = new JsonObject { ["Title"] = "Vision bridge pianist", ["Language"] = "it" },
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
                ["Notes"] = ""
            }
        },
        ["Relations"] = new JsonArray(),
        ["BibleEntries"] = new JsonArray(),
        ["ConsistencyFacts"] = new JsonArray(),
        ["ConsistencyIssues"] = new JsonArray(),
        ["ConsistencyResolutions"] = new JsonArray(),
        ["RevisionCandidates"] = new JsonArray(),
        ["FutureVisionHostField"] = "keep-me"
    };
    return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

var coloring = DiezAiExchangeBridge.CreateReadyJob(
    NewProjectJson(BookTypeCatalog.ColoringBook),
    "Tavola Vision",
    "Image",
    "ART DIRECTION — SYNTHESIZED\nClean Line Art\nsingle composition");
Require(coloring.Job.WorkUnitId.HasValue, "Il job immagine deve avere una Work Unit per Vision.");

var coloringRequirements = DiezVisionFrontendBridge.Requirements(coloring.ProjectJson, coloring.Job.WorkUnitId.Value);
var coloringKeys = coloringRequirements.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
foreach (var requiredKey in new[]
         {
             VisionHardGatePolicy.SubjectMatch,
             VisionHardGatePolicy.SingleComposition,
             VisionHardGatePolicy.StyleMatch,
             VisionHardGatePolicy.BoldEasyMatch,
             VisionHardGatePolicy.CozyMatch,
             VisionHardGatePolicy.LineWeightMatch
         })
    Require(coloringKeys.Contains(requiredKey), $"Il Core Coloring deve richiedere {requiredKey}.");
Require(!coloringKeys.Contains(VisionHardGatePolicy.SceneParticipantsMatch),
    "scene_participants_match non deve essere inventato quando non ci sono partecipanti strutturati.");

var collection = DiezAiExchangeBridge.CreateReadyJob(
    NewProjectJson(BookTypeCatalog.ImageCollection),
    "Immagine editoriale",
    "Image",
    "Illustrazione editoriale chiara.");
var collectionRequirements = DiezVisionFrontendBridge.Requirements(collection.ProjectJson, collection.Job.WorkUnitId!.Value);
var collectionKeys = collectionRequirements.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
Require(collectionKeys.Contains(VisionHardGatePolicy.StyleMatch) && collectionKeys.Contains(VisionHardGatePolicy.LineWeightMatch),
    "Raccolta immagini deve verificare stile di resa e trattamento linee.");
Require(!collectionKeys.Contains(VisionHardGatePolicy.BoldEasyMatch) && !collectionKeys.Contains(VisionHardGatePolicy.CozyMatch),
    "Raccolta immagini non deve ereditare per errore i gate Coloring Bold & Easy/Cozy.");

var tempDir = Path.Combine(Path.GetTempPath(), "Diez-Vision-Pianist-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDir);
var candidateFile = Path.Combine(tempDir, "vision-candidate.png");
await File.WriteAllBytesAsync(candidateFile, [137, 80, 78, 71, 13, 10, 26, 10, 10, 20, 30, 40]);
try
{
    var candidate = await DiezAiExchangeBridge.IngestImageResultAsync(
        coloring.ProjectJson,
        coloring.Job.WorkUnitId.Value,
        candidateFile,
        "Soggetto singolo, composizione unica e stile Clean Line Art.",
        candidateVersion: 1);
    Require(candidate.Status == "IMPORTED" && candidate.Version is { Status: "CANDIDATE", CanApprove: false },
        "La candidate completa deve entrare come CANDIDATE ma restare non approvabile fuori da Vision.");

    var onlySubject = new[]
    {
        new DiezVisionCheckInput(VisionHardGatePolicy.SubjectMatch, VisionHardGatePolicy.Pass, "Soggetto visibile corretto.")
    };
    var missing = DiezVisionFrontendBridge.ApproveImageVersion(
        candidate.ProjectJson,
        candidate.Version!.VersionId,
        onlySubject,
        "Test con gate mancanti");
    Require(missing.Status == "VISION_FAILED" && !missing.Approved,
        "Una checklist Vision incompleta deve bloccare l'approvazione.");
    Require(missing.BlockingKeys.Contains(VisionHardGatePolicy.SingleComposition),
        "Un gate obbligatorio mancante deve comparire tra i blocchi.");
    Require(missing.Version?.Status == "INCOMPLETE" && missing.Version.DescriptionStatus == "NEEDS_VERIFICATION",
        "Un FAIL Vision deve marcare la candidate come da verificare.");

    var failCozyChecks = missing.Requirements
        .Select(r => new DiezVisionCheckInput(
            r.Key,
            r.Key == VisionHardGatePolicy.CozyMatch ? VisionHardGatePolicy.Fail : VisionHardGatePolicy.Pass,
            r.Key == VisionHardGatePolicy.CozyMatch ? "Mood non conforme." : "Controllo conforme."))
        .ToList();
    var failCozy = DiezVisionFrontendBridge.ApproveImageVersion(
        missing.ProjectJson,
        candidate.Version.VersionId,
        failCozyChecks,
        "Cozy volutamente errato");
    Require(failCozy.Status == "VISION_FAILED" && failCozy.BlockingKeys.SequenceEqual([VisionHardGatePolicy.CozyMatch]),
        "Un singolo FAIL HARD Cozy deve essere sufficiente a bloccare.");

    var allPass = failCozy.Requirements
        .Select(r => new DiezVisionCheckInput(r.Key, VisionHardGatePolicy.Pass, "PASS verificato."))
        .ToList();
    var approvedImage = DiezVisionFrontendBridge.ApproveImageVersion(
        failCozy.ProjectJson,
        candidate.Version.VersionId,
        allPass,
        "Tutti i gate HARD sono PASS",
        confidence: 0.99);
    Require(approvedImage.Status == "APPROVED" && approvedImage.Approved,
        "Una nuova verifica completa PASS deve poter recuperare e approvare una candidate prima fallita.");
    Require(approvedImage.Version?.Status == "APPROVED", "La versione deve diventare APPROVED nel contratto AI Exchange.");
    Require(approvedImage.Job?.DisplayStatus == "Approvato", "Lo stato leggibile del job deve diventare Approvato.");

    var approvedRoot = JsonNode.Parse(approvedImage.ProjectJson)!.AsObject();
    Require(approvedRoot["FutureVisionHostField"]?.GetValue<string>() == "keep-me",
        "Vision non deve cancellare campi futuri del progetto.");
    var visionEntity = approvedRoot["Entities"]!.AsArray().OfType<JsonObject>()
        .Single(e => e["Kind"]?.GetValue<string>() == "DiezVisionValidation");
    var visionState = JsonNode.Parse(visionEntity["Notes"]!.GetValue<string>())!.AsObject();
    var records = visionState["Records"]!.AsArray().OfType<JsonObject>().ToList();
    Require(records.Count == 1, "Le ri-verifiche della stessa candidate devono aggiornare un unico record Vision.");
    var audit = records[0];
    Require(audit["OverallStatus"]?.GetValue<string>() == "PASS" && audit["BlocksApproval"]?.GetValue<bool>() == false,
        "L'audit finale deve registrare PASS senza blocco.");
    Require(audit["Checks"]!.AsArray().Count == approvedImage.Requirements.Count,
        "L'audit deve contenere tutti i gate richiesti dal Core.");
}
finally
{
    try { Directory.Delete(tempDir, true); } catch { }
}

Console.WriteLine("VISION PIANIST PASS: HARD semantics resisted downgrade, Core-derived requirements blocked missing/failed gates, and a full recheck safely approved the image.");
