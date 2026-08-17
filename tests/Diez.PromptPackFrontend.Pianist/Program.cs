using System.IO.Compression;
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
        ["Name"] = "Prompt Pack pianist",
        ["ProjectId"] = Guid.NewGuid().ToString(),
        ["SavedAtLocal"] = "",
        ["EditionMetadata"] = new JsonObject { ["Title"] = "Prompt Pack pianist", ["Language"] = "it" },
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
        ["RevisionCandidates"] = new JsonArray(),
        ["FutureRoot"] = new JsonObject { ["Marker"] = "must-survive" }
    };
    return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

static async Task WriteCanonicalResponseAsync(
    string path,
    Guid projectId,
    Guid jobId,
    Guid promptPackId,
    string packageId,
    bool partial,
    params (Guid WorkUnitId, int Version, string Status, string Asset, string Description, string FailureReason)[] items)
{
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    var jsonItems = new JsonArray();
    foreach (var item in items)
    {
        jsonItems.Add(new JsonObject
        {
            ["work_unit_id"] = item.WorkUnitId.ToString(),
            ["candidate_version"] = item.Version,
            ["content_type"] = "Image",
            ["status"] = item.Status,
            ["primary_asset"] = item.Asset,
            ["description"] = item.Description,
            ["failure_reason"] = item.FailureReason,
            ["render_request_id"] = Guid.NewGuid().ToString(),
            ["render_prompt_sha256"] = new string('a', 64)
        });
        if (!string.IsNullOrWhiteSpace(item.Asset) && !item.Asset.Contains("..", StringComparison.Ordinal))
        {
            var asset = zip.CreateEntry(item.Asset);
            await using var assetStream = asset.Open();
            await assetStream.WriteAsync(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x01, 0x02, 0x03, 0x04 });
        }
    }

    var manifest = new JsonObject
    {
        ["protocol"] = "diez-response",
        ["protocol_version"] = 1,
        ["project_id"] = projectId.ToString(),
        ["job_id"] = jobId.ToString(),
        ["prompt_pack_id"] = promptPackId.ToString(),
        ["package_id"] = packageId,
        ["partial"] = partial,
        ["items"] = jsonItems
    };
    var entry = zip.CreateEntry("response-manifest.json");
    await using var stream = entry.Open();
    await using var writer = new StreamWriter(stream);
    await writer.WriteAsync(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}

static async Task WritePhysicalProviderResponseAsync(
    string path,
    Guid projectId,
    Guid jobId,
    Guid promptPackId,
    params (Guid WorkUnitId, int Version, string Asset, string Description)[] results)
{
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    var jsonResults = new JsonArray();
    foreach (var item in results)
    {
        jsonResults.Add(new JsonObject
        {
            ["work_unit_id"] = item.WorkUnitId.ToString(),
            ["candidate_version"] = item.Version,
            ["content_type"] = "IMAGE",
            ["status"] = "COMPLETED",
            ["primary_asset"] = item.Asset,
            ["description"] = item.Description
        });
        var asset = zip.CreateEntry(item.Asset);
        await using var assetStream = asset.Open();
        await assetStream.WriteAsync(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x11, 0x22, 0x33, 0x44 });
    }

    var manifest = new JsonObject
    {
        ["protocol"] = "diez-response",
        ["protocol_version"] = 1,
        ["source_prompt_pack_id"] = promptPackId.ToString(),
        ["job_id"] = jobId.ToString(),
        ["project_id"] = projectId.ToString(),
        ["results"] = jsonResults
    };
    var entry = zip.CreateEntry("diez-response.json");
    await using var stream = entry.Open();
    await using var writer = new StreamWriter(stream);
    await writer.WriteAsync(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}

static async Task WrapZipAsync(string sourcePath, string targetPath, string wrapper)
{
    using var source = ZipFile.OpenRead(sourcePath);
    using var target = ZipFile.Open(targetPath, ZipArchiveMode.Create);
    foreach (var sourceEntry in source.Entries)
    {
        var targetEntry = target.CreateEntry(wrapper.TrimEnd('/') + "/" + sourceEntry.FullName);
        await using var input = sourceEntry.Open();
        await using var output = targetEntry.Open();
        await input.CopyToAsync(output);
    }
}

