using System.IO.Compression;
using System.Text.Json;

namespace DiezPublishingStudio;

/// <summary>
/// Regression for the exact failure family found during the real three-image Coloring Pack test:
/// stale full prompts inside legacy Work Units, a manual flag with no actual manual delta, mixed Italian
/// provider text and technically-valid-but-low-effort placeholder generation instructions.
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
            profile.SubjectDescription = "animali della jungla";
            profile.EnvironmentDescription = "giungla leggibile e poco affollata";
            profile.Style = "Bold & Easy";
            profile.TargetAudience = "Bambini 6–9 anni";
            profile.Difficulty = "Facile";
            profile.LineWeight = "Spesso — Bold";
            profile.Complexity = "Bassa";
            profile.ElementDensity = "Media";
            profile.Background = "Semplice / minimo";
            profile.WhiteSpace = "Ampio";
            BookTypePromptProfileService.SaveColoring(project, profile);

            PromptPreparationSettingsStore.Save(project, new PromptPreparationSettings
            {
                ProviderId = PromptEngineeringProviderIds.OpenAi,
                PreferAdvancedModel = true
            });

            const int count = 3;
            const string mustDo = "3 immagini separate di animali della jungla";
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
            // Reproduce the old state observed in the real Pack: metadata says "manual", but the stored
            // text is byte-for-byte the generated baseline and therefore has no genuine user delta.
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

            // Reproduce the second bug from the real Pack: each legacy Work Unit may carry a complete
            // obsolete prompt instead of a concise local instruction. It must never be appended again.
            var legacyFullInstruction = baseline + Environment.NewLine + Environment.NewLine +
                                        "SPECIFICHE TECNICHE:" + Environment.NewLine +
                                        "- vecchio blocco tecnico italiano che non deve raggiungere il provider";
            foreach (var unit in units) unit.Instruction = legacyFullInstruction;
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(projectPath, project);

            var built = await AiVisualPromptPackService.BuildAsync(
                project, projectPath, state, units.Select(u => u.WorkUnitId), packPath);
            Require(built.Success && built.WorkUnitCount == count,
                "Il Prompt Pack reale 3×Coloring non viene costruito: " + built.Message);

            using var zip = ZipFile.OpenRead(packPath);
            var manifestText = await ReadEntryAsync(zip, "prompt-manifest.json");
            var instructions = await ReadEntryAsync(zip, "instructions.md");
            using var manifest = JsonDocument.Parse(manifestText);
            var workUnits = manifest.RootElement.GetProperty("work_units").EnumerateArray().ToList();
            Require(workUnits.Count == count, "Il manifest non contiene esattamente tre Work Unit.");

            foreach (var item in workUnits)
            {
                var instruction = item.GetProperty("instruction").GetString() ?? string.Empty;
                Require(Count(instruction, $"DIEZ PROVIDER COMPILER v{PromptEngineeringCompiler.Version}") == 1,
                    "Una Work Unit contiene il master prompt zero volte o più di una volta.");
                Require(instruction.Contains("Generate EXACTLY ONE image", StringComparison.Ordinal),
                    "Contratto one-image mancante nella Work Unit.");
                Require(instruction.Contains("COLORING PUBLICATION ACCEPTANCE GATE — HARD", StringComparison.OrdinalIgnoreCase),
                    "Gate editoriale anti-scarabocchio mancante nella Work Unit.");
                Require(instruction.Contains("3 separate images of jungle animals", StringComparison.OrdinalIgnoreCase),
                    "Il MUST DO del caso reale non è stato normalizzato in inglese.");
                Require(instruction.Contains("Children ages 6–9", StringComparison.OrdinalIgnoreCase) &&
                        instruction.Contains("Thick — Bold", StringComparison.OrdinalIgnoreCase) &&
                        instruction.Contains("element density: Medium", StringComparison.OrdinalIgnoreCase),
                    "I valori UI del profilo Coloring non sono normalizzati correttamente per il provider.");
                foreach (var forbidden in new[]
                {
                    "SPECIFICHE TECNICHE:", "animali della jungla", "Bambini 6–9 anni",
                    "Spesso — Bold", "Semplice / minimo"
                })
                    Require(!instruction.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                        "Testo italiano/legacy ancora presente nella Work Unit: " + forbidden);
            }

            var promptEngine = manifest.RootElement.GetProperty("prompt_engine");
            Require(promptEngine.GetProperty("manual_prompt_present").GetBoolean() == false,
                "Il falso manual override senza delta non viene riparato durante l'export.");
            Require(string.IsNullOrWhiteSpace(promptEngine.GetProperty("manual_delta").GetString()),
                "Il falso manual override produce un delta inesistente.");
            Require(promptEngine.GetProperty("provider_compiler_version").GetString() == PromptEngineeringCompiler.Version,
                "Versione compiler errata nel manifest.");

            foreach (var required in new[]
            {
                "## Modes", "## Essential transport rules", "## Image-generation integrity — HARD",
                "genuine image-generation/illustration capability", "primitive SVG/Canvas/Pillow geometry",
                "professionally illustrated coloring page FIRST", "return `INCOMPLETE` or `FAILED`",
                "rough-draft, scribble-like, placeholder, primitive-geometric"
            })
                Require(instructions.Contains(required, StringComparison.OrdinalIgnoreCase),
                    "Contratto inglese/qualitativo mancante da instructions.md: " + required);

            foreach (var forbidden in new[]
            {
                "Questo package descrive", "## Modalità", "Regole essenziali", "SPECIFICHE TECNICHE:"
            })
                Require(!instructions.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    "instructions.md contiene ancora testo operativo italiano/legacy: " + forbidden);
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
