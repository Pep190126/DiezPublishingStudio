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
        DiezVisualPromptPack pack)
    {
        var current = DiezAiExchangeBridge.ReadJobs(document.ExportProjectJson())
            .Where(j => string.Equals(j.OutputType, "Image", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var expectedCodes = pack.Items.Select(i => i.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unexpected = current.Where(j => !expectedCodes.Contains(j.Code)).ToList();
        if (unexpected.Count > 0)
        {
            return new VisualJobSyncResult(
                false, 0, 0,
                "Il piano immagini è cambiato ma esistono job immagine fuori dal piano corrente. Nessun job è stato modificato: rivedi il piano o la cronologia AI.",
                current);
        }

        var created = 0;
        var existing = 0;
        foreach (var item in pack.Items.OrderBy(i => i.Position))
        {
            current = DiezAiExchangeBridge.ReadJobs(document.ExportProjectJson())
                .Where(j => string.Equals(j.OutputType, "Image", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var already = current.FirstOrDefault(j => string.Equals(j.Code, item.Code, StringComparison.OrdinalIgnoreCase));
            if (already is not null)
            {
                if (!string.Equals(already.Prompt.Trim(), item.Prompt.Trim(), StringComparison.Ordinal))
                {
                    return new VisualJobSyncResult(
                        false, created, existing,
                        $"{item.Code} esiste già ma il Prompt del piano corrente è cambiato. Nessun job esistente viene sovrascritto automaticamente.",
                        current);
                }
                existing++;
                continue;
            }

            var mutation = DiezAiExchangeBridge.CreateReadyJob(
                document.ExportProjectJson(), item.Title, "Image", item.Prompt);
            if (!string.Equals(mutation.Job.Code, item.Code, StringComparison.OrdinalIgnoreCase))
            {
                return new VisualJobSyncResult(
                    false, created, existing,
                    $"Il Core avrebbe assegnato {mutation.Job.Code} invece di {item.Code}; sincronizzazione annullata prima di applicare il job.",
                    current);
            }
            ApplyCoreJson(document, mutation.ProjectJson);
            created++;
        }

        var finalJobs = DiezAiExchangeBridge.ReadJobs(document.ExportProjectJson())
            .Where(j => string.Equals(j.OutputType, "Image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(j => j.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new VisualJobSyncResult(
            true,
            created,
            existing,
            created == 0
                ? $"I {existing} job immagine del piano erano già pronti."
                : $"Creati {created} job immagine; {existing} erano già pronti.",
            finalJobs);
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