var setup = DiezVisualBookFrontendBridge.SaveColoring(
    NewProject(), 2, "animali della giungla", "giungla", true,
    "Identità dei personaggi stabile fra le Scene.",
    new DiezColoringProfileDto(
        "Kawaii", true, true, "Bambini 6–9 anni", "Facile", "Spesso — Bold",
        "Bassa", "Bassa", "Semplice / minimo", "Ampio",
        true, true, true, true, true, ""));

var subjectsMutation = DiezVisualSceneFrontendBridge.ConfigureSubjects(setup.ProjectJson, true, 2);
var subjects = subjectsMutation.State.Subjects.ToList();
Require(subjects.Count == 2, "Servono due soggetti strutturati.");
var s1 = DiezVisualSceneFrontendBridge.SaveSubject(
    subjectsMutation.ProjectJson, subjects[0].SubjectId, "Elefante",
    "Elefante kawaii con grandi orecchie tonde e proboscide corta.");
var s2 = DiezVisualSceneFrontendBridge.SaveSubject(
    s1.ProjectJson, subjects[1].SubjectId, "Scimmia",
    "Scimmia kawaii con viso tondo e coda curva.");

var scenesMutation = DiezVisualSceneFrontendBridge.ConfigureScenes(s2.ProjectJson, true, 2);
var scenes = scenesMutation.State.Scenes.ToList();
Require(scenes.Count == 2, "Servono due Scene strutturate.");
var sc1 = DiezVisualSceneFrontendBridge.SaveScene(
    scenesMutation.ProjectJson, scenes[0].SceneId, "Radura",
    "Elefante e Scimmia giocano insieme in una radura tranquilla.");
var p11 = DiezVisualSceneFrontendBridge.SetSceneParticipation(sc1.ProjectJson, scenes[0].SceneId, subjects[0].SubjectId, true);
var p12 = DiezVisualSceneFrontendBridge.SetSceneParticipation(p11.ProjectJson, scenes[0].SceneId, subjects[1].SubjectId, true);
var sc2 = DiezVisualSceneFrontendBridge.SaveScene(
    p12.ProjectJson, scenes[1].SceneId, "Cascata",
    "Elefante e Scimmia riposano vicino a una piccola cascata.");
var p21 = DiezVisualSceneFrontendBridge.SetSceneParticipation(sc2.ProjectJson, scenes[1].SceneId, subjects[0].SubjectId, true);
var p22 = DiezVisualSceneFrontendBridge.SetSceneParticipation(p21.ProjectJson, scenes[1].SceneId, subjects[1].SubjectId, true);

var synced = DiezVisualJobFrontendBridge.SyncReadyJobs(
    p22.ProjectJson, "soggetti leggibili e allegri", "testo e watermark", "openai", true);
Require(synced.Success && synced.Jobs.Count == 2 && synced.Jobs.All(x => x.WorkUnitId.HasValue),
    "Il piano visuale deve produrre due Work Unit pronte.");

var selectedIds = synced.Jobs.Select(x => x.WorkUnitId!.Value).ToList();
var recompiled = DiezVisualHardPromptFrontendBridge.Recompile(synced.ProjectJson, selectedIds);
Require(recompiled.Success && recompiled.Recompiled == 2,
    "Le Work Unit devono essere ricompilate al freeze del Prompt Pack.");
var preview = DiezPromptPackFrontendBridge.Preview(recompiled.ProjectJson, selectedIds);
Require(preview.Count == 2 && preview.Select(x => x.Code).SequenceEqual(new[] { "IMG-001", "IMG-002" }),
    "Il Prompt Pack deve mantenere le due Work Unit ordinate.");

foreach (var item in preview)
{
    Require(item.Prompt.Contains("SCENE PARTICIPANTS — HARD LOCK", StringComparison.Ordinal) &&
            item.Prompt.Contains("elephant", StringComparison.OrdinalIgnoreCase) &&
            item.Prompt.Contains("monkey", StringComparison.OrdinalIgnoreCase),
        "Il Prompt provider-facing deve trasportare il cast concreto della Scena dopo la normalizzazione inglese.");
    Require(item.Prompt.Contains("STYLE — HARD LOCK: Kawaii", StringComparison.Ordinal) &&
            item.Prompt.Contains("unmistakably cute Kawaii", StringComparison.OrdinalIgnoreCase),
        "Kawaii deve mantenere la semantica HARD Avalonia.");
    Require(item.Prompt.Contains("BOLD & EASY — HARD: ON", StringComparison.Ordinal) &&
            item.Prompt.Contains("COZY — HARD: ON", StringComparison.Ordinal) &&
            item.Prompt.Contains("LINE WEIGHT — HARD", StringComparison.Ordinal),
        "Bold & Easy, Cozy e line weight devono essere HARD indipendenti.");
    Require(item.Prompt.Contains("crude geometric primitives", StringComparison.OrdinalIgnoreCase) &&
            item.Prompt.Contains("random floating circles", StringComparison.OrdinalIgnoreCase) &&
            item.Prompt.Contains("FINAL CHECK — HARD", StringComparison.Ordinal),
        "Il contratto deve bloccare deriva geometrica/placeholder e richiedere self-check finale.");
    foreach (var id in selectedIds)
        Require(!item.Prompt.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase),
            "Il prompt visuale non deve contenere WorkUnitId tecnici.");
}

