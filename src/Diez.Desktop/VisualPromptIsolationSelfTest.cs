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
            _ = AiImageBatchService.CreateImageSeries(project, 3, "jungle animals", "Coloring").ToList();
            VisualPromptSessionService.EnsureActive(project);
            Require(VisualPromptSessionService.ActiveImageJobs(project).Count == 3,
                "I nuovi job Coloring non vengono adottati nella sessione attiva.");

            PromptPreparationSettingsStore.Save(project, new PromptPreparationSettings
            {
                ProviderId = PromptEngineeringProviderIds.OpenAi,
                PreferAdvancedModel = true
            });
            await ProjectFileStore.SaveAsync(projectPath, project);

            var state = AiExchangeStateStore.Load(project);
            var activeIds = VisualPromptSessionService.ActiveLegacyJobIds(project);
            var units = state.WorkUnits
                .Where(u => u.LegacyAiJobId.HasValue && activeIds.Contains(u.LegacyAiJobId.Value))
                .OrderBy(u => u.Position)
                .ToList();
            Require(units.Count == 3, "Le Work Unit attive non corrispondono ai soli job Coloring.");

            var packPath = Path.Combine(root, "isolated.zip");
            var built = await AiVisualPromptPackService.BuildAsync(
                project, projectPath, state, units.Select(u => u.WorkUnitId), packPath);
            Require(built.Success, "Pipeline visuale centrale non crea il Prompt Pack: " + built.Message);

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
            Require(context.Contains($"provider_compiler_version\": \"{PromptEngineeringCompiler.Version}", StringComparison.OrdinalIgnoreCase),
                "Il request-context non usa il compiler provider-specific corrente.");

            var manifestRoot = JsonNode.Parse(manifest)?.AsObject()
                ?? throw new InvalidOperationException("Manifest finale non leggibile.");
            var engine = manifestRoot["prompt_engine"]?.AsObject()
                ?? throw new InvalidOperationException("prompt_engine finale mancante.");
            Require(engine["provider_compiler_version"]?.ToString() == PromptEngineeringCompiler.Version,
                "Versione provider compiler errata nel manifest.");
            Require((engine["master_prompt"]?.ToString() ?? string.Empty).Contains("PROVIDER EXECUTION PROFILE — OPENAI", StringComparison.Ordinal),
                "Il Prompt Pack non contiene la strategia OpenAI provider-specific.");

            var manifestUnits = manifestRoot["work_units"]?.AsArray()
                ?? throw new InvalidOperationException("work_units mancanti nel manifest finale.");
            Require(manifestUnits.Count == 3, $"Manifest finale atteso 3 Work Unit, trovate {manifestUnits.Count}.");
            foreach (var node in manifestUnits.OfType<JsonObject>())
            {
                var code = node["code"]?.ToString() ?? "?";
                Require(node["output_count_for_this_work_unit"]?.GetValue<int>() == 1,
                    $"{code}: output_count_for_this_work_unit non è 1.");
                var instruction = node["instruction"]?.ToString() ?? string.Empty;
                Require(instruction.Contains("Generate EXACTLY ONE image", StringComparison.Ordinal),
                    $"{code}: contratto EXACTLY ONE assente dall'istruzione finale.");
                Require(instruction.Contains("PROVIDER EXECUTION PROFILE — OPENAI", StringComparison.Ordinal),
                    $"{code}: strategia OpenAI assente dall'istruzione finale.");
            }
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("VISUAL PROMPT ISOLATION SELF-TEST: " + message);
    }
}
