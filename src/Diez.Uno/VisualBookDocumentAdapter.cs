using System.Runtime.CompilerServices;
using DiezPublishingStudio;

namespace DiezPublishingStudio.UnoSpike;

internal sealed record VisualJobSyncResult(
    bool Success,
    int Created,
    int Existing,
    string Message,
    IReadOnlyList<DiezAiFrontendJob> Jobs);

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

    public static DiezVisualSceneMutation ConfigureVisualSubjects(
        this DiezProjectDocument document,
        bool enabled,
        int requestedCount)
    {
        var result = DiezVisualSceneFrontendBridge.ConfigureSubjects(
            document.ExportProjectJson(), enabled, requestedCount);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualSceneMutation SaveVisualSubject(
        this DiezProjectDocument document,
        string subjectId,
        string? name,
        string? description)
    {
        var result = DiezVisualSceneFrontendBridge.SaveSubject(
            document.ExportProjectJson(), subjectId, name, description);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualSceneMutation SaveVisualConsistencyRule(
        this DiezProjectDocument document,
        string subjectId,
        string key,
        string? level,
        string? strategy,
        string? variation)
    {
        var result = DiezVisualSceneFrontendBridge.SaveConsistencyRule(
            document.ExportProjectJson(), subjectId, key, level, strategy, variation);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualSceneMutation ConfigureVisualScenes(
        this DiezProjectDocument document,
        bool enabled,
        int requestedCount)
    {
        var result = DiezVisualSceneFrontendBridge.ConfigureScenes(
            document.ExportProjectJson(), enabled, requestedCount);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualSceneMutation SaveVisualScene(
        this DiezProjectDocument document,
        string sceneId,
        string? name,
        string? description)
    {
        var result = DiezVisualSceneFrontendBridge.SaveScene(
            document.ExportProjectJson(), sceneId, name, description);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualSceneMutation SetVisualSceneParticipation(
        this DiezProjectDocument document,
        string sceneId,
        string subjectId,
        bool participates)
    {
        var result = DiezVisualSceneFrontendBridge.SetSceneParticipation(
            document.ExportProjectJson(), sceneId, subjectId, participates);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualBookMutation SaveColoringSetup(
        this DiezProjectDocument document,
        int imageCount,
        string? subject,
        string? environment,
        bool consistent,
        string? consistencyRules,
        DiezColoringProfileDto profile)
    {
        var result = DiezVisualBookFrontendBridge.SaveColoring(
            document.ExportProjectJson(), imageCount, subject, environment, consistent, consistencyRules, profile);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualBookMutation SaveImageBookSetup(
        this DiezProjectDocument document,
        string bookType,
        int imageCount,
        string? subject,
        string? environment,
        bool consistent,
        string? consistencyRules,
        DiezImageProfileDto profile)
    {
        var result = DiezVisualBookFrontendBridge.SaveImageBook(
            document.ExportProjectJson(), bookType, imageCount, subject, environment, consistent, consistencyRules, profile);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualPromptPack BuildVisualPromptPack(
        this DiezProjectDocument document,
        string? mustDo,
        string? mustNotDo,
        string providerId,
        bool preferAdvanced)
    {
        var result = DiezVisualBookFrontendBridge.BuildPromptPack(
            document.ExportProjectJson(), mustDo, mustNotDo, providerId, preferAdvanced);
        ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static VisualJobSyncResult EnsureVisualReadyJobs(
        this DiezProjectDocument document,
        string? mustDo,
        string? mustNotDo,
        string providerId,
        bool preferAdvanced)
    {
        var result = DiezVisualJobFrontendBridge.SyncReadyJobs(
            document.ExportProjectJson(), mustDo, mustNotDo, providerId, preferAdvanced);
        if (result.Success) ApplyCoreJson(document, result.ProjectJson);
        return new VisualJobSyncResult(
            result.Success,
            result.Created,
            result.Existing,
            result.Message,
            result.Jobs);
    }

    public static IReadOnlyList<DiezPromptPackItemDto> PromptPackPreview(
        this DiezProjectDocument document,
        IEnumerable<Guid>? workUnitIds = null) =>
        DiezPromptPackFrontendBridge.Preview(document.ExportProjectJson(), workUnitIds);

    public static async Task<DiezPromptPackBuildResult> CreateManualPromptPackAsync(
        this DiezProjectDocument document,
        IEnumerable<Guid>? workUnitIds,
        string outputPath)
    {
        var result = await DiezPromptPackFrontendBridge.BuildManualAsync(
            document.ExportProjectJson(),
            document.SourcePath,
            workUnitIds,
            outputPath);
        if (result.Success) ApplyCoreJson(document, result.ProjectJson);
        return result;
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

    public static Task<DiezFileExportResult> ExportPublicationPackageAsync(
        this DiezProjectDocument document,
        string outputPath) =>
        DiezPublicationFrontendBridge.ExportPublicationPackageAsync(document.ExportProjectJson(), outputPath);

    public static Task<DiezFileExportResult> ExportFinalVisualImagesAsync(
        this DiezProjectDocument document,
        string projectPath,
        string outputPath) =>
        DiezPublicationFrontendBridge.ExportFinalVisualImagesAsync(document.ExportProjectJson(), projectPath, outputPath);
}