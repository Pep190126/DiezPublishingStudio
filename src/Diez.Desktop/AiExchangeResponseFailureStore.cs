using System.Text.Json;

namespace DiezPublishingStudio;

/// <summary>
/// Persists provider-declared FAILED response items even when no Candidate/material exists.
/// A FAILED renderer attempt is a real audited result and must remain visible in Review instead of
/// looking like a missing/unimported response.
/// </summary>
internal static class AiExchangeResponseFailureStore
{
    private const string EntityKind = "DiezAiExchangeResponseFailures";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    internal sealed class State
    {
        public int SchemaVersion { get; set; } = 1;
        public List<Record> Records { get; set; } = [];
    }

    internal sealed class Record
    {
        public string PackageId { get; set; } = string.Empty;
        public Guid PromptPackId { get; set; }
        public Guid WorkUnitId { get; set; }
        public int CandidateVersion { get; set; }
        public string Status { get; set; } = "FAILED";
        public string Description { get; set; } = string.Empty;
        public string FailureReason { get; set; } = string.Empty;
        public string RenderRequestId { get; set; } = string.Empty;
        public string RenderPromptSha256 { get; set; } = string.Empty;
        public string ImportedAtLocal { get; set; } = string.Empty;
    }

    public static State Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return new State();
        try
        {
            var state = JsonSerializer.Deserialize<State>(entity.Notes, JsonOptions) ?? new State();
            state.Records ??= [];
            return state;
        }
        catch { return new State(); }
    }

    public static Record? Latest(PreviewProject project, Guid workUnitId) =>
        Load(project).Records
            .Where(r => r.WorkUnitId == workUnitId)
            .OrderByDescending(r => r.CandidateVersion)
            .ThenByDescending(r => r.ImportedAtLocal, StringComparer.Ordinal)
            .FirstOrDefault();

    public static void RecordFailure(
        PreviewProject project,
        string packageId,
        Guid promptPackId,
        Guid workUnitId,
        int candidateVersion,
        string? description,
        string? failureReason,
        string? renderRequestId,
        string? renderPromptSha256)
    {
        var state = Load(project);
        var record = state.Records.FirstOrDefault(r =>
            r.WorkUnitId == workUnitId &&
            r.CandidateVersion == candidateVersion &&
            string.Equals(r.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        if (record is null)
        {
            record = new Record
            {
                PackageId = packageId,
                PromptPackId = promptPackId,
                WorkUnitId = workUnitId,
                CandidateVersion = candidateVersion
            };
            state.Records.Add(record);
        }

        record.Status = "FAILED";
        record.Description = (description ?? string.Empty).Trim();
        record.FailureReason = (failureReason ?? string.Empty).Trim();
        record.RenderRequestId = (renderRequestId ?? string.Empty).Trim();
        record.RenderPromptSha256 = (renderPromptSha256 ?? string.Empty).Trim().ToLowerInvariant();
        record.ImportedAtLocal = DateTimeOffset.Now.ToString("O");
        Save(project, state);
    }

    private static void Save(PreviewProject project, State state)
    {
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = EntityKind,
                Name = "Esiti FAILED Response AI",
                IsCandidate = false
            };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(state, JsonOptions);
    }
}
