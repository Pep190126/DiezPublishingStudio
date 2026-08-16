using System.Text.Json;
using System.Text.Json.Nodes;
using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string NewProject(string bookType)
{
    var root = new JsonObject
    {
        ["Format"] = "diez-project-package",
        ["SchemaVersion"] = 10,
        ["Name"] = "Pianista visuale",
        ["ProjectId"] = Guid.NewGuid().ToString(),
        ["SavedAtLocal"] = "",
        ["EditionMetadata"] = new JsonObject
        {
            ["Title"] = "Pianista visuale",
            ["Language"] = "it",
            ["FutureMetadataField"] = "keep-metadata-extension"
        },
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
                ["FutureBookTypeField"] = "keep-book-type-extension"
            }
        },
        ["Relations"] = new JsonArray(),
        ["BibleEntries"] = new JsonArray(),
        ["ConsistencyFacts"] = new JsonArray(),
        ["ConsistencyIssues"] = new JsonArray(),
        ["ConsistencyResolutions"] = new JsonArray(),
        ["RevisionCandidates"] = new JsonArray(),
        ["FutureRoot"] = new JsonObject { ["Marker"] = "keep-root-extension" }
    };
    return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

static DiezVisualBookMutation SaveSetup(string json, string bookType, int count)
{
    if (bookType == BookTypeCatalog.ColoringBook)
    {
        return DiezVisualBookFrontendBridge.SaveColoring(
            json,
            count,
            "gattino sorridente",
            "giardino semplice",
            consistent: true,
            "Stesso personaggio, proporzioni e stile in tutta la serie.",
            new DiezColoringProfileDto(
                "Kawaii",
                BoldEasy: true,
                Cozy: true,
                "Bambini 6–9 anni",
                "Facile",
                "Spesso — Bold",
                "Bassa",
                "Bassa",
                "Semplice / minimo",
                "Ampio",
                ClosedAreas: true,
                AvoidTinyAreas: true,
                CleanContours: true,
                NoTextInsideImage: true,
                SubjectClearlySeparated: true,
                ""));
    }

    return DiezVisualBookFrontendBridge.SaveImageBook(
        json,
        bookType,
        count,
        "gattino sorridente",
        "giardino semplice",
        consistent: true,
        "Stesso soggetto e resa lungo tutta la serie.",
        new DiezImageProfileDto(
            "Illustrazione editoriale / saggio",
            "Colore limitato / palette controllata",
            "Medio",
            "Contorno medio",
            "Illustrativo chiaro",
            "Semplice / funzionale",
            "Tre quarti",
            KeepSubjectReadable: true,
            AvoidTextInsideImage: true,
            EditorialClarity: true,
            SameScaleWhenSeries: true,
            ""));
}

static byte[] PngBytes(int variant)
{
    // Minimal PNG-like asset: the ingest contract hashes and packages bytes; Vision is semantic/manual.
    var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z3L8AAAAASUVORK5CYII=");
    return bytes.Concat(new byte[] { (byte)variant }).ToArray();
}

