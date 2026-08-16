using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiezPublishingStudio;

internal static class ImagePromotionPianist
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var imagePath = Path.Combine(Path.GetTempPath(), $"diez-ai-editorial-{Guid.NewGuid():N}.png");
        try
        {
            // Real 1×1 PNG: the test exercises the same file-ingest boundary used by Uno.
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Zl7sAAAAASUVORK5CYII=");
            await File.WriteAllBytesAsync(imagePath, png);

            var projectJson = NewProject("Pianista promozione libro illustrato", BookTypeCatalog.IllustratedBook);
            var job = DiezAiExchangeBridge.CreateReadyJob(
                projectJson,
                "Illustrazione capitolo",
                "Image",
                "Crea una singola illustrazione editoriale coerente con il capitolo.");
            Require(job.Job.WorkUnitId.HasValue, "Il job immagine deve avere una Work Unit.");

            var candidate = await DiezAiExchangeBridge.IngestImageResultAsync(
                job.ProjectJson,
                job.Job.WorkUnitId.Value,
                imagePath,
                "Una singola illustrazione editoriale con il soggetto principale chiaramente leggibile.",
                candidateVersion: 1);
            Require(candidate.Status is "IMPORTED" or "UPDATED",
                "L'immagine completa deve entrare come versione candidata.");
            Require(candidate.Version?.MaterialId.HasValue == true,
                "La versione immagine deve mantenere il MaterialId importato.");

            var premature = DiezAiEditorialBridge.PromoteApprovedVersion(
                candidate.ProjectJson,
                candidate.Version!.VersionId);
            Require(premature.Status == "NOT_APPROVED",
                "Una immagine non deve poter essere portata nel libro prima di Vision.");
            Require(Root(premature.ProjectJson)["IllustrationPlacements"]!.AsArray().Count == 0,
                "Il tentativo prematuro non deve creare una collocazione editoriale.");

            var requirements = DiezVisionFrontendBridge.Requirements(
                candidate.ProjectJson,
                job.Job.WorkUnitId.Value);
            Require(requirements.Count >= 2,
                "Vision deve derivare dal progetto almeno soggetto e composizione come gate richiesti.");
            var passChecks = requirements
                .Where(r => r.Required)
                .Select(r => new DiezVisionCheckInput(r.Key, "PASS", "Pianista: gate verificato."))
                .ToList();

            var vision = DiezVisionFrontendBridge.ApproveImageVersion(
                candidate.ProjectJson,
                candidate.Version.VersionId,
                passChecks,
                "Pianista: tutti i gate richiesti sono PASS.");
            Require(vision.Status == "APPROVED" && vision.Approved,
                "L'immagine deve essere approvata solo tramite Vision con tutti i gate richiesti PASS.");

            var applied = DiezAiEditorialBridge.PromoteApprovedVersion(
                vision.ProjectJson,
                candidate.Version.VersionId);
            Require(applied.Status == "APPLIED",
                "Dopo Vision PASS l'immagine deve poter essere portata nel libro.");
            Require(applied.ContentId.HasValue && applied.PlacementId.HasValue && applied.MaterialId.HasValue,
                "Il libro illustrato deve ricevere contenuto, collocazione e materiale canonici.");
            Require(applied.Surface == "Piano illustrazioni",
                "Una immagine approvata del libro illustrato deve entrare nel Piano illustrazioni.");

            var root = Root(applied.ProjectJson);
            Require(root["ContentNodes"]!.AsArray().Count == 1,
                "La prima immagine promossa deve creare una destinazione editoriale stabile.");
            Require(root["IllustrationPlacements"]!.AsArray().Count == 1,
                "La promozione immagine deve creare una sola collocazione editoriale.");
            Require(root["AiProductionJobs"]!.AsArray().OfType<JsonObject>().Single()["Status"]?.GetValue<string>() == "Applied",
                "Dopo la promozione il job immagine deve risultare Applied.");
            Require(root["FutureRoot"]?["Marker"]?.GetValue<string>() == "must-survive",
                "La promozione immagine non deve cancellare estensioni JSON future.");

            var repeat = DiezAiEditorialBridge.PromoteApprovedVersion(
                applied.ProjectJson,
                candidate.Version.VersionId);
            Require(repeat.Status == "ALREADY_APPLIED",
                "Ripromuovere la stessa immagine deve essere idempotente.");
            Require(Root(repeat.ProjectJson)["IllustrationPlacements"]!.AsArray().Count == 1,
                "L'idempotenza immagine non deve duplicare la collocazione.");

            Console.WriteLine("AI IMAGE EDITORIAL PIANIST PASS: Vision gated image promotion and the editorial placement stayed idempotent.");
        }
        finally
        {
            try { if (File.Exists(imagePath)) File.Delete(imagePath); } catch { }
        }
    }

    private static string NewProject(string name, string bookType)
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
                ["ProjectBrief"] = ""
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
                ["Marker"] = "must-survive"
            }
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject Root(string json) => JsonNode.Parse(json)!.AsObject();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
