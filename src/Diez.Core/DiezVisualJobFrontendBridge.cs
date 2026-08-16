namespace DiezPublishingStudio;

public sealed record DiezVisualJobSyncResult(
    string ProjectJson,
    bool Success,
    int Created,
    int Existing,
    string Message,
    IReadOnlyList<DiezAiFrontendJob> Jobs,
    DiezVisualPromptPack PromptPack);

/// <summary>
/// Transactional frontend boundary from a visual book plan to exactly one Ready IMAGE job per
/// planned atomic prompt. Existing work is never silently overwritten or renumbered.
/// </summary>
public static class DiezVisualJobFrontendBridge
{
    public static DiezVisualJobSyncResult SyncReadyJobs(
        string projectJson,
        string? mustDo = null,
        string? mustNotDo = null,
        string providerId = "generic",
        bool preferAdvancedModel = true)
    {
        var pack = DiezVisualBookFrontendBridge.BuildPromptPack(
            projectJson, mustDo, mustNotDo, providerId, preferAdvancedModel);
        var json = pack.ProjectJson;
        var current = ImageJobs(json);
        var expectedByCode = pack.Items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        var unexpected = current.Where(job => !expectedByCode.ContainsKey(job.Code)).ToList();
        if (unexpected.Count > 0)
            return Failure(json, pack, current,
                "Esistono job Immagine fuori dal piano corrente: " + string.Join(", ", unexpected.Select(x => x.Code)) + ". Nessun job viene rimosso automaticamente.");

        foreach (var job in current)
        {
            var expected = expectedByCode[job.Code];
            if (!string.Equals((job.Prompt ?? string.Empty).Trim(), expected.Prompt.Trim(), StringComparison.Ordinal))
                return Failure(json, pack, current,
                    $"{job.Code} esiste già ma il Prompt del piano corrente è cambiato. Il Core non sovrascrive un job esistente automaticamente.");
        }

        // Existing image jobs must form IMG-001..IMG-K. This keeps stable job/work-unit identities and
        // avoids generating a new IMG-004 just because IMG-002 was manually removed from history.
        var orderedExisting = current.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < orderedExisting.Count; i++)
        {
            var expectedCode = $"IMG-{i + 1:D3}";
            if (!string.Equals(orderedExisting[i].Code, expectedCode, StringComparison.OrdinalIgnoreCase))
                return Failure(json, pack, current,
                    $"La cronologia job Immagine contiene un vuoto prima di {orderedExisting[i].Code}. Nessuna identità viene riciclata o rinumerata automaticamente.");
        }

        var created = 0;
        for (var i = orderedExisting.Count; i < pack.Items.Count; i++)
        {
            var item = pack.Items[i];
            var mutation = DiezAiExchangeBridge.CreateReadyJob(json, item.Title, "Image", item.Prompt);
            if (!string.Equals(mutation.Job.Code, item.Code, StringComparison.OrdinalIgnoreCase))
            {
                // Transactional behavior for the caller: the mutation JSON is not adopted if the Core
                // code allocator cannot preserve the planned IMG-NNN identity.
                return Failure(json, pack, ImageJobs(json),
                    $"Il Core avrebbe assegnato {mutation.Job.Code} invece di {item.Code}; sincronizzazione interrotta prima di applicare il job.");
            }
            json = mutation.ProjectJson;
            created++;
        }

        var finalJobs = ImageJobs(json);
        return new DiezVisualJobSyncResult(
            json,
            true,
            created,
            orderedExisting.Count,
            created == 0
                ? $"I {orderedExisting.Count} job Immagine del piano erano già pronti."
                : $"Creati {created} job Immagine; {orderedExisting.Count} erano già pronti.",
            finalJobs,
            pack);
    }

    private static List<DiezAiFrontendJob> ImageJobs(string json) =>
        DiezAiExchangeBridge.ReadJobs(json)
            .Where(job => string.Equals(job.OutputType, "Image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(job => job.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static DiezVisualJobSyncResult Failure(
        string json,
        DiezVisualPromptPack pack,
        IReadOnlyList<DiezAiFrontendJob> current,
        string message) =>
        new(json, false, 0, current.Count, message, current, pack);
}
