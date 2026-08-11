using System.IO.Compression;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

internal static class VisualPromptIsolationSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezPromptIsolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "isolation.diez");
            var project = ProjectFileStore.Create("Isolation Test");
            BookTypeProfileService.Set(project, BookTypeProfileService.ImageCollection);

            var illustration = ImageCollectionPromptProfileService.Load(project);
            illustration.SubjectDescription = "OLD COLLECTION SUNSET LANDSCAPES";
            illustration.RenderingStyle = "Fotografico / realistico";
            illustration.ColorMode = "Colore pieno";
            ImageCollectionPromptProfileService.Save(project, illustration);

            var oldJobs = AiImageBatchService.CreateImageSeries(project, 2, "landscape photographs", "Old collection").ToList();
            VisualPromptSessionService.EnsureActive(project);
            Require(VisualPromptSessionService.ActiveImageJobs(project).Count == 2, "La sessione Raccolta immagini non adotta i job iniziali.");
            await ProjectFileStore.SaveAsync(projectPath, project);

            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            Require(project.AiProductionJobs.All(j => !oldJobs.Any(o => o.JobId == j.JobId)),
                "I job della Raccolta immagini sono ancora nella lista operativa dopo il cambio Tipo libro.");
            Require(VisualPromptSessionService.ArchivedJobCount(project) >= 2,
                "I job precedenti non sono stati preservati nello storico della sessione.");
            Require(VisualPromptSessionService.ActiveImageJobs(project).Count == 0,
                "La nuova sessione Coloring non parte vuota.");

            var coloring = BookTypePromptProfileService.LoadColoring(project);
            coloring.SubjectDescription = "jungle animals";
            coloring.Style = "Bold & Easy";
            BookTypePromptProfileService.SaveColoring(project, coloring);
            var jobs = AiImageBatchService.CreateImageSeries(project, 3, "jungle animals", "Coloring").ToList();
            VisualPromptSessionService.EnsureActive(project);
            Require(VisualPromptSessionService.ActiveImageJobs(project).Count == 3,
                "I nuovi job Coloring non vengono adottati nella sessione attiva.");

            var master = PromptEngineeringEngine.BuildSeriesPrompt(
                project, 3, "jungle animals", string.Empty, PromptEngineeringProviderIds.OpenAi, true);
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
                MustDo = "jungle animals",
                Prompt = master
            });
            for (var i = 0; i < jobs.Count; i++)
                jobs[i].Prompt = PromptEngineeringEngine.BuildItemPrompt(project, master, 3, i + 1, jobs[i].Code, PromptEngineeringProviderIds.OpenAi, true);

            await ProjectFileStore.SaveAsync(projectPath, project);
            var state = AiExchangeStateStore.Load(project);
            var activeIds = VisualPromptSessionService.ActiveLegacyJobIds(project);
            var units = state.WorkUnits.Where(u => u.LegacyAiJobId.HasValue && activeIds.Contains(u.LegacyAiJobId.Value)).ToList();
            Require(units.Count == 3, "Le Work Unit attive non corrispondono ai soli job Coloring.");

            var packPath = Path.Combine(root, "isolated.zip");
            var built = await AiExchangePromptPackBuilder.BuildAsync(project, projectPath, state, units.Select(u => u.WorkUnitId), packPath);
            Require(built.Success, "Prompt Pack core non creato.");
            var enhanced = await AiExchangeImageRequestContextSafeEnhancer.EnhancePromptPackAsync(project, projectPath, state, units.Select(u => u.WorkUnitId), packPath);
            Require(enhanced.Success, "Enrichment immagini fallito.");
            PromptPackPromptEngineeringFinalizer.Finalize(packPath, project, state, units.Select(u => u.WorkUnitId));

            using var zip = ZipFile.OpenRead(packPath);
            var context = await ReadAsync(zip, "request-context.json");
            var manifest = await ReadAsync(zip, "prompt-manifest.json");
            Require(context.Contains("active_profile_kind", StringComparison.OrdinalIgnoreCase) &&
                    context.Contains("COLORING_BOOK", StringComparison.Ordinal),
                "Il contesto non dichiara il solo profilo Coloring attivo.");
            Require(!context.Contains("illustration_profile", StringComparison.OrdinalIgnoreCase),
                "Il profilo Raccolta immagini viene ancora esportato nel request-context Coloring.");
            Require(!context.Contains("OLD COLLECTION SUNSET LANDSCAPES", StringComparison.OrdinalIgnoreCase),
                "Testo del profilo Raccolta immagini contaminato nel Prompt Pack Coloring.");
            Require(manifest.Contains("output_count_for_this_work_unit", StringComparison.OrdinalIgnoreCase),
                "Il manifest non espone il contratto 1 output per Work Unit.");
            Require(Count(manifest, "Generate EXACTLY ONE image") == 3,
                "Non tutte le tre Work Unit impongono esattamente un'immagine.");
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
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }
        return count;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("VISUAL PROMPT ISOLATION SELF-TEST: " + message);
    }
}
