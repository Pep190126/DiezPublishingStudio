using System.IO.Compression;
using System.Runtime.CompilerServices;
using DiezPublishingStudio;

namespace DiezPublishingStudio.UnoSpike;

internal sealed record VisualJobSyncResult(
    bool Success,
    int Created,
    int Existing,
    string Message,
    IReadOnlyList<DiezAiFrontendJob> Jobs);

internal sealed record VisualResponseImportResult(
    bool Success,
    int Candidates,
    int ProviderFailed,
    int Duplicates,
    string Message,
    string RecoveryPath);

/// <summary>
/// Temporary migration adapter: DiezProjectDocument still owns ZIP preservation while the
/// visual-domain state is already owned by Diez.Core. UnsafeAccessor keeps this strongly typed
/// and avoids runtime reflection; remove this shim when package ownership moves into Core.
/// </summary>
internal static class VisualBookDocumentAdapter
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ApplyCoreJson")]
    private static extern void ApplyCoreJson(DiezProjectDocument document, string json);

    public static DiezVisualBookSetupDto ReadVisualSetup(this DiezProjectDocument document) =>
        DiezVisualBookFrontendBridge.Read(document.ExportProjectJson());

    public static DiezVisualSceneStateDto ReadVisualSceneState(this DiezProjectDocument document) =>
        DiezVisualSceneFrontendBridge.Read(document.ExportProjectJson());

    public static DiezVisualSceneMutation ConfigureVisualSubjects(this DiezProjectDocument document, bool enabled, int requestedCount)
    {
        var result = DiezVisualSceneFrontendBridge.ConfigureSubjects(document.ExportProjectJson(), enabled, requestedCount);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualSceneMutation SaveVisualSubject(this DiezProjectDocument document, string subjectId, string? name, string? description)
    {
        var result = DiezVisualSceneFrontendBridge.SaveSubject(document.ExportProjectJson(), subjectId, name, description);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualSceneMutation SaveVisualConsistencyRule(this DiezProjectDocument document, string subjectId, string key, string? level, string? strategy, string? variation)
    {
        var result = DiezVisualSceneFrontendBridge.SaveConsistencyRule(document.ExportProjectJson(), subjectId, key, level, strategy, variation);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualSceneMutation ConfigureVisualScenes(this DiezProjectDocument document, bool enabled, int requestedCount)
    {
        var result = DiezVisualSceneFrontendBridge.ConfigureScenes(document.ExportProjectJson(), enabled, requestedCount);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualSceneMutation SaveVisualScene(this DiezProjectDocument document, string sceneId, string? name, string? description)
    {
        var result = DiezVisualSceneFrontendBridge.SaveScene(document.ExportProjectJson(), sceneId, name, description);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualSceneMutation SetVisualSceneParticipation(this DiezProjectDocument document, string sceneId, string subjectId, bool participates)
    {
        var result = DiezVisualSceneFrontendBridge.SetSceneParticipation(document.ExportProjectJson(), sceneId, subjectId, participates);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualBookMutation SaveColoringSetup(this DiezProjectDocument document, int imageCount, string? subject, string? environment, bool consistent, string? consistencyRules, DiezColoringProfileDto profile)
    {
        var result = DiezVisualBookFrontendBridge.SaveColoring(document.ExportProjectJson(), imageCount, subject, environment, consistent, consistencyRules, profile);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualBookMutation SaveImageBookSetup(this DiezProjectDocument document, string bookType, int imageCount, string? subject, string? environment, bool consistent, string? consistencyRules, DiezImageProfileDto profile)
    {
        var result = DiezVisualBookFrontendBridge.SaveImageBook(document.ExportProjectJson(), bookType, imageCount, subject, environment, consistent, consistencyRules, profile);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualPromptPack BuildVisualPromptPack(this DiezProjectDocument document, string? mustDo, string? mustNotDo, string providerId, bool preferAdvanced)
    {
        var result = DiezVisualBookFrontendBridge.BuildPromptPack(document.ExportProjectJson(), mustDo, mustNotDo, providerId, preferAdvanced);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static VisualJobSyncResult EnsureVisualReadyJobs(this DiezProjectDocument document, string? mustDo, string? mustNotDo, string providerId, bool preferAdvanced)
    {
        var result = DiezVisualJobFrontendBridge.SyncReadyJobs(document.ExportProjectJson(), mustDo, mustNotDo, providerId, preferAdvanced);
        if (result.Success) ApplyCoreJson(document, result.ProjectJson);
        return new VisualJobSyncResult(result.Success, result.Created, result.Existing, result.Message, result.Jobs);
    }

    public static IReadOnlyList<DiezPromptPackItemDto> PromptPackPreview(this DiezProjectDocument document, IEnumerable<Guid>? workUnitIds = null) =>
        DiezPromptPackFrontendBridge.Preview(document.ExportProjectJson(), workUnitIds);

    public static async Task<DiezPromptPackBuildResult> CreateManualPromptPackAsync(this DiezProjectDocument document, IEnumerable<Guid>? workUnitIds, string outputPath)
    {
        // Freeze the latest visual semantics, not the possibly older UI/job draft. Scene participation,
        // Subject identity and all independent Coloring HARD choices are authoritative at package time.
        var hard = DiezVisualHardPromptFrontendBridge.Recompile(document.ExportProjectJson(), workUnitIds);
        if (!hard.Success)
            return new DiezPromptPackBuildResult(
                document.ExportProjectJson(), false, hard.Status, hard.Message,
                Guid.Empty, Guid.Empty, 0, string.Empty, "MANUAL");
        ApplyCoreJson(document, hard.ProjectJson);

        var result = await DiezPromptPackBatchFrontendBridge.BuildManualPackageAsync(document.ExportProjectJson(), document.SourcePath, workUnitIds, outputPath);
        if (result.Success) ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    /// <summary>
    /// Imports one audited Response ZIP as a batch. Assets are extracted and ingested one at a time,
    /// then immediately embedded by SaveAsync before temporary files are removed.
    /// </summary>
    public static async Task<VisualResponseImportResult> ImportManualVisualResponsePackAsync(
        this DiezProjectDocument document,
        string zipPath)
    {
        if (string.IsNullOrWhiteSpace(document.SourcePath) || !File.Exists(document.SourcePath))
            return new(false, 0, 0, 0, "Salva prima il progetto .diez: il Response Pack deve poter incorporare subito gli asset importati.", string.Empty);

        var audit = await DiezVisualResponsePackFrontendBridge.ReadAsync(document.ExportProjectJson(), zipPath);
        if (!audit.Success)
            return new(false, 0, 0, 0, $"Response non importato [{audit.Status}]: {audit.Message}", string.Empty);

        var tempRoot = Path.Combine(Path.GetTempPath(), "DiezVisualResponse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var candidates = 0;
        var providerFailed = 0;
        var duplicates = 0;
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var item in audit.Items)
            {
                if (string.Equals(item.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    var failed = DiezVisualResponsePackFrontendBridge.RecordProviderFailure(
                        document.ExportProjectJson(), audit.PackageId, audit.PromptPackId,
                        audit.RequestSnapshotId, item);
                    if (!failed.Success)
                        return new(false, candidates, providerFailed, duplicates, failed.Message, tempRoot);
                    ApplyCoreJson(document, failed.ProjectJson);
                    providerFailed++;
                    continue;
                }

                var entry = archive.Entries.FirstOrDefault(x =>
                    string.Equals(NormalizeZipPath(x.FullName), item.AssetEntryPath, StringComparison.Ordinal));
                if (entry is null)
                    return new(false, candidates, providerFailed, duplicates,
                        $"{item.Code}: l'asset verificato non è più presente nel Response ZIP.", tempRoot);

                var safeName = SafeAssetName(item.AssetFileName, item.Code);
                var localPath = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + "-" + safeName);
                await using (var source = entry.Open())
                await using (var destination = File.Create(localPath))
                    await source.CopyToAsync(destination);

                var ingest = await document.IngestAiImageResultAsync(
                    item.WorkUnitId,
                    localPath,
                    item.Description,
                    item.CandidateVersion,
                    item.Status);

                if (ingest.Status is not ("IMPORTED" or "UPDATED" or "DUPLICATE" or "INCOMPLETE"))
                    return new(false, candidates, providerFailed, duplicates,
                        $"{item.Code}: import interrotto — {ingest.Message}", tempRoot);

                if (ingest.Status == "DUPLICATE") duplicates++;
                else candidates++;

                if (ingest.Version is not null)
                {
                    var withSnapshot = DiezAiSnapshotFrontendBridge.AttachVersion(
                        document.ExportProjectJson(), ingest.Version.VersionId, audit.RequestSnapshotId);
                    ApplyCoreJson(document, withSnapshot);
                }
            }

            var marked = DiezVisualResponsePackFrontendBridge.MarkPackageImported(
                document.ExportProjectJson(), audit.PackageId);
            if (!marked.Success)
                return new(false, candidates, providerFailed, duplicates, marked.Message, tempRoot);
            ApplyCoreJson(document, marked.ProjectJson);

            await document.SaveAsync(document.SourcePath);
            try { Directory.Delete(tempRoot, true); } catch { }
            var total = candidates + providerFailed + duplicates;
            return new(true, candidates, providerFailed, duplicates,
                $"IMPORT RIUSCITO: {total} risultati dal Response ZIP · {candidates} Candidate · {providerFailed} FAILED provider · {duplicates} duplicati. Vision viene aperto con le Candidate importate.",
                string.Empty);
        }
        catch (Exception ex)
        {
            return new(false, candidates, providerFailed, duplicates,
                "Import Response ZIP non riuscito: " + ex.GetBaseException().Message + $" · file temporanei conservati in {tempRoot}",
                tempRoot);
        }
    }

    public static DiezVisualBookProgress VisualProgress(this DiezProjectDocument document) =>
        DiezVisualBookFrontendBridge.Progress(document.ExportProjectJson());

    public static DiezPublicationStateDto PublicationState(this DiezProjectDocument document) =>
        DiezPublicationFrontendBridge.Read(document.ExportProjectJson());

    public static DiezPublicationMutation CreateEditionFreeze(this DiezProjectDocument document, string? note = null)
    {
        var result = DiezPublicationFrontendBridge.CreateFreeze(document.ExportProjectJson(), note);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezPublicationMutation CreatePublicationCandidate(this DiezProjectDocument document)
    {
        var result = DiezPublicationFrontendBridge.CreatePublicationCandidate(document.ExportProjectJson());
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static Task<DiezFileExportResult> ExportPublicationPackageAsync(this DiezProjectDocument document, string outputPath) =>
        DiezPublicationFrontendBridge.ExportPublicationPackageAsync(document.ExportProjectJson(), outputPath);

    public static Task<DiezFileExportResult> ExportFinalVisualImagesAsync(this DiezProjectDocument document, string projectPath, string outputPath) =>
        DiezPublicationFrontendBridge.ExportFinalVisualImagesAsync(document.ExportProjectJson(), projectPath, outputPath);

    private static string NormalizeZipPath(string value) => value.Replace('\\', '/').Trim().TrimStart('/');

    private static string SafeAssetName(string? fileName, string code)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? code + ".bin" : Path.GetFileName(fileName);
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? code + ".bin" : name;
    }
}
