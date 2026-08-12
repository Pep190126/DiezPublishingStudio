using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class AiExchangeFailedResponseImportSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezFailedResponseImport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "failed-response.diez");
            var project = ProjectFileStore.Create("Failed Response Import Regression");
            project.EditionMetadata.Title = "Animali jungla";
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            var profile = BookTypePromptProfileService.LoadColoring(project);
            profile.SubjectDescription = "3 animali diversi 3 immagini";
            profile.EnvironmentDescription = "jungla";
            profile.Style = "Kawaii / Cartoon";
            profile.TargetAudience = "Bambini 6–9 anni";
            profile.Difficulty = "Facile";
            profile.LineWeight = "Spesso — Bold";
            profile.Complexity = "Bassa";
            profile.ElementDensity = "Bassa";
            profile.Background = "Contestuale leggero";
            BookTypePromptProfileService.SaveColoring(project, profile);

            const string mustDo = "3 immagini di animali della jungla, riempi lo sfondo con ambientazione jungla";
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
                MustDo = mustDo,
                Prompt = PromptEngineeringCompiler.BuildSeriesPrompt(
                    project, 3, mustDo, string.Empty, PromptEngineeringProviderIds.OpenAi, true)
            });
            PromptMasterMetadataStore.MarkGenerated(
                project, 3, mustDo, string.Empty, PromptEngineeringProviderIds.OpenAi, true);

            var jobs = AiImageBatchService.CreateImageSeries(project, 3, mustDo, "Tavola").ToList();
            VisualPromptSessionService.EnsureActive(project);
            await ProjectFileStore.SaveAsync(projectPath, project);
            var state = AiExchangeStateStore.Load(project);
            var active = VisualPromptSessionService.ActiveLegacyJobIds(project);
            var units = state.WorkUnits
                .Where(u => u.LegacyAiJobId.HasValue && active.Contains(u.LegacyAiJobId.Value))
                .OrderBy(u => u.Position)
                .ToList();
            Require(units.Count == 3, "Attese tre Work Unit attive.");

            var promptPath = Path.Combine(root, "prompt.zip");
            var pack = await AiExchangePromptPackBuilder.BuildAsync(
                project, projectPath, state, units.Select(u => u.WorkUnitId), promptPath);
            Require(pack.Success, "Prompt Pack base non creato.");
            var snapshot = state.RequestSnapshots.Single(s => s.PromptPackId == pack.PromptPackId);

            var responsePath = Path.Combine(root, "response.zip");
            var renderIds = units.Select(_ => Guid.NewGuid().ToString("D")).ToArray();
            var promptHashes = units.Select((_, i) => new string((char)('a' + i), 64)).ToArray();
            await using (var stream = File.Create(responsePath))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                var items = units.Select((unit, i) => new
                {
                    work_unit_id = unit.WorkUnitId,
                    candidate_version = snapshot.Items.Single(s => s.WorkUnitId == unit.WorkUnitId).TargetCandidateVersion,
                    content_type = "IMAGE",
                    status = "FAILED",
                    primary_asset = "",
                    description = "Renderer returned a three-panel composition instead of one Work Unit composition.",
                    render_request_id = renderIds[i],
                    render_prompt_sha256 = promptHashes[i],
                    failure_reason = "PRIMARY SUBJECT / one-composition hard lock failed after the allowed fresh retry; non-compliant asset discarded."
                }).ToArray();
                var manifest = new
                {
                    protocol = "diez-response",
                    protocol_version = 1,
                    project_id = project.ProjectId,
                    job_id = snapshot.JobId,
                    prompt_pack_id = pack.PromptPackId,
                    package_id = "FAILED-V002-REGRESSION-" + Guid.NewGuid().ToString("N"),
                    partial = false,
                    items
                };
                var entry = zip.CreateEntry("response-manifest.json");
                await using var target = entry.Open();
                await using var writer = new StreamWriter(target, new UTF8Encoding(false));
                await writer.WriteAsync(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                zip.CreateEntry("content/");
            }

            var beforeVersions = state.Versions.Count;
            var beforeMaterials = project.Materials.Count;
            var result = await AiExchangeResponseImportV2.ImportAsync(project, projectPath, state, [responsePath]);
            Require(result.Summary.Success, "Un Response valido tutto FAILED viene ancora trattato come import fallito. " + result.Message);
            Require(result.Message.Contains("3 FAILED provider registrati", StringComparison.OrdinalIgnoreCase),
                "Il riepilogo non distingue i FAILED provider dagli errori package. " + result.Message);
            Require(state.Versions.Count == beforeVersions, "Il FAILED provider ha creato Candidate finte.");
            Require(project.Materials.Count == beforeMaterials, "Il FAILED provider ha creato Material fittizi.");

            foreach (var (unit, i) in units.Select((u, i) => (u, i)))
            {
                var failure = AiExchangeResponseFailureStore.Latest(project, unit.WorkUnitId);
                Require(failure is not null, unit.Code + ": FAILED non persistito.");
                Require(failure!.CandidateVersion == snapshot.Items.Single(s => s.WorkUnitId == unit.WorkUnitId).TargetCandidateVersion,
                    unit.Code + ": candidate_version FAILED non preservata.");
                Require(failure.RenderRequestId == renderIds[i], unit.Code + ": render_request_id non preservato.");
                Require(failure.RenderPromptSha256 == promptHashes[i], unit.Code + ": hash renderer non preservato.");
                Require(failure.FailureReason.Contains("one-composition", StringComparison.OrdinalIgnoreCase),
                    unit.Code + ": failure_reason non preservato.");
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAILED RESPONSE IMPORT SELF-TEST: " + message);
    }
}