static async Task<(string Json, DiezEditorialPromotionResult Promotion)> CompleteOneAsync(
    string json,
    DiezVisualPromptItem item,
    string imagePath)
{
    var created = DiezAiExchangeBridge.CreateReadyJob(json, item.Title, "Image", item.Prompt);
    Require(created.Job.WorkUnitId.HasValue, "Ogni job visuale deve avere una Work Unit AI Exchange.");
    Require(created.Job.Prompt == item.Prompt, "Il Prompt visuale provider-facing non deve essere riscritto dal job bridge.");

    var ingested = await DiezAiExchangeBridge.IngestImageResultAsync(
        created.ProjectJson,
        created.Job.WorkUnitId.Value,
        imagePath,
        "Un gattino sorridente chiaramente visibile in un giardino semplice.");
    Require(ingested.Version is not null && ingested.Material is not null,
        "L'immagine candidata deve produrre versione e materiale canonici.");

    var bypass = DiezAiExchangeBridge.ApproveVersion(ingested.ProjectJson, ingested.Version!.VersionId);
    Require(bypass.Status == "VISION_REQUIRED", "L'approvazione generica di un'immagine deve essere sempre vietata.");

    var requirements = DiezVisionFrontendBridge.Requirements(ingested.ProjectJson, created.Job.WorkUnitId.Value);
    Require(requirements.Count >= 2 && requirements.All(r => r.Required),
        "Vision deve derivare dal Core i gate HARD applicabili.");
    var checks = requirements.Select(r => new DiezVisionCheckInput(r.Key, "PASS", "Pianista: controllo semantico superato.")).ToList();
    var vision = DiezVisionFrontendBridge.ApproveImageVersion(
        ingested.ProjectJson,
        ingested.Version.VersionId,
        checks,
        "Tutti i gate HARD sono PASS.");
    Require(vision.Approved && vision.Status == "APPROVED", "Una candidate completa con tutti i gate PASS deve essere approvabile.");

    var promotion = DiezAiEditorialBridge.PromoteApprovedVersion(vision.ProjectJson, ingested.Version.VersionId);
    Require(promotion.Status == "APPLIED", "Dopo Vision PASS l'immagine deve poter essere portata nel libro.");
    var again = DiezAiEditorialBridge.PromoteApprovedVersion(promotion.ProjectJson, ingested.Version.VersionId);
    Require(again.Status == "ALREADY_APPLIED" && !again.Changed,
        "Porta nel libro deve essere idempotente sulla stessa versione.");
    return (again.ProjectJson, promotion);
}

