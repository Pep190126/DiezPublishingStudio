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

static async Task WriteResponseAsync(
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

var json = NewProject();
var setup = DiezVisualBookFrontendBridge.SaveColoring(
    json,
    2,
    "gattino",
    "giardino",
    consistent: true,
    "Stesso personaggio e stesso tratto.",
    new DiezColoringProfileDto(
        "Kawaii", true, true, "Bambini 6–9 anni", "Facile", "Spesso — Bold",
        "Bassa", "Bassa", "Semplice / minimo", "Ampio",
        true, true, true, true, true, ""));

var synced = DiezVisualJobFrontendBridge.SyncReadyJobs(
    setup.ProjectJson,
    "un soggetto leggibile e allegro",
    "testo e watermark",
    "openai",
    true);
Require(synced.Success && synced.Jobs.Count == 2 && synced.Jobs.All(x => x.WorkUnitId.HasValue),
    "Il piano visuale deve produrre due Work Unit pronte prima del Prompt Pack.");

var selectedIds = synced.Jobs.Select(x => x.WorkUnitId!.Value).ToList();
var preview = DiezPromptPackFrontendBridge.Preview(synced.ProjectJson, selectedIds);
Require(preview.Count == 2 && preview.All(x => x.CandidateVersion == 1),
    "Il Prompt Pack deve prenotare la prossima Candidate per ogni Work Unit.");
Require(preview.Select(x => x.Code).SequenceEqual(new[] { "IMG-001", "IMG-002" }),
    "Le Work Unit devono mantenere ordine e codici stabili.");

var packagePrompt = DiezPromptPackBatchFrontendBridge.BuildPackagePrompt(synced.ProjectJson, selectedIds);
Require(packagePrompt.Contains("Questo ZIP è il pacchetto completo da eseguire", StringComparison.Ordinal),
    "PROMPT.md deve descrivere lo ZIP come unità di consegna, non come testo da copiare.");
Require(packagePrompt.Contains("ESATTAMENTE 2 immagini", StringComparison.Ordinal),
    "PROMPT.md deve dichiarare l'intero lotto.");
Require(packagePrompt.Contains("Immagine 001 di 002", StringComparison.Ordinal) &&
        packagePrompt.Contains("Immagine 002 di 002", StringComparison.Ordinal),
    "PROMPT.md deve contenere entrambe le posizioni in ordine.");
foreach (var id in selectedIds)
    Require(!packagePrompt.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase),
        "PROMPT.md non deve contenere WorkUnitId tecnici nei prompt visuali umani.");
foreach (var item in preview)
    Require(packagePrompt.Contains(item.Prompt.Trim(), StringComparison.Ordinal),
        "Ogni Prompt provider-facing deve essere incluso integralmente in PROMPT.md.");

var tempRoot = Path.Combine(Path.GetTempPath(), "diez-prompt-pack-pianist-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var target = Path.Combine(tempRoot, "coloring-manuale");
    var built = await DiezPromptPackBatchFrontendBridge.BuildManualPackageAsync(
        synced.ProjectJson,
        projectPackagePath: null,
        selectedIds,
        target);

    Require(built.Success && built.Status == "CREATED", "La strada Manuale deve creare realmente il Prompt Pack.");
    Require(built.PromptPackId != Guid.Empty && built.RequestSnapshotId != Guid.Empty,
        "PromptPackId e RequestSnapshotId devono essere persistiti.");
    Require(built.WorkUnitCount == 2 && built.Transport == "MANUAL",
        "Il Prompt Pack manuale deve contenere esattamente le Work Unit selezionate.");
    Require(File.Exists(built.OutputPath) && built.OutputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
        "La creazione deve produrre un solo vero ZIP sul filesystem.");

    Guid projectId;
    Guid jobId;
    using (var archive = ZipFile.OpenRead(built.OutputPath))
    {
        Require(archive.GetEntry("prompt-manifest.json") is not null, "prompt-manifest.json mancante.");
        Require(archive.GetEntry("instructions.md") is not null, "instructions.md mancante.");
        Require(archive.GetEntry(DiezPromptPackBatchFrontendBridge.PromptEntryName) is not null,
            "Il Prompt Pack deve includere PROMPT.md come ingresso AI.");

        string packedPrompt;
        await using (var stream = archive.GetEntry(DiezPromptPackBatchFrontendBridge.PromptEntryName)!.Open())
        using (var reader = new StreamReader(stream))
            packedPrompt = await reader.ReadToEndAsync();
        Require(string.Equals(packedPrompt.Trim(), packagePrompt.Trim(), StringComparison.Ordinal),
            "PROMPT.md nello ZIP deve coincidere con il prompt del lotto costruito dal Core.");

        string manifestText;
        await using (var stream = archive.GetEntry("prompt-manifest.json")!.Open())
        using (var reader = new StreamReader(stream))
            manifestText = await reader.ReadToEndAsync();

        var manifest = JsonNode.Parse(manifestText)!.AsObject();
        Require(manifest["protocol"]?.GetValue<string>() == "diez-prompt-pack", "Protocollo Prompt Pack errato.");
        Require(manifest["transport"]?.GetValue<string>() == "MANUAL", "Il trasporto deve essere dichiarato Manuale.");
        var units = manifest["work_units"]!.AsArray().OfType<JsonObject>().ToList();
        Require(units.Count == 2, "Il manifest deve contenere due Work Unit dentro lo stesso ZIP.");

        foreach (var unit in units)
        {
            var instruction = unit["instruction"]?.GetValue<string>() ?? string.Empty;
            Require(instruction.Contains("ART DIRECTION — SYNTHESIZED", StringComparison.OrdinalIgnoreCase),
                "Il manifest deve trasportare il Prompt provider-facing già compilato.");
            foreach (var forbidden in new[] { "ELEMENTO DIEZ", "DIEZ RENDER REQUEST ID", "Work-unit code", "Series position" })
                Require(!instruction.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    "Il Prompt provider-facing è contaminato da metadata interni: " + forbidden);
            Require(Guid.TryParse(unit["id"]?.GetValue<string>(), out _),
                "L'identità tecnica deve stare nel manifest separata dal Prompt visuale.");
        }
        projectId = Guid.Parse(manifest["project_id"]!.GetValue<string>());
        jobId = Guid.Parse(manifest["job_id"]!.GetValue<string>());
    }

    var returnedRoot = JsonNode.Parse(built.ProjectJson)!.AsObject();
    Require(returnedRoot["FutureRoot"]?["Marker"]?.GetValue<string>() == "must-survive",
        "La creazione del Prompt Pack non deve perdere estensioni JSON future.");
    var exchange = returnedRoot["Entities"]!.AsArray().OfType<JsonObject>()
        .Single(e => string.Equals(e["Kind"]?.GetValue<string>(), "DiezAiExchangeState", StringComparison.OrdinalIgnoreCase));
    Require((exchange["Notes"]?.GetValue<string>() ?? string.Empty).Contains(built.PromptPackId.ToString(), StringComparison.OrdinalIgnoreCase),
        "Lo stato canonico AI Exchange deve ricordare il Prompt Pack creato.");

    var nextPreview = DiezPromptPackFrontendBridge.Preview(built.ProjectJson, selectedIds);
    Require(nextPreview.All(x => x.CandidateVersion == 1),
        "Creare il trasporto non deve inventare una Candidate prima del rientro del risultato.");

    // One Response ZIP may contain the whole batch, including a mixture of COMPLETE and FAILED results.
    var responsePath = Path.Combine(tempRoot, "response-batch.zip");
    await WriteResponseAsync(
        responsePath, projectId, jobId, built.PromptPackId, "package-001", false,
        (selectedIds[0], 1, "COMPLETE", "content/001.png", "gattino riuscito", ""),
        (selectedIds[1], 1, "FAILED", "", "nessun asset", "provider non ha rispettato un HARD lock"));

    var response = await DiezVisualResponsePackFrontendBridge.ReadAsync(built.ProjectJson, responsePath);
    Require(response.Success && response.Items.Count == 2, "Un solo Response ZIP deve ricomporre entrambe le Work Unit.");
    Require(response.RequestSnapshotId == built.RequestSnapshotId,
        "Il Response deve essere ricondotto allo snapshot esatto del Prompt Pack.");
    var complete = response.Items.Single(x => x.WorkUnitId == selectedIds[0]);
    var failedItem = response.Items.Single(x => x.WorkUnitId == selectedIds[1]);
    Require(complete.AssetEntryPath == "content/001.png" && complete.AssetLength > 0,
        "L'asset riuscito deve essere validato senza caricare l'intero ZIP in memoria.");
    Require(string.Equals(failedItem.Status, "FAILED", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(failedItem.AssetEntryPath),
        "Un FAILED provider è un risultato auditabile senza asset, non un'immagine mancante inventata.");

    var failedMutation = DiezVisualResponsePackFrontendBridge.RecordProviderFailure(
        built.ProjectJson, response.PackageId, response.PromptPackId, response.RequestSnapshotId, failedItem);
    Require(failedMutation.Success, "Il FAILED provider deve essere registrabile nello stato AI centrale.");
    var failedVersions = DiezAiExchangeBridge.ReadVersions(failedMutation.ProjectJson, failedItem.WorkUnitId);
    Require(failedVersions.Any(v => v.VersionNumber == 1 &&
                                    v.Status == "INCOMPLETE" &&
                                    v.TextContent.StartsWith("DIEZ_PROVIDER_FAILED_V1:", StringComparison.Ordinal)),
        "Il FAILED deve restare visibile come versione incompleta e non approvabile.");
    Require(JsonNode.Parse(failedMutation.ProjectJson)!["FutureRoot"]?["Marker"]?.GetValue<string>() == "must-survive",
        "Registrare un FAILED non deve perdere JSON futuro.");

    var marked = DiezVisualResponsePackFrontendBridge.MarkPackageImported(failedMutation.ProjectJson, response.PackageId);
    Require(marked.Success, "Il Response Package importato deve essere registrato una sola volta.");
    var secondRead = await DiezVisualResponsePackFrontendBridge.ReadAsync(marked.ProjectJson, responsePath);
    Require(!secondRead.Success && secondRead.Status == "PACKAGE_ALREADY_IMPORTED",
        "Reimportare lo stesso Response ZIP deve essere bloccato prima di duplicare asset/versioni.");

    var stalePath = Path.Combine(tempRoot, "response-stale.zip");
    await WriteResponseAsync(
        stalePath, projectId, jobId, built.PromptPackId, "package-stale", true,
        (selectedIds[0], 99, "COMPLETE", "content/stale.png", "stale", ""));
    var stale = await DiezVisualResponsePackFrontendBridge.ReadAsync(built.ProjectJson, stalePath);
    Require(!stale.Success && stale.Status == "CANDIDATE_VERSION_MISMATCH",
        "Una candidate_version manomessa/stale deve essere rifiutata prima dell'ingest.");

    var unsafePath = Path.Combine(tempRoot, "response-unsafe.zip");
    await WriteResponseAsync(
        unsafePath, projectId, jobId, built.PromptPackId, "package-unsafe", true,
        (selectedIds[0], 1, "COMPLETE", "../evil.png", "unsafe", ""));
    var unsafeResponse = await DiezVisualResponsePackFrontendBridge.ReadAsync(built.ProjectJson, unsafePath);
    Require(!unsafeResponse.Success && unsafeResponse.Status == "ASSET_NOT_FOUND",
        "Un path traversal nel Response ZIP deve essere rifiutato.");

    Console.WriteLine("Prompt Pack + Response Pack frontend pianist: PASS");
}
finally
{
    try { Directory.Delete(tempRoot, true); } catch { }
}
