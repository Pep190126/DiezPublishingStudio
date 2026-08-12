using System.Text.Json;

namespace DiezPublishingStudio;

/// <summary>
/// Persists provider-declared FAILED response items even when no Candidate/material exists.
/// A FAILED renderer attempt is a real audited result and must remain visible in Review instead of
/// looking like a missing/unimported response. The sidecar entity is retained for backward compatibility,
/// while the authoritative current audit is also mirrored into AiExchangeVersion so normal AI-state reloads
/// cannot make a FAILED attempt disappear from the UI.
/// </summary>
internal static class AiExchangeResponseFailureStore
{
    private const string EntityKind = "DiezAiExchangeResponseFailures";
    private const string VersionMarker = "DIEZ_PROVIDER_FAILED_V1:";
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

    /// <summary>
    /// Resolve the newest FAILED audit from both the legacy sidecar and the central AI version state.
    /// Central versions are authoritative for the current application session; sidecar records keep older
    /// projects and already-imported packages readable during migration.
    /// </summary>
    public static Record? Latest(PreviewProject project, AiExchangeState exchange, Guid workUnitId)
    {
        var sidecar = Latest(project, workUnitId);
        var central = exchange.Versions
            .Where(v => v.WorkUnitId == workUnitId)
            .Select(TryReadVersion)
            .Where(r => r is not null)
            .Select(r => r!)
            .OrderByDescending(r => r.CandidateVersion)
            .ThenByDescending(r => r.ImportedAtLocal, StringComparer.Ordinal)
            .FirstOrDefault();
        if (sidecar is null) return central;
        if (central is null) return sidecar;
        if (central.CandidateVersion != sidecar.CandidateVersion)
            return central.CandidateVersion > sidecar.CandidateVersion ? central : sidecar;
        return string.CompareOrdinal(central.ImportedAtLocal, sidecar.ImportedAtLocal) >= 0 ? central : sidecar;
    }

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

        Fill(record, description, failureReason, renderRequestId, renderPromptSha256);
        Save(project, state);
    }

    /// <summary>
    /// Mirror a provider FAILED result into the normal AiExchange version stream. This is not an approvable
    /// Candidate: it has Status=INCOMPLETE, no MaterialId and NEEDS_VERIFICATION. It is nevertheless a real
    /// attempted version, so its audit survives every normal AiExchangeStateStore.Load/Save cycle.
    /// </summary>
    public static bool RecordFailureVersion(
        AiExchangeState exchange,
        AiExchangeWorkUnit unit,
        string packageId,
        Guid promptPackId,
        int candidateVersion,
        string? description,
        string? failureReason,
        string? renderRequestId,
        string? renderPromptSha256,
        Guid? sourceSnapshotId)
    {
        var version = exchange.Versions.FirstOrDefault(v =>
            v.WorkUnitId == unit.WorkUnitId && v.VersionNumber == candidateVersion);
        if (version?.MaterialId.HasValue == true)
            return false;

        version ??= new AiExchangeVersion
        {
            VersionId = Guid.NewGuid(),
            WorkUnitId = unit.WorkUnitId,
            VersionNumber = candidateVersion,
            Origin = AiExchangeOrigins.AiPromptPack,
            CreatedAtLocal = DateTimeOffset.Now.ToString("O")
        };
        if (!exchange.Versions.Contains(version)) exchange.Versions.Add(version);

        var record = new Record
        {
            PackageId = packageId,
            PromptPackId = promptPackId,
            WorkUnitId = unit.WorkUnitId,
            CandidateVersion = candidateVersion
        };
        Fill(record, description, failureReason, renderRequestId, renderPromptSha256);

        version.Status = AiExchangeVersionStatuses.Incomplete;
        version.Origin = AiExchangeOrigins.AiPromptPack;
        version.MaterialId = null;
        version.TextContent = VersionMarker + JsonSerializer.Serialize(record, JsonOptions);
        version.Description = record.Description;
        version.DescriptionStatus = AiExchangeDescriptionStatuses.NeedsVerification;
        version.ContentSha256 = string.Empty;
        version.SourceSnapshotId = sourceSnapshotId;
        if (string.IsNullOrWhiteSpace(version.CreatedAtLocal)) version.CreatedAtLocal = record.ImportedAtLocal;
        if (!unit.CandidateVersionIds.Contains(version.VersionId)) unit.CandidateVersionIds.Add(version.VersionId);
        return true;
    }

    public static bool IsFailureVersion(AiExchangeVersion? version) =>
        version is not null &&
        !version.MaterialId.HasValue &&
        (version.TextContent ?? string.Empty).StartsWith(VersionMarker, StringComparison.Ordinal);

    public static Record? TryReadVersion(AiExchangeVersion version)
    {
        if (!IsFailureVersion(version)) return null;
        var json = (version.TextContent ?? string.Empty)[VersionMarker.Length..];
        try
        {
            var record = JsonSerializer.Deserialize<Record>(json, JsonOptions);
            if (record is null) return null;
            record.WorkUnitId = version.WorkUnitId;
            record.CandidateVersion = version.VersionNumber;
            return record;
        }
        catch { return null; }
    }

    public static void ClearFailureMarker(AiExchangeVersion version)
    {
        if (IsFailureVersion(version)) version.TextContent = string.Empty;
    }

    /// <summary>
    /// A later real asset for the same Work Unit supersedes sidecar FAILED attempts at the same or older
    /// candidate version. Central historical FAILED versions remain in the version stream, while the actual
    /// version receiving the real asset has its FAILED marker cleared by ClearFailureMarker.
    /// </summary>
    public static void ClearSupersededByAsset(PreviewProject project, Guid workUnitId, int candidateVersion)
    {
        var state = Load(project);
        var removed = state.Records.RemoveAll(r =>
            r.WorkUnitId == workUnitId && r.CandidateVersion <= candidateVersion);
        if (removed > 0) Save(project, state);
    }

    private static void Fill(
        Record record,
        string? description,
        string? failureReason,
        string? renderRequestId,
        string? renderPromptSha256)
    {
        record.Status = "FAILED";
        record.Description = (description ?? string.Empty).Trim();
        record.FailureReason = (failureReason ?? string.Empty).Trim();
        record.RenderRequestId = (renderRequestId ?? string.Empty).Trim();
        record.RenderPromptSha256 = (renderPromptSha256 ?? string.Empty).Trim().ToLowerInvariant();
        record.ImportedAtLocal = DateTimeOffset.Now.ToString("O");
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