var tempRoot = Path.Combine(Path.GetTempPath(), "diez-visual-book-pianist-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    foreach (var bookType in new[] { BookTypeCatalog.ColoringBook, BookTypeCatalog.ImageCollection, BookTypeCatalog.IllustratedBook })
    {
        var json = NewProject(bookType);
        var saved = SaveSetup(json, bookType, 1);
        var setup = DiezVisualBookFrontendBridge.Read(saved.ProjectJson);
        Require(setup.BookType == bookType && setup.ImageCount == 1 && setup.Consistent,
            $"{bookType}: quantità e Consistent devono vivere nel Core.");
        Require(setup.Subject.Contains("gattino", StringComparison.OrdinalIgnoreCase),
            $"{bookType}: il soggetto deve fare round-trip dal profilo canonico.");

        if (bookType == BookTypeCatalog.ColoringBook)
        {
            Require(setup.Coloring is { Style: "Kawaii", BoldEasy: true, Cozy: true },
                "Coloring: Style, Bold & Easy e Cozy devono restare dimensioni canoniche indipendenti.");
        }
        else
        {
            Require(setup.Image is { RenderingStyle: "Illustrativo chiaro" },
                $"{bookType}: lo stile di resa deve fare round-trip dal Core.");
        }

        var savedRoot = JsonNode.Parse(saved.ProjectJson)!.AsObject();
        Require(savedRoot["FutureRoot"]?["Marker"]?.GetValue<string>() == "keep-root-extension",
            $"{bookType}: il bridge non deve cancellare estensioni JSON future alla root.");
        Require(savedRoot["Entities"]!.AsArray().OfType<JsonObject>()
                .Any(e => e["Kind"]?.GetValue<string>() == "DiezBookType" &&
                          e["FutureBookTypeField"]?.GetValue<string>() == "keep-book-type-extension"),
            $"{bookType}: il bridge non deve cancellare campi futuri dall'entità Tipo libro.");

        var pack = DiezVisualBookFrontendBridge.BuildPromptPack(saved.ProjectJson);
        Require(pack.Items.Count == 1 && pack.Items[0].Code == "IMG-001",
            $"{bookType}: il Prompt Pack deve produrre esattamente una Work Unit per immagine pianificata.");
        var prompt = pack.Items[0].Prompt;
        Require(prompt.Contains("ART DIRECTION — SYNTHESIZED", StringComparison.OrdinalIgnoreCase),
            $"{bookType}: il Prompt provider-facing deve contenere l'art direction sintetizzata.");
        Require(prompt.Contains("COMPOSITION — HARD LOCK", StringComparison.OrdinalIgnoreCase),
            $"{bookType}: il Prompt provider-facing deve mantenere il lock di composizione.");
        foreach (var forbidden in new[]
                 {
                     "DIEZ ITEM EXECUTION CONTRACT", "Work-unit code", "Series position", "DIEZ RENDER REQUEST ID",
                     "FAILED/INCOMPLETE", "FRESH GENERATION"
                 })
        {
            Require(!prompt.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"{bookType}: il Prompt visuale è contaminato da metadati/orchestrazione interna: {forbidden}.");
        }

        var imagePath = Path.Combine(tempRoot, bookType.Replace(' ', '-') + ".png");
        await File.WriteAllBytesAsync(imagePath, PngBytes(bookType.GetHashCode()));
        var completed = await CompleteOneAsync(pack.ProjectJson, pack.Items[0], imagePath);

        if (bookType == BookTypeCatalog.ColoringBook)
            Require(completed.Promotion.Surface == "Raccolta pagine Coloring" && !completed.Promotion.PlacementId.HasValue,
                "Coloring: l'immagine approvata deve entrare nella raccolta pagine senza inventare una collocazione testuale.");
        else if (bookType == BookTypeCatalog.ImageCollection)
            Require(completed.Promotion.Surface == "Raccolta immagini" && !completed.Promotion.PlacementId.HasValue,
                "Raccolta immagini: il materiale approvato deve entrare nella raccolta canonica.");
        else
            Require(completed.Promotion.Surface == "Piano illustrazioni" && completed.Promotion.PlacementId.HasValue,
                "Libro illustrato: l'immagine approvata deve avere una collocazione nel Piano illustrazioni.");

        var progress = DiezVisualBookFrontendBridge.Progress(completed.Json);
        Require(progress.ReadyForPublication && progress.ExpectedImages == 1 && progress.AppliedImages == 1 && progress.DistinctAppliedMaterials == 1,
            $"{bookType}: il percorso visuale completo deve risultare pronto solo con quantità esatta e materiale applicato.");
    }

    // Global visual duplicate guard: two planned pages may not resolve to the same exact image bytes.
    var duplicateJson = NewProject(BookTypeCatalog.ColoringBook);
    var duplicateSetup = SaveSetup(duplicateJson, BookTypeCatalog.ColoringBook, 2);
    var duplicatePack = DiezVisualBookFrontendBridge.BuildPromptPack(duplicateSetup.ProjectJson);
    Require(duplicatePack.Items.Count == 2, "Il piano da due immagini deve produrre due prompt atomici.");
    var sameImage = Path.Combine(tempRoot, "same.png");
    await File.WriteAllBytesAsync(sameImage, PngBytes(99));
    var d1 = await CompleteOneAsync(duplicatePack.ProjectJson, duplicatePack.Items[0], sameImage);
    var d2 = await CompleteOneAsync(d1.Json, duplicatePack.Items[1], sameImage);
    var duplicateProgress = DiezVisualBookFrontendBridge.Progress(d2.Json);
    Require(!duplicateProgress.ReadyForPublication && duplicateProgress.Problems.Any(p => p.Contains("duplicate", StringComparison.OrdinalIgnoreCase)),
        "Due pagine diverse che usano lo stesso identico file devono bloccare la readiness del libro visuale.");

    Console.WriteLine("VISUAL BOOK PIANIST PASS: Coloring, Raccolta immagini and Libro illustrato preserve canonical setup, clean atomic prompts, Vision-only approval, explicit promotion, idempotence and whole-book duplicate guards.");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
