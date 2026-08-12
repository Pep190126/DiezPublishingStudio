using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class PromptPackExecutionPlanSelfTest
{
    public static async Task RunAsync()
    {
        Require(BookPackageNamingService.Slug("Il Libro È Mio!") == "il-libro-e-mio", "Slug titolo non stabile.");
        Require(SingleWindowPromptTargetAiUi.LooksLikeLegacyGeneratedPrompt(
                "REGOLE COMUNI DEL PROGETTO:\n...\nSPECIFICHE TECNICHE:\n- Qualità rendering: Massima / stampa"),
            "Il vecchio prompt tecnico italiano non viene riconosciuto come legacy.");

        var root = Path.Combine(Path.GetTempPath(), "DiezRenderPlanTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "project.diez");
            var project = ProjectFileStore.Create("Progetto tecnico interno");
            project.EditionMetadata.Title = "Animali della Giungla";
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            var profile = BookTypePromptProfileService.LoadColoring(project);
            profile.SubjectDescription = "animali della giungla";
            profile.EnvironmentDescription = "giungla";
            profile.Style = "Bold & Easy";
            profile.TargetAudience = "Bambini 6–9 anni";
            profile.Difficulty = "Facile";
            profile.LineWeight = "Spesso — Bold";
            profile.Complexity = "Bassa";
            profile.ElementDensity = "Bassa";
            profile.Background = "Contestuale leggero";
            BookTypePromptProfileService.SaveColoring(project, profile);
            PromptPreparationSettingsStore.Save(project, new PromptPreparationSettings
            {
                ProviderId = PromptEngineeringProviderIds.OpenAi,
                PreferAdvancedModel = true
            });
            PromptMasterStateStore.Save(project, new PromptMasterState
            {
                BookType = BookTypeProfileService.ColoringBook,
                ProviderId = PromptEngineeringProviderIds.OpenAi,
                PreferAdvancedModel = true,
                SeriesCount = 3,
                MustDo = "3 immagini separate di animali della giungla",
                Prompt = PromptEngineeringCompiler.BuildSeriesPrompt(
                    project, 3, "3 immagini separate di animali della giungla", string.Empty,
                    PromptEngineeringProviderIds.OpenAi, true)
            });
            PromptMasterMetadataStore.MarkGenerated(
                project, 3, "3 immagini separate di animali della giungla", string.Empty,
                PromptEngineeringProviderIds.OpenAi, true);

            var jobs = AiImageBatchService.CreateImageSeries(project, 3, "3 immagini separate di animali della giungla", "Tavola").ToList();
            Require(jobs.Count == 3, "Non sono stati creati tre job immagine.");
            VisualPromptSessionService.EnsureActive(project);
            await ProjectFileStore.SaveAsync(projectPath, project);

            var state = AiExchangeStateStore.Load(project);
            var units = state.WorkUnits
                .Where(u => jobs.Any(j => j.JobId == u.LegacyAiJobId))
                .OrderBy(u => u.Position)
                .ToList();
            Require(units.Count == 3, "Non sono state create tre Work Unit.");
            foreach (var unit in units) unit.Mode = "AI_ONLY";
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(projectPath, project);

            var expectedPackName = "diez-animali-della-giungla-prompt-pack-v001.zip";
            var expectedResponseName = "diez-animali-della-giungla-response-v001.zip";
            Require(BookPackageNamingService.PromptPackFileName(project, 1) == expectedPackName, "Nome Prompt Pack errato.");
            Require(BookPackageNamingService.ResponseFileName(project, 1) == expectedResponseName, "Nome Response errato.");

            var packPath = Path.Combine(root, expectedPackName);
            var result = await AiVisualPromptPackService.BuildAsync(
                project, projectPath, state, units.Select(u => u.WorkUnitId), packPath);
            Require(result.Success, "Build Prompt Pack fallita: " + result.Message);
            Require(BookPackageNamingService.PeekNextVersion(project) == 2, "Versione package non avanzata dopo export riuscito.");

            using var zip = ZipFile.OpenRead(packPath);
            Require(zip.GetEntry("00-START-HERE.md") is not null, "00-START-HERE.md mancante.");
            Require(zip.GetEntry("render-plan.json") is not null, "render-plan.json mancante.");
            var start = await ReadAsync(zip, "00-START-HERE.md");
            Require(start.Contains(expectedResponseName, StringComparison.Ordinal), "Nome response non presente nel runbook.");
            Require(start.Contains("NEW/FRESH image generation", StringComparison.OrdinalIgnoreCase), "Regola fresh generation mancante.");
            Require(start.Contains("previous Work Unit", StringComparison.OrdinalIgnoreCase), "Divieto riuso Work Unit precedente mancante.");

            var planText = await ReadAsync(zip, "render-plan.json");
            using var plan = JsonDocument.Parse(planText);
            Require(plan.RootElement.GetProperty("response_filename").GetString() == expectedResponseName, "Nome response errato nel render plan.");
            var calls = plan.RootElement.GetProperty("calls").EnumerateArray().ToList();
            Require(calls.Count == 3, "Render plan non contiene tre chiamate.");
            Require(calls.Select(c => c.GetProperty("render_request_id").GetString()).Distinct().Count() == 3,
                "render_request_id non univoci.");

            var expectedSubjects = new[] { "one monkey", "one tiger", "one elephant" };
            for (var i = 0; i < calls.Count; i++)
            {
                var call = calls[i];
                Require(call.GetProperty("fresh_generation_required").GetBoolean(), "fresh_generation_required non true.");
                Require(call.GetProperty("reuse_prior_generated_images_forbidden").GetBoolean(), "Divieto riuso immagini precedenti non true.");
                Require(call.GetProperty("source_image_policy").GetString() == "BLANK_CANVAS_NO_INPUT_IMAGES", "AI_ONLY non parte da blank canvas.");
                var promptFile = call.GetProperty("prompt_file").GetString() ?? string.Empty;
                var expectedSha = call.GetProperty("prompt_sha256").GetString() ?? string.Empty;
                var prompt = await ReadAsync(zip, promptFile);
                var actualSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt))).ToLowerInvariant();
                Require(actualSha == expectedSha, "SHA prompt file non corrisponde al render plan.");
                Require(prompt.StartsWith("FRESH GENERATION — HARD RESET", StringComparison.Ordinal), "Prompt non inizia con hard reset renderer.");
                Require(prompt.Contains(expectedSubjects[i], StringComparison.OrdinalIgnoreCase), "Soggetto concreto errato: " + expectedSubjects[i]);
                Require(prompt.Contains("PRIMARY SUBJECT — HARD LOCK", StringComparison.Ordinal), "Hard lock soggetto mancante.");
                Require(!prompt.Contains("SPECIFICHE TECNICHE", StringComparison.OrdinalIgnoreCase), "Specifiche tecniche italiane nel prompt renderer.");
                Require(!prompt.Contains("Massima / stampa", StringComparison.OrdinalIgnoreCase), "Qualità tecnica italiana nel prompt renderer.");
            }

            var manifest = await ReadAsync(zip, "prompt-manifest.json");
            Require(manifest.Contains(expectedPackName, StringComparison.Ordinal), "Naming Prompt Pack assente dal manifest.");
            Require(manifest.Contains(expectedResponseName, StringComparison.Ordinal), "Naming Response assente dal manifest.");
            Require(manifest.Contains(project.ProjectId.ToString("D"), StringComparison.OrdinalIgnoreCase), "ProjectId interno assente dal manifest.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static async Task<string> ReadAsync(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidOperationException("Entry mancante: " + path);
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return await reader.ReadToEndAsync();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("PROMPT PACK EXECUTION PLAN SELF-TEST: " + message);
    }
}
