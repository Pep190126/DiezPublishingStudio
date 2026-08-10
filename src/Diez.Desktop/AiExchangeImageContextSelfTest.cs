using System.IO.Compression;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class AiExchangeImageContextSelfTest
{
    private static readonly byte[] BasePng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZK1sAAAAASUVORK5CYII=");
    private static readonly byte[] IntakePng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezAiImageContextSelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "visual-context.diez");
            var project = ProjectFileStore.Create("Visual Context V2 Test");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);

            var coloring = BookTypePromptProfileService.LoadColoring(project);
            coloring.SubjectDescription = "Un orso che fa ginnastica";
            coloring.EnvironmentDescription = "Palestra semplice con tappetino";
            coloring.Style = "Line Art dettagliata";
            coloring.LineWeight = "Sottile — Fine";
            BookTypePromptProfileService.SaveColoring(project, coloring);

            project.Entities.Add(new GraphEntity
            {
                Kind = "DiezImageGenerationSpecs",
                Name = "Specifiche immagini",
                IsCandidate = false,
                Notes = "{\"PresetId\":\"letter\",\"Width\":\"8.5\",\"Height\":\"11\",\"Unit\":\"in\",\"Orientation\":\"Verticale\",\"AspectRatio\":\"17:22\",\"ResolutionClassId\":\"4k\",\"PixelWidth\":\"2967\",\"PixelHeight\":\"3840\",\"Dpi\":\"300\",\"Quality\":\"Massima / stampa\",\"LineDetail\":\"Dettaglio alto ma colorabile\",\"SafeMargin\":\"0.25\",\"Bleed\":true,\"BleedAmount\":\"0.125\"}"
            });

            AiProductionService.CreateJob(project, AiProductionService.TypeImage, "Pagina 1", "Correggi l'orso");
            await ProjectFileStore.SaveAsync(projectPath, project);

            var basePath = Path.Combine(root, "base.png");
            await File.WriteAllBytesAsync(basePath, BasePng);
            var baseMaterial = await MaterialImporter.ImportAsync(basePath);
            project.Materials.Add(baseMaterial);

            var intakePath = Path.Combine(root, "intake.png");
            await File.WriteAllBytesAsync(intakePath, IntakePng);
            var intakeMaterial = await MaterialImporter.ImportAsync(intakePath);
            project.Materials.Add(intakeMaterial);

            var state = AiExchangeStateStore.Load(project);
            var unit = state.WorkUnits.Single();
            unit.Mode = AiExchangeModes.AiWithInputAsReference;
            unit.Instruction = "Cambia soltanto la posizione del braccio destro.";
            unit.Change = ["posizione del braccio destro"];
            unit.Preserve = ["all unspecified elements"];

            var baseVersion = new AiExchangeVersion
            {
                WorkUnitId = unit.WorkUnitId,
                VersionNumber = 1,
                Status = AiExchangeVersionStatuses.Approved,
                Origin = AiExchangeOrigins.Import,
                MaterialId = baseMaterial.MaterialId,
                Description = "Orso in piedi sul tappetino con entrambe le braccia lungo i fianchi.",
                DescriptionStatus = AiExchangeDescriptionStatuses.Valid,
                CreatedAtLocal = DateTimeOffset.Now.ToString("O")
            };
            state.Versions.Add(baseVersion);
            unit.ApprovedVersionId = baseVersion.VersionId;

            var paradigm = new AiExchangeParadigm
            {
                MaterialId = intakeMaterial.MaterialId,
                Scope = "ITEM",
                Roles = ["style"],
                Description = "Riferimento per tratto e stile"
            };
            state.Paradigms.Add(paradigm);
            unit.ParadigmIds.Add(paradigm.ParadigmId);

            var context = AiExchangeStateStore.EnsureVisualConsistencyContext(project, state, true,
                "Personaggio / soggetto ricorrente: Da mantenere\nStile: Da mantenere\nPalette / colori: Da mantenere — fisso nero puro #000000 e bianco puro #FFFFFF\nTratto / dettaglio: Da mantenere");
            Require(context.ConsistentEnabled, "Consistent non attivo nel test.");

            AiExchangeImageRequestContextService.Add(
                project,
                intakeMaterial.MaterialId,
                "REFERENCE",
                "Foto utente: usa la postura delle gambe come riferimento, non copiare lo sfondo.",
                [unit.WorkUnitId]);

            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(projectPath, project);

            var packPath = Path.Combine(root, "correction-pack.zip");
            var built = await AiExchangePromptPackBuilder.BuildAsync(project, projectPath, state, [unit.WorkUnitId], packPath);
            Require(built.Success, "Il core Prompt Pack non è stato creato.");

            var enhanced = await AiExchangeImageRequestContextSafeEnhancer.EnhancePromptPackAsync(
                project, projectPath, state, [unit.WorkUnitId], packPath);
            Require(enhanced.Success, "L'enrichment visuale V2 è fallito: " + enhanced.Message);
            Require(enhanced.IntakeImages == 1, "La foto intake reale non è stata inclusa.");
            Require(enhanced.BaseImages == 1, "L'immagine base reale non è stata riconosciuta.");

            using var zip = ZipFile.OpenRead(packPath);
            Require(zip.GetEntry("request-context.json") is not null, "request-context.json mancante.");
            Require(zip.GetEntry("inputs/intake/intake-index.json") is not null, "intake-index.json mancante.");
            Require(zip.Entries.Any(e => e.FullName.StartsWith("inputs/intake/", StringComparison.Ordinal) && e.FullName.EndsWith("intake.png", StringComparison.OrdinalIgnoreCase)),
                "Il file intake reale non è nel ZIP.");
            Require(zip.Entries.Any(e => e.FullName.StartsWith($"inputs/current/{unit.WorkUnitId:D}/", StringComparison.Ordinal) && e.FullName.EndsWith("base.png", StringComparison.OrdinalIgnoreCase)),
                "L'immagine base reale non è nel ZIP.");
            Require(zip.Entries.Any(e => e.FullName.StartsWith($"inputs/paradigms/{paradigm.ParadigmId:D}/", StringComparison.Ordinal)),
                "Il paradigma reale non è nel ZIP.");

            var manifest = await ReadEntryAsync(zip, "prompt-manifest.json");
            Require(manifest.Contains("Orso in piedi sul tappetino", StringComparison.Ordinal), "La descrizione corrente della base non è nel manifest.");
            Require(manifest.Contains("authoritative_visual_source", StringComparison.Ordinal), "La base non è marcata come sorgente visuale autoritativa.");
            Require(manifest.Contains("all unspecified elements", StringComparison.Ordinal), "preserve non è nel manifest.");
            Require(manifest.Contains("posizione del braccio destro", StringComparison.Ordinal), "change non è nel manifest.");

            var requestContext = await ReadEntryAsync(zip, "request-context.json");
            foreach (var required in new[]
            {
                "Foto utente: usa la postura delle gambe", "Riferimento per tratto e stile",
                "Line Art dettagliata", "Sottile — Fine", "4K UHD", "2967", "3840", "17:22", "300",
                "Massima / stampa", "0.25", "0.125", "Consistent", "#000000", "#FFFFFF"
            })
                Require(requestContext.Contains(required, StringComparison.OrdinalIgnoreCase), "Preset/contesto mancante: " + required);

            var instructions = await ReadEntryAsync(zip, "instructions.md");
            Require(instructions.Contains("Diez Publishing Studio — Prompt Pack v1", StringComparison.Ordinal), "Le istruzioni core sono state perse durante l'enrichment.");
            Require(instructions.Contains("base_version.file", StringComparison.Ordinal), "Le istruzioni non impongono l'uso della base reale.");
            Require(instructions.Contains("non sostituiscono il file immagine", StringComparison.OrdinalIgnoreCase), "Le descrizioni possono ancora sostituire impropriamente l'immagine.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static async Task<string> ReadEntryAsync(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidOperationException("Entry mancante: " + path);
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("AI IMAGE CONTEXT SELF-TEST: " + message);
    }
}
