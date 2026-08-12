using System.IO.Compression;
using System.Text.Json;

namespace DiezPublishingStudio;

/// <summary>
/// Regression for the real three-image Coloring Prompt Pack failures found during physical testing:
/// stale Work Unit copies in request-context.json, generated Italian technical text misclassified as a
/// user manual delta, vague per-item subjects and an overlong renderer prompt contaminated by negative concepts.
/// </summary>
internal static class PromptPackRegressionSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezPromptPackRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "three-jungle-animals.diez");
            var packPath = Path.Combine(root, "three-jungle-animals-pack.zip");
            var project = ProjectFileStore.Create("Three Jungle Animals Regression");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);

            var profile = BookTypePromptProfileService.LoadColoring(project);
            profile.SubjectDescription = "3 animali della jungla";
            profile.EnvironmentDescription = "mettici la classica vegetazione della jungle";
            profile.Style = "Bold & Easy";
            profile.TargetAudience = "Bambini 6–9 anni";
            profile.Difficulty = "Facile";
            profile.LineWeight = "Spesso — Bold";
            profile.Complexity = "Bassa";
            profile.ElementDensity = "Bassa";
            profile.Background = "Contestuale leggero";
            profile.WhiteSpace = "Ampio";
            BookTypePromptProfileService.SaveColoring(project, profile);

            project.Entities.Add(new GraphEntity
            {
                Kind = "DiezImageGenerationSpecs",
                Name = "Specifiche immagini",
                IsCandidate = false,
                Notes = "{\"PresetId\":\"kdp_letter\",\"Width\":\"8.5\",\"Height\":\"11\",\"Unit\":\"in\",\"Orientation\":\"Verticale\",\"AspectRatio\":\"17:22\",\"ResolutionClassId\":\"custom\",\"PixelWidth\":\"2550\",\"PixelHeight\":\"3300\",\"Dpi\":\"300\",\"Quality\":\"Massima / stampa\",\"LineDetail\":\"Dettaglio medio\"}"
            });

            PromptPreparationSettingsStore.Save(project, new PromptPreparationSettings
            {
                ProviderId = PromptEngineeringProviderIds.OpenAi,
                PreferAdvancedModel = true
            });

            const int count = 3;
            const string mustDo = "3 immagini, una per ogni animale";
            var baseline = PromptEngineeringCompiler.BuildSeriesPrompt(
                project, count, mustDo, string.Empty, PromptEngineeringProviderIds.OpenAi, true);
            PromptMasterStateStore.Save(project, new PromptMasterState
            {
                BookType = BookTypeProfileService.ColoringBook,
                ProviderId = PromptEngineeringProviderIds.OpenAi,
                PreferAdvancedModel = true,
                SeriesCount = count,
                MustDo = mustDo,
                MustNotDo = string.Empty,
                Prompt = baseline
            });
            PromptMasterMetadataStore.MarkGenerated(
                project, count, mustDo, string.Empty, PromptEngineeringProviderIds.OpenAi, true);

            // Exact family observed in the physical Pack: a previously machine-generated technical block
            // survived below the current prompt and was then marked as if it were a user-authored delta.
            var leakedGeneratedTechnical = """

SPECIFICHE TECNICHE:
- Tipo libro / uso images: Coloring book.
- Formato pagina / trim finale: KDP — 8.5 × 11 in.
- Dimensioni pagina: 8.5 × 11 in.
- Aspect ratio image: 17:22.
- Coerenza trim/aspect ratio: devono combaciare.
- Non deformare mai l'image per adattarla alla pagina.
- Classe risoluzione / qualità: Custom.
- Risoluzione target effettiva: 2550 × 3300 px.
- DPI di destinazione: 300.
- Qualità rendering: Massima / stampa.
- Livello tecnico di dettaglio: Dettaglio medio.
- Output Coloring Book: line art binaria in nero puro #000000 e bianco puro #FFFFFF.
- Vietati senza eccezioni: scala di grigi, ombre, gradienti e colori intermedi.
- Evita testo tecnico dentro l'image.
- Bleed e margini di sicurezza sono gestiti nel layout.
""";
            var storedWithLeak = baseline + leakedGeneratedTechnical;
            var stored = PromptMasterStateStore.LoadForCurrentBook(project)!;
            stored.Prompt = storedWithLeak;
            PromptMasterStateStore.Save(project, stored);
            PromptMasterMetadataStore.MarkManual(
                project, count, mustDo, string.Empty, PromptEngineeringProviderIds.OpenAi, true);

            var jobs = AiImageBatchService.CreateImageSeries(project, count, mustDo, "Page").ToList();
            Require(jobs.Count == count, "La serie di test non contiene tre job.");
            VisualPromptSessionService.EnsureActive(project);
            await ProjectFileStore.SaveAsync(projectPath, project);

            var state = AiExchangeStateStore.Load(project);
            var units = state.WorkUnits
                .Where(u => jobs.Any(j => j.JobId == u.LegacyAiJobId))
                .OrderBy(u => u.Position)
                .ToList();
            Require(units.Count == count, "La conversione legacy → Work Unit non produce 3 elementi.");

            // Also preserve the earlier legacy failure family: Work Units may themselves contain a full old prompt.
            foreach (var unit in units) unit.Instruction = storedWithLeak;
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(projectPath, project);

            var built = await AiVisualPromptPackService.BuildAsync(
                project, projectPath, state, units.Select(u => u.WorkUnitId), packPath);
            Require(built.Success && built.WorkUnitCount == count,
                "Il Prompt Pack reale 3×Coloring non viene costruito: " + built.Message);

            using var zip = ZipFile.OpenRead(packPath);
            var manifestText = await ReadEntryAsync(zip, "prompt-manifest.json");
            var contextText = await ReadEntryAsync(zip, "request-context.json");
            var instructions = await ReadEntryAsync(zip, "instructions.md");
            using var manifest = JsonDocument.Parse(manifestText);
            using var context = JsonDocument.Parse(contextText);
            var workUnits = manifest.RootElement.GetProperty("work_units").EnumerateArray().ToList();
            var contextUnits = context.RootElement.GetProperty("work_units").EnumerateArray().ToList();
            Require(workUnits.Count == count && contextUnits.Count == count,
                "Manifest e request-context non contengono entrambi esattamente tre Work Unit.");

            var expectedSpecies = new[] { "monkey", "tiger", "elephant" };
            var manifestRendererById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < workUnits.Count; i++)
            {
                var item = workUnits[i];
                var id = item.GetProperty("id").GetString() ?? string.Empty;
                var instruction = item.GetProperty("instruction").GetString() ?? string.Empty;
                var renderer = item.GetProperty("image_generation_prompt").GetString() ?? string.Empty;
                manifestRendererById[id] = renderer;

                Require(Count(instruction, $"DIEZ PROVIDER COMPILER v{PromptEngineeringCompiler.Version}") == 1,
                    "Una Work Unit contiene il master prompt zero volte o più di una volta.");
                Require(item.GetProperty("image_generation_prompt_authoritative").GetBoolean(),
                    "Il brief renderer non è marcato come autoritativo.");
                Require(item.GetProperty("image_generation_prompt_language").GetString() == "en",
                    "Il brief renderer non dichiara lingua inglese.");
                Require(renderer.Contains("PRIMARY SUBJECT — HARD LOCK", StringComparison.Ordinal) &&
                        renderer.Contains("one " + expectedSpecies[i], StringComparison.OrdinalIgnoreCase),
                    "Il renderer brief non assegna un soggetto concreto e distinto alla Work Unit " + (i + 1));
                Require(renderer.Contains("classic jungle vegetation", StringComparison.OrdinalIgnoreCase),
                    "L'ambiente reale non è stato normalizzato in inglese nel renderer brief.");
                Require(renderer.Contains("2550 × 3300", StringComparison.Ordinal) &&
                        renderer.Contains("300 DPI", StringComparison.OrdinalIgnoreCase) &&
                        renderer.Contains("Maximum / print", StringComparison.OrdinalIgnoreCase) &&
                        renderer.Contains("Medium detail", StringComparison.OrdinalIgnoreCase),
                    "Le specifiche tecniche correnti non sono complete e inglesi nel renderer brief.");
                foreach (var forbidden in new[]
                {
                    "SPECIFICHE TECNICHE:", "mettici", "una per ogni animale", "Dettaglio medio",
                    "scenic photography", "sunset photography", "contact sheet", "collage", "3 images"
                })
                    Require(!renderer.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                        "Contaminazione nel renderer brief: " + forbidden);

                foreach (var forbidden in new[]
                {
                    "SPECIFICHE TECNICHE:", "mettici la classica vegetazione", "una per ogni animale",
                    "Dettaglio medio", "scenic photography", "sunset photography"
                })
                    Require(!instruction.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                        "Testo italiano/legacy o distractor ancora presente nella Work Unit: " + forbidden);
            }

            foreach (var contextItem in contextUnits)
            {
                var id = contextItem.GetProperty("id").GetString() ?? string.Empty;
                var contextRenderer = contextItem.GetProperty("image_generation_prompt").GetString() ?? string.Empty;
                Require(manifestRendererById.TryGetValue(id, out var manifestRenderer) &&
                        string.Equals(contextRenderer, manifestRenderer, StringComparison.Ordinal),
                    "request-context e prompt-manifest espongono due renderer prompt diversi per la stessa Work Unit.");
                var contextInstruction = contextItem.GetProperty("instruction").GetString() ?? string.Empty;
                Require(Count(contextInstruction, $"DIEZ PROVIDER COMPILER v{PromptEngineeringCompiler.Version}") == 1,
                    "request-context conserva una Work Unit stale/non finalizzata.");
            }

            var promptEngine = manifest.RootElement.GetProperty("prompt_engine");
            Require(promptEngine.GetProperty("manual_prompt_present").GetBoolean() == false,
                "Il blocco tecnico generato viene ancora classificato come manual override.");
            Require(string.IsNullOrWhiteSpace(promptEngine.GetProperty("manual_delta").GetString()),
                "Il blocco tecnico generato produce ancora un manual delta.");
            Require(promptEngine.GetProperty("provider_compiler_version").GetString() == PromptEngineeringCompiler.Version,
                "Versione compiler errata nel manifest.");
            Require(promptEngine.GetProperty("renderer_prompt_field").GetString() == "image_generation_prompt",
                "Il manifest non dichiara il campo renderer autoritativo.");

            Require(context.RootElement.GetProperty("critical_rule").GetString()!.StartsWith("For corrections/edits", StringComparison.Ordinal),
                "critical_rule è ancora localizzata/non provider-facing.");
            Require(context.RootElement.GetProperty("profile_isolation_rule").GetString()!.StartsWith("Only the active Book Type", StringComparison.Ordinal),
                "profile_isolation_rule è ancora localizzata/non provider-facing.");
            var technical = context.RootElement.GetProperty("image_presets").GetProperty("technical_image_specs");
            Require(technical.GetProperty("Quality").GetString() == "Maximum / print",
                "Quality tecnica in request-context non normalizzata in inglese.");
            Require(technical.GetProperty("LineDetail").GetString() == "Medium detail",
                "LineDetail tecnica in request-context non normalizzata in inglese.");

            foreach (var forbidden in new[]
            {
                "SPECIFICHE TECNICHE:", "Qualità rendering:", "Livello tecnico di dettaglio:",
                "mettici la classica vegetazione", "una per ogni animale", "scenic photography", "sunset photography"
            })
            {
                Require(!manifestText.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    "Manifest contiene ancora testo legacy/localizzato/distractor: " + forbidden);
                Require(!contextText.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    "Request-context contiene ancora testo legacy/localizzato/distractor: " + forbidden);
            }

            foreach (var required in new[]
            {
                "## IMAGE renderer routing — HARD", "image_generation_prompt", "ONLY prompt text",
                "ONE Work Unit → ONE `image_generation_prompt` → ONE image-generation call",
                "PRIMARY SUBJECT — HARD LOCK", "## Image-generation integrity — HARD"
            })
                Require(instructions.Contains(required, StringComparison.OrdinalIgnoreCase),
                    "Contratto renderer/inglese mancante da instructions.md: " + required);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static int Count(string value, string token)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token)) return 0;
        var count = 0;
        var start = 0;
        while ((start = value.IndexOf(token, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += token.Length;
        }
        return count;
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
        if (!condition) throw new InvalidOperationException("PROMPT PACK REGRESSION SELF-TEST: " + message);
    }
}
