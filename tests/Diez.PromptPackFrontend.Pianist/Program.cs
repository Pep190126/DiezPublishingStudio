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

var tempRoot = Path.Combine(Path.GetTempPath(), "diez-prompt-pack-pianist-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var target = Path.Combine(tempRoot, "coloring-manuale");
    var built = await DiezPromptPackFrontendBridge.BuildManualAsync(
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
        "La creazione deve produrre un vero ZIP sul filesystem.");

    using var archive = ZipFile.OpenRead(built.OutputPath);
    Require(archive.GetEntry("prompt-manifest.json") is not null, "prompt-manifest.json mancante.");
    Require(archive.GetEntry("instructions.md") is not null, "instructions.md mancante.");

    string manifestText;
    await using (var stream = archive.GetEntry("prompt-manifest.json")!.Open())
    using (var reader = new StreamReader(stream))
        manifestText = await reader.ReadToEndAsync();

    var manifest = JsonNode.Parse(manifestText)!.AsObject();
    Require(manifest["protocol"]?.GetValue<string>() == "diez-prompt-pack", "Protocollo Prompt Pack errato.");
    Require(manifest["transport"]?.GetValue<string>() == "MANUAL", "Il trasporto deve essere dichiarato Manuale.");
    var units = manifest["work_units"]!.AsArray().OfType<JsonObject>().ToList();
    Require(units.Count == 2, "Il manifest deve contenere due Work Unit.");

    foreach (var unit in units)
    {
        var instruction = unit["instruction"]?.GetValue<string>() ?? string.Empty;
        Require(instruction.Contains("ART DIRECTION — SYNTHESIZED", StringComparison.OrdinalIgnoreCase),
            "Il manifest deve trasportare il Prompt provider-facing già compilato.");
        foreach (var forbidden in new[] { "ELEMENTO DIEZ", "DIEZ RENDER REQUEST ID", "Work-unit code", "Series position" })
            Require(!instruction.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                "Il Prompt provider-facing è contaminato da metadata interni: " + forbidden);
        Require(Guid.TryParse(unit["id"]?.GetValue<string>(), out _),
            "L'identità tecnica deve stare nel manifest separata dal Prompt.");
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

    Console.WriteLine("Prompt Pack frontend pianist: PASS");
}
finally
{
    try { Directory.Delete(tempRoot, true); } catch { }
}
