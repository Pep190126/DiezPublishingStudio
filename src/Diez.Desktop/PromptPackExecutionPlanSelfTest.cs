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
        Require(ColoringIndependentHardProfileService.SelectableStyles.Contains("Kawaii", StringComparer.OrdinalIgnoreCase),
            "Kawaii non disponibile come stile singolo.");
        Require(ColoringIndependentHardProfileService.SelectableStyles.Contains("Cartoon", StringComparer.OrdinalIgnoreCase),
            "Cartoon non disponibile come stile singolo.");
        Require(!ColoringIndependentHardProfileService.SelectableStyles.Contains("Cozy", StringComparer.OrdinalIgnoreCase),
            "Cozy è ancora erroneamente esposto come stile anziché parametro HARD indipendente.");
        Require(!ColoringIndependentHardProfileService.SelectableStyles.Contains("Bold & Easy", StringComparer.OrdinalIgnoreCase),
            "Bold & Easy è ancora erroneamente esposto come stile anziché parametro HARD indipendente.");

        var rejectedNonAtomic = false;
        try
        {
            PromptPackProviderFacingService.EnsureRendererPromptReady(
                "Create ONE coloring-book illustration.\nPRIMARY SUBJECT — HARD LOCK: 3 animals different 3 images. The subject must be dominant.\nSTYLE — HARD LOCK: Kawaii.\nBOLD & EASY — HARD: OFF.\nCOZY — HARD: OFF.",
                "IMG-TEST");
        }
        catch (InvalidOperationException) { rejectedNonAtomic = true; }
        Require(rejectedNonAtomic, "Il preflight non blocca un PRIMARY SUBJECT contenente quantità di serie / images.");

        var root = Path.Combine(Path.GetTempPath(), "DiezRenderPlanTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "project.diez");
            var project = ProjectFileStore.Create("Progetto tecnico interno");
            project.EditionMetadata.Title = "Animali della Giungla";
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            var profile = BookTypePromptProfileService.LoadColoring(project);
            // Exact physical v002 failure family: the subject itself contains the series count while jungle
            // only appears in environment/MUST DO. The old resolver therefore failed to choose one species.
            profile.SubjectDescription = "3 animali diversi 3 immagini";
            profile.EnvironmentDescription = "jungla";
            profile.Style = "Kawaii";
            profile.TargetAudience = "Bambini 6–9 anni";
            profile.Difficulty = "Facile";
            profile.LineWeight = "Sottile — Fine";
            profile.BoldEasy = true; // must be resolved OFF because thin lines are authoritative.
            profile.Complexity = "Media";
            profile.ElementDensity = "Media";
            profile.Background = "Contestuale leggero";
            BookTypePromptProfileService.SaveColoring(project, profile);
            ColoringBoldEasyPolicyStore.Save(project, true, profile.LineWeight);
            ColoringCozyPolicyStore.Save(project, false);
            project.Entities.Add(new GraphEntity
            {
                Kind = "DiezImageGenerationSpecs",
                Name = "Specifiche immagini",
                IsCandidate = false,
                Notes = "{\"PresetId\":\"kdp_letter\",\"Width\":\"8.5\",\"Height\":\"11\",\"Unit\":\"in\",\"AspectRatio\":\"17:22\",\"ResolutionClassId\":\"custom\",\"PixelWidth\":\"2550\",\"PixelHeight\":\"3300\",\"Dpi\":\"300\",\"Quality\":\"Massima / stampa\",\"LineDetail\":\"Linee semplici e pulite\"}"
            });
            PromptPreparationSettingsStore.Save(project, new PromptPreparationSettings
            {
                ProviderId = PromptEngineeringProviderIds.OpenAi,
                PreferAdvancedModel = true
            });
            const string mustDo = "3 immagini di animali della jungla, riempi lo sfondo con ambientazione jungla";
            PromptMasterStateStore.Save(project, new PromptMasterState
            {
                BookType = BookTypeProfileService.ColoringBook,
                ProviderId = PromptEngineeringProviderIds.OpenAi,
                PreferAdvancedModel = true,
                SeriesCount = 3,
                MustDo = mustDo,
                Prompt = PromptEngineeringCompiler.BuildSeriesPrompt(
                    project, 3, mustDo, string.Empty,
                    PromptEngineeringProviderIds.OpenAi, true)
            });
            PromptMasterMetadataStore.MarkGenerated(
                project, 3, mustDo, string.Empty,
                PromptEngineeringProviderIds.OpenAi, true);

            var jobs = AiImageBatchService.CreateImageSeries(project, 3, mustDo, "Tavola").ToList();
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
            Require(result.Success, "Build Prompt Pack v002 regression fallita: " + result.Message);
            Require(BookPackageNamingService.PeekNextVersion(project) == 2, "Versione package non avanzata dopo export riuscito.");

            using var zip = ZipFile.OpenRead(packPath);
            Require(zip.GetEntry("00-START-HERE.md") is not null, "00-START-HERE.md mancante.");
            Require(zip.GetEntry("render-plan.json") is not null, "render-plan.json mancante.");
            var start = await ReadAsync(zip, "00-START-HERE.md");
            Require(start.Contains(expectedResponseName, StringComparison.Ordinal), "Nome response non presente nel runbook.");
            Require(start.Contains("NEW/FRESH image generation", StringComparison.OrdinalIgnoreCase), "Regola fresh generation mancante.");
            Require(start.Contains("STYLE — HARD LOCK", StringComparison.Ordinal), "Runbook non verifica lo style hard lock.");
            Require(start.Contains("triptych", StringComparison.OrdinalIgnoreCase), "Runbook non classifica triptych come failure.");

            var planText = await ReadAsync(zip, "render-plan.json");
            using var plan = JsonDocument.Parse(planText);
            Require(plan.RootElement.GetProperty("protocol_version").GetString() == PromptPackExecutionPlanService.ProtocolVersion,
                "Versione render plan non aggiornata.");
            Require(PromptPackExecutionPlanService.ProtocolVersion == "1.2", "Protocollo render plan atteso 1.2.");
            Require(plan.RootElement.GetProperty("response_filename").GetString() == expectedResponseName, "Nome response errato nel render plan.");
            Require(plan.RootElement.GetProperty("atomic_subject_required").GetBoolean(), "atomic_subject_required non attivo.");
            Require(plan.RootElement.GetProperty("selected_style_is_hard").GetBoolean(), "selected_style_is_hard non attivo.");
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
                Require(call.GetProperty("hard_style_guard").GetString() == "STYLE — HARD LOCK", "hard_style_guard errato.");
                Require(call.GetProperty("hard_composition_guard").GetString() == "COMPOSITION — HARD LOCK", "hard_composition_guard errato.");
                var promptFile = call.GetProperty("prompt_file").GetString() ?? string.Empty;
                var expectedSha = call.GetProperty("prompt_sha256").GetString() ?? string.Empty;
                var prompt = await ReadAsync(zip, promptFile);
                var actualSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt))).ToLowerInvariant();
                Require(actualSha == expectedSha, "SHA prompt file non corrisponde al render plan.");
                Require(prompt.StartsWith("FRESH GENERATION — HARD RESET", StringComparison.Ordinal), "Prompt non inizia con hard reset renderer.");
                Require(prompt.Contains("PRIMARY SUBJECT — HARD LOCK: " + expectedSubjects[i], StringComparison.OrdinalIgnoreCase),
                    "Soggetto concreto errato per v002: " + expectedSubjects[i]);
                Require(prompt.Contains("STYLE — HARD LOCK: Kawaii", StringComparison.OrdinalIgnoreCase),
                    "Stile Kawaii singolo non è HARD nel renderer brief.");
                Require(!prompt.Contains("Kawaii / Cartoon", StringComparison.OrdinalIgnoreCase),
                    "Il vecchio stile combinato Kawaii / Cartoon è ancora nel renderer brief.");
                Require(prompt.Contains("BOLD & EASY — HARD: OFF", StringComparison.Ordinal),
                    "Linee sottili non producono Bold & Easy HARD OFF.");
                Require(prompt.Contains("COZY — HARD: OFF", StringComparison.Ordinal),
                    "Cozy HARD OFF non arriva nel renderer brief.");
                Require(prompt.Contains("Thin — Fine", StringComparison.OrdinalIgnoreCase),
                    "Spessore Thin/Fine non arriva al renderer brief.");
                Require(prompt.Contains("COMPOSITION — HARD LOCK", StringComparison.Ordinal),
                    "Composizione singola non è HARD nel renderer brief.");
                Require(prompt.Contains("realistic natural-history", StringComparison.OrdinalIgnoreCase),
                    "Kawaii hard lock non respinge la resa realistica osservata nelle immagini fisiche.");
                Require(prompt.Contains("Simple clean lines", StringComparison.OrdinalIgnoreCase),
                    "Linee semplici e pulite non normalizzato in inglese.");
                foreach (var forbidden in new[]
                {
                    "PRIMARY SUBJECT — HARD LOCK: 3", "3 animals", "3 images", "Linee semplici e pulite",
                    "SPECIFICHE TECNICHE", "Massima / stampa"
                })
                    Require(!prompt.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                        "Contaminazione v002 nel renderer prompt: " + forbidden);
            }

            var manifest = await ReadAsync(zip, "prompt-manifest.json");
            Require(manifest.Contains(expectedPackName, StringComparison.Ordinal), "Naming Prompt Pack assente dal manifest.");
            Require(manifest.Contains(expectedResponseName, StringComparison.Ordinal), "Naming Response assente dal manifest.");
            Require(manifest.Contains(project.ProjectId.ToString("D"), StringComparison.OrdinalIgnoreCase), "ProjectId interno assente dal manifest.");

            // Positive-direction regression: the same independent dimensions must be HARD when ON too.
            var p2 = BookTypePromptProfileService.LoadColoring(project);
            p2.LineWeight = "Spesso — Bold";
            p2.BoldEasy = true;
            p2.Style = "Cartoon";
            BookTypePromptProfileService.SaveColoring(project, p2);
            ColoringBoldEasyPolicyStore.Save(project, true, p2.LineWeight);
            ColoringCozyPolicyStore.Save(project, true);
            var onPrompt = PromptPackProviderFacingService.BuildImageGenerationPrompt(
                project, units[0], 3, 1, PromptPreparationSettingsStore.Load(project));
            Require(onPrompt.Contains("STYLE — HARD LOCK: Cartoon", StringComparison.OrdinalIgnoreCase),
                "Cartoon non resta uno stile singolo indipendente.");
            Require(onPrompt.Contains("BOLD & EASY — HARD: ON", StringComparison.Ordinal),
                "Bold & Easy HARD ON non arriva al renderer brief.");
            Require(onPrompt.Contains("COZY — HARD: ON", StringComparison.Ordinal),
                "Cozy HARD ON non arriva al renderer brief.");
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