var packagePrompt = DiezPromptPackBatchFrontendBridge.BuildPackagePrompt(recompiled.ProjectJson, selectedIds);
Require(packagePrompt.Contains("ESATTAMENTE 2 immagini", StringComparison.Ordinal) &&
        packagePrompt.Contains("Immagine 001 di 002", StringComparison.Ordinal) &&
        packagePrompt.Contains("Immagine 002 di 002", StringComparison.Ordinal),
    "PROMPT.md deve orchestrare un unico lotto da due immagini distinte.");

var tempRoot = Path.Combine(Path.GetTempPath(), "diez-prompt-pack-pianist-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var built = await DiezPromptPackBatchFrontendBridge.BuildManualPackageAsync(
        recompiled.ProjectJson, null, selectedIds, Path.Combine(tempRoot, "coloring-manuale"));
    Require(built.Success && built.Status == "CREATED" && File.Exists(built.OutputPath),
        "La strada Manuale deve produrre un vero ZIP.");

    Guid projectId;
    Guid jobId;
    using (var archive = ZipFile.OpenRead(built.OutputPath))
    {
        Require(archive.GetEntry("PROMPT.md") is not null &&
                archive.GetEntry("prompt-manifest.json") is not null &&
                archive.GetEntry("instructions.md") is not null,
            "Prompt Pack incompleto.");
        string manifestText;
        await using (var stream = archive.GetEntry("prompt-manifest.json")!.Open())
        using (var reader = new StreamReader(stream))
            manifestText = await reader.ReadToEndAsync();
        var manifest = JsonNode.Parse(manifestText)!.AsObject();
        var units = manifest["work_units"]!.AsArray().OfType<JsonObject>().ToList();
        Require(units.Count == 2 && units.All(x =>
                (x["instruction"]?.GetValue<string>() ?? "").Contains("SCENE PARTICIPANTS — HARD LOCK", StringComparison.Ordinal)),
            "Il manifest deve congelare gli stessi HARD prompt di PROMPT.md.");
        projectId = Guid.Parse(manifest["project_id"]!.GetValue<string>());
        jobId = Guid.Parse(manifest["job_id"]!.GetValue<string>());
    }

    Require(JsonNode.Parse(built.ProjectJson)!["FutureRoot"]?["Marker"]?.GetValue<string>() == "must-survive",
        "Prompt Pack non deve perdere JSON futuro.");

    var canonicalPath = Path.Combine(tempRoot, "response-canonical.zip");
    await WriteCanonicalResponseAsync(
        canonicalPath, projectId, jobId, built.PromptPackId, "package-001", false,
        (selectedIds[0], 1, "COMPLETE", "content/001.png", "candidate riuscita", ""),
        (selectedIds[1], 1, "FAILED", "", "nessun asset", "provider non ha rispettato un HARD lock"));
    var canonical = await DiezVisualResponsePackFrontendBridge.ReadAsync(built.ProjectJson, canonicalPath);
    Require(canonical.Success && canonical.Items.Count == 2 && canonical.RequestSnapshotId == built.RequestSnapshotId,
        "Il Response canonico deve ricomporsi sullo snapshot esatto.");
    var failed = canonical.Items.Single(x => x.Status == "FAILED");
    var failedMutation = DiezVisualResponsePackFrontendBridge.RecordProviderFailure(
        built.ProjectJson, canonical.PackageId, canonical.PromptPackId, canonical.RequestSnapshotId, failed);
    Require(failedMutation.Success &&
            DiezAiExchangeBridge.ReadVersions(failedMutation.ProjectJson, failed.WorkUnitId)
                .Any(v => v.Status == "INCOMPLETE" && v.TextContent.StartsWith("DIEZ_PROVIDER_FAILED_V1:", StringComparison.Ordinal)),
        "FAILED provider deve restare auditabile e non approvabile.");

    var physicalPath = Path.Combine(tempRoot, "diez-response-physical.zip");
    await WritePhysicalProviderResponseAsync(
        physicalPath, projectId, jobId, built.PromptPackId,
        (selectedIds[0], 1, "assets/IMG-001.png", "Kawaii scene one"),
        (selectedIds[1], 1, "assets/IMG-002.png", "Kawaii scene two"));
    var physical = await DiezVisualResponsePackFrontendBridge.ReadAsync(built.ProjectJson, physicalPath);
    Require(physical.Success && physical.Items.Count == 2,
        "Il dialetto Response osservato nel test fisico Windows deve essere accettato.");
    Require(physical.PackageId.StartsWith("sha256:", StringComparison.Ordinal) &&
            physical.Items.All(x => x.Status == "COMPLETE") &&
            physical.Items.All(x => x.AssetEntryPath.StartsWith("assets/", StringComparison.Ordinal)),
        "diez-response.json/results/COMPLETED senza package_id deve normalizzarsi in modo sicuro e idempotente.");

    var wrappedPath = Path.Combine(tempRoot, "diez-response-wrapped.zip");
    await WrapZipAsync(physicalPath, wrappedPath, "provider-output");
    var wrapped = await DiezVisualResponsePackFrontendBridge.ReadAsync(built.ProjectJson, wrappedPath);
    Require(wrapped.Success && wrapped.Items.Count == 2 &&
            wrapped.Items.All(x => x.AssetEntryPath.StartsWith("provider-output/assets/", StringComparison.Ordinal)),
        "Un wrapper di cartella del provider non deve nascondere manifest e asset validi.");

    var noManifestPath = Path.Combine(tempRoot, "response-no-manifest.zip");
    using (var noManifest = ZipFile.Open(noManifestPath, ZipArchiveMode.Create))
    {
        var note = noManifest.CreateEntry("provider-output/readme.txt");
        await using var noteStream = new StreamWriter(note.Open());
        await noteStream.WriteAsync("not a response manifest");
    }
    var missingManifest = await DiezVisualResponsePackFrontendBridge.ReadAsync(built.ProjectJson, noManifestPath);
    Require(!missingManifest.Success && missingManifest.Status == "MANIFEST_MISSING" &&
            missingManifest.Message.Contains("provider-output/readme.txt", StringComparison.Ordinal),
        "MANIFEST_MISSING deve elencare le voci realmente viste nello ZIP.");

    var marked = DiezVisualResponsePackFrontendBridge.MarkPackageImported(built.ProjectJson, physical.PackageId);
    var duplicate = await DiezVisualResponsePackFrontendBridge.ReadAsync(marked.ProjectJson, physicalPath);
    Require(!duplicate.Success && duplicate.Status == "PACKAGE_ALREADY_IMPORTED",
        "Lo stesso Response provider senza package_id non deve poter essere importato due volte.");

    var stalePath = Path.Combine(tempRoot, "response-stale.zip");
    await WriteCanonicalResponseAsync(
        stalePath, projectId, jobId, built.PromptPackId, "package-stale", true,
        (selectedIds[0], 99, "COMPLETE", "content/stale.png", "stale", ""));
    var stale = await DiezVisualResponsePackFrontendBridge.ReadAsync(built.ProjectJson, stalePath);
    Require(!stale.Success && stale.Status == "CANDIDATE_VERSION_MISMATCH",
        "Una candidate_version stale/manomessa deve essere rifiutata.");

    var unsafePath = Path.Combine(tempRoot, "response-unsafe.zip");
    await WriteCanonicalResponseAsync(
        unsafePath, projectId, jobId, built.PromptPackId, "package-unsafe", true,
        (selectedIds[0], 1, "COMPLETE", "../evil.png", "unsafe", ""));
    var unsafeResponse = await DiezVisualResponsePackFrontendBridge.ReadAsync(built.ProjectJson, unsafePath);
    Require(!unsafeResponse.Success && unsafeResponse.Status == "ASSET_NOT_FOUND",
        "Path traversal nel Response ZIP deve essere rifiutato.");

    Console.WriteLine("Prompt Pack + physical Response dialect pianist: PASS");
}
finally
{
    try { Directory.Delete(tempRoot, true); } catch { }
}
