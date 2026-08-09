namespace DiezPublishingStudio;

internal sealed class AiExchangeMockApiAdapter
{
    public int Attempts { get; private set; }

    public async Task<AiExchangeIngestResult?> RunAttemptAsync(
        PreviewProject project,
        AiExchangeState state,
        AiExchangeRequestSnapshot snapshot,
        Guid workUnitId,
        string outcome,
        string? primaryAssetPath = null,
        string? textContent = null,
        string? description = null)
    {
        Attempts++;
        if (string.Equals(outcome, "TIMEOUT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(outcome, "RATE_LIMIT", StringComparison.OrdinalIgnoreCase))
            return null;

        var request = snapshot.Items.FirstOrDefault(i => i.WorkUnitId == workUnitId)
            ?? throw new InvalidOperationException("Mock API: Work Unit non presente nello snapshot.");
        var unit = state.WorkUnits.First(w => w.WorkUnitId == workUnitId);

        return await AiExchangeResultIngestor.IngestAsync(project, state, new AiExchangeNormalizedResultItem
        {
            WorkUnitId = workUnitId,
            CandidateVersion = request.TargetCandidateVersion,
            ContentType = unit.ContentType,
            ResultStatus = string.Equals(outcome, "PARTIAL", StringComparison.OrdinalIgnoreCase) ? "INCOMPLETE" : "COMPLETE",
            PrimaryAssetPath = primaryAssetPath,
            TextContent = textContent ?? string.Empty,
            Description = description ?? string.Empty,
            Origin = AiExchangeOrigins.AiApi,
            SourceSnapshotId = snapshot.SnapshotId
        });
    }
}
