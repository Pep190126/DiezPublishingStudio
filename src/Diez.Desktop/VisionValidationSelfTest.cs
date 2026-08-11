using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class VisionValidationSelfTest
{
    private static readonly byte[] GoodPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAaklEQVR42u2XwRIAEAhE+/+f5uRGZKOYbcbF1OzLUJESbEIAAnQ3RY4sE4B7pl8BWI/ZDWAmovnAACvimi8EYBEfxcAA6MXbBkBfRIsnAAEI8G4dSFEJw3tBim6YYh64OhGFDqX8FxDgplV2O053240oowAAAABJRU5ErkJggg==");

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezVisionSelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "vision.diez");
            var project = ProjectFileStore.Create("Vision QA Test");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            var profile = BookTypePromptProfileService.LoadColoring(project);
            profile.SubjectDescription = "jungle elephant";
            profile.EnvironmentDescription = "simple jungle foliage";
            profile.Style = "Bold & Easy";
            profile.TargetAudience = "Bambini 6–9 anni";
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
                SeriesCount = 1,
                MustDo = "one friendly jungle elephant",
                MustNotDo = "no village, no lake",
                Prompt = string.Empty
            });

            var jobs = AiImageBatchService.CreateImageSeries(project, 1, "one friendly jungle elephant", "Page").ToList();
            VisualPromptSessionService.EnsureActive(project);
            await ProjectFileStore.SaveAsync(projectPath, project);
            var state = AiExchangeStateStore.Load(project);
            var unit = state.WorkUnits.Single(w => w.LegacyAiJobId == jobs[0].JobId);

            var image = Path.Combine(root, "candidate.png");
            await File.WriteAllBytesAsync(image, GoodPng);
            var imported = await AiExchangeResultIngestor.IngestAsync(project, state, new AiExchangeNormalizedResultItem
            {
                WorkUnitId = unit.WorkUnitId,
                CandidateVersion = 1,
                ContentType = AiExchangeContentTypes.Image,
                ResultStatus = "COMPLETE",
                PrimaryAssetPath = image,
                Description = "A friendly jungle elephant coloring page.",
                Origin = AiExchangeOrigins.AiPromptPack
            });
            Require(imported.Status == "IMPORTED", "Candidate di test non importata.");
            var version = state.Versions.Single(v => v.WorkUnitId == unit.WorkUnitId && v.VersionNumber == 1);
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(projectPath, project);

            var packPath = Path.Combine(root, "vision-pack.zip");
            var pack = await VisionValidationPromptPackService.BuildAsync(
                project, projectPath, state, [version.VersionId], packPath);
            Require(pack.Success && pack.ValidationPackId != Guid.Empty, "Prompt Pack Vision non creato: " + pack.Message);
            using (var zip = ZipFile.OpenRead(packPath))
            {
                var manifest = await ReadAsync(zip, "vision-manifest.json");
                var instructions = await ReadAsync(zip, "instructions.md");
                Require(manifest.Contains(version.ContentSha256, StringComparison.OrdinalIgnoreCase),
                    "Il manifest Vision non lega la verifica all'hash reale della Candidate.");
                Require(manifest.Contains("jungle elephant", StringComparison.OrdinalIgnoreCase),
                    "La specifica semantica del soggetto non arriva al controllo Vision.");
                Require(manifest.Contains("no village, no lake", StringComparison.OrdinalIgnoreCase),
                    "MUST NOT DO non arriva al controllo Vision.");
                Require(manifest.Contains("COMMERCIAL COLORING BOOK", StringComparison.OrdinalIgnoreCase),
                    "Il contratto di generazione canonico non arriva al controllo Vision.");
                Require(instructions.Contains("inspect the REAL candidate image", StringComparison.OrdinalIgnoreCase),
                    "La Vision non viene obbligata a guardare il file reale.");
                Require(instructions.Contains("provider's description", StringComparison.OrdinalIgnoreCase),
                    "La descrizione del generatore non è dichiarata non affidabile.");
            }

            var passZip = Path.Combine(root, "vision-pass.zip");
            await WriteResponseAsync(passZip, project.ProjectId, pack.ValidationPackId, "VISION-PASS-1",
                PromptEngineeringProviderIds.OpenAi, version, unit,
                VisionValidationStatuses.Pass, 0.96,
                "A black-and-white coloring page showing a friendly elephant among simple jungle leaves.",
                "Requested subject and book type are visually aligned.",
                [
                    Check("subject_match", VisionCheckStatuses.Pass, VisionSeverity.Hard, 0.99, "One elephant is clearly visible."),
                    Check("must_not_do", VisionCheckStatuses.Pass, VisionSeverity.Hard, 0.98, "No village or lake is visible."),
                    Check("book_type_fit", VisionCheckStatuses.Pass, VisionSeverity.Hard, 0.97, "The asset is a coloring-page illustration."),
                    Check("publication_quality", VisionCheckStatuses.Pass, VisionSeverity.Soft, 0.91, "Clear focal subject and usable composition.")
                ]);
            var passReport = await VisionValidationPromptPackService.ImportAsync(project, projectPath, state, [passZip]);
            Require(passReport.Passed == 1 && passReport.Failed == 0,
                "Vision PASS non importato correttamente: " + passReport.Message);
            var passRecord = VisionValidationStore.Get(project, version.VersionId);
            Require(passRecord?.OverallStatus == VisionValidationStatuses.Pass && passRecord.BlocksApproval == false,
                "Vision PASS viene interpretato come blocco.");
            Require(AiExchangeApprovalService.CanApprove(project, state, version.VersionId, out _),
                "Una Candidate tecnicamente valida con Vision PASS non supera il gate.");

            // Same visual asset, next candidate version: simulate a vision model correctly detecting a semantic mismatch.
            var imported2 = await AiExchangeResultIngestor.IngestAsync(project, state, new AiExchangeNormalizedResultItem
            {
                WorkUnitId = unit.WorkUnitId,
                CandidateVersion = 2,
                ContentType = AiExchangeContentTypes.Image,
                ResultStatus = "COMPLETE",
                PrimaryAssetPath = image,
                Description = "Provider claims this is a jungle elephant.",
                Origin = AiExchangeOrigins.AiPromptPack
            });
            Require(imported2.Status == "IMPORTED", "Seconda Candidate di test non importata.");
            var badVersion = state.Versions.Single(v => v.WorkUnitId == unit.WorkUnitId && v.VersionNumber == 2);
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(projectPath, project);

            var badPackPath = Path.Combine(root, "vision-bad-pack.zip");
            var badPack = await VisionValidationPromptPackService.BuildAsync(
                project, projectPath, state, [badVersion.VersionId], badPackPath);
            Require(badPack.Success, "Secondo Prompt Pack Vision non creato.");
            var failZip = Path.Combine(root, "vision-fail.zip");
            await WriteResponseAsync(failZip, project.ProjectId, badPack.ValidationPackId, "VISION-FAIL-1",
                PromptEngineeringProviderIds.OpenAi, badVersion, unit,
                VisionValidationStatuses.Fail, 0.99,
                "A lakeside village landscape at sunset; no elephant is visible.",
                "Wrong subject and scene: the candidate does not satisfy the requested jungle-elephant work unit.",
                [
                    Check("subject_match", VisionCheckStatuses.Fail, VisionSeverity.Hard, 0.99, "No elephant is visible."),
                    Check("environment_match", VisionCheckStatuses.Fail, VisionSeverity.Hard, 0.98, "The scene is a lake/village, explicitly excluded."),
                    Check("book_type_fit", VisionCheckStatuses.Fail, VisionSeverity.Hard, 0.99, "The visible content is not the requested coloring-book subject."),
                    Check("publication_quality", VisionCheckStatuses.Warn, VisionSeverity.Soft, 0.85, "Could be visually polished but is unusable for this job.")
                ]);
            var failReport = await VisionValidationPromptPackService.ImportAsync(project, projectPath, state, [failZip]);
            Require(failReport.Failed == 1, "Vision FAIL non contabilizzato: " + failReport.Message);
            var failRecord = VisionValidationStore.Get(project, badVersion.VersionId);
            Require(failRecord?.BlocksApproval == true && badVersion.Status == AiExchangeVersionStatuses.Incomplete,
                "Il semantic mismatch non blocca la Candidate.");
            Require(!AiExchangeApprovalService.Approve(project, state, badVersion.VersionId, out var blocked) &&
                    blocked.Contains("Vision", StringComparison.OrdinalIgnoreCase),
                "Il gate di approvazione non impedisce l'approvazione di un HARD FAIL Vision.");

            // Hash binding: a report for a modified/replaced candidate must be rejected.
            var staleZip = Path.Combine(root, "vision-stale.zip");
            var stale = new AiExchangeVersion
            {
                VersionId = badVersion.VersionId,
                WorkUnitId = badVersion.WorkUnitId,
                VersionNumber = badVersion.VersionNumber,
                ContentSha256 = new string('0', 64)
            };
            await WriteResponseAsync(staleZip, project.ProjectId, badPack.ValidationPackId, "VISION-STALE-1",
                PromptEngineeringProviderIds.OpenAi, stale, unit,
                VisionValidationStatuses.Pass, 1,
                "Unrelated replacement image.", "Should be rejected by hash binding.",
                [Check("subject_match", VisionCheckStatuses.Pass, VisionSeverity.Hard, 1, "Irrelevant because hash is stale.")]);
            var staleReport = await VisionValidationPromptPackService.ImportAsync(project, projectPath, state, [staleZip]);
            Require(staleReport.Invalid == 1,
                "Un esito Vision riferito a un hash diverso è stato applicato alla Candidate corrente.");

            IAiExchangeApiAdapter api = new AiExchangeMockApiAdapter();
            Require(api.Capabilities.Vision,
                "La capability Vision non è dichiarata dall'adapter che può fare analisi multimodale.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static VisionValidationCheck Check(string key, string status, string severity, double confidence, string evidence) => new()
    {
        Key = key,
        Status = status,
        Severity = severity,
        Confidence = confidence,
        Evidence = evidence
    };

    private static async Task WriteResponseAsync(
        string path,
        Guid projectId,
        Guid packId,
        string packageId,
        string providerId,
        AiExchangeVersion version,
        AiExchangeWorkUnit unit,
        string overall,
        double confidence,
        string observed,
        string summary,
        IReadOnlyList<VisionValidationCheck> checks)
    {
        var payload = new
        {
            protocol = "diez-vision-response",
            protocol_version = 1,
            project_id = projectId,
            validation_pack_id = packId,
            package_id = packageId,
            provider_id = providerId,
            items = new[]
            {
                new
                {
                    version_id = version.VersionId,
                    work_unit_id = unit.WorkUnitId,
                    candidate_version = version.VersionNumber,
                    content_sha256 = version.ContentSha256,
                    overall_status = overall,
                    confidence,
                    observed_description = observed,
                    summary,
                    checks
                }
            }
        };
        await using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        var entry = zip.CreateEntry("vision-response.json");
        await using var target = entry.Open();
        await using var writer = new StreamWriter(target, new UTF8Encoding(false));
        await writer.WriteAsync(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        }));
    }

    private static async Task<string> ReadAsync(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidOperationException("Entry Vision mancante: " + path);
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("VISION VALIDATION SELF-TEST: " + message);
    }
}
