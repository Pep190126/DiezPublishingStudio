namespace DiezPublishingStudio;

internal sealed class AiExchangeApiCapabilities
{
    public bool TextGeneration { get; init; }
    public bool ImageGeneration { get; init; }
    public bool ImageEdit { get; init; }
    public bool MultiImageReference { get; init; }
    public bool StructuredOutput { get; init; }
    public bool FileInput { get; init; }
    public bool Vision { get; init; }
}

internal interface IAiExchangeApiAdapter
{
    string ProviderId { get; }
    AiExchangeApiCapabilities Capabilities { get; }
    Task<AiExchangeIngestResult?> RunAttemptAsync(
        PreviewProject project,
        AiExchangeState state,
        AiExchangeRequestSnapshot snapshot,
        Guid workUnitId,
        string outcome,
        string? primaryAssetPath = null,
        string? textContent = null,
        string? description = null);
}

internal sealed class AiExchangeMockApiAdapter : IAiExchangeApiAdapter
{
    public string ProviderId => "mock";
    public AiExchangeApiCapabilities Capabilities { get; } = new()
    {
        TextGeneration = true,
        ImageGeneration = true,
        ImageEdit = true,
        MultiImageReference = true,
        StructuredOutput = true,
        FileInput = true,
        Vision = true
    };

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

        if (string.Equals(outcome, "INVALID_RESPONSE", StringComparison.OrdinalIgnoreCase))
            return new AiExchangeIngestResult(
                "INVALID",
                workUnitId,
                request.TargetCandidateVersion,
                null,
                "Mock API: risposta provider non normalizzabile.");

        if (string.Equals(outcome, "MISSING_ASSET", StringComparison.OrdinalIgnoreCase))
            primaryAssetPath = null;
        if (string.Equals(outcome, "MISSING_DESCRIPTION", StringComparison.OrdinalIgnoreCase))
            description = string.Empty;

        var resultStatus = string.Equals(outcome, "PARTIAL", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(outcome, "MISSING_ASSET", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(outcome, "MISSING_DESCRIPTION", StringComparison.OrdinalIgnoreCase)
            ? "INCOMPLETE"
            : "COMPLETE";

        return await AiExchangeResultIngestor.IngestAsync(project, state, new AiExchangeNormalizedResultItem
        {
            WorkUnitId = workUnitId,
            CandidateVersion = request.TargetCandidateVersion,
            ContentType = unit.ContentType,
            ResultStatus = resultStatus,
            PrimaryAssetPath = primaryAssetPath,
            TextContent = textContent ?? string.Empty,
            Description = description ?? string.Empty,
            Origin = AiExchangeOrigins.AiApi,
            SourceSnapshotId = snapshot.SnapshotId
        });
    }
}
