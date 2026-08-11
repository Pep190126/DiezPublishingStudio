using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal sealed class PromptMasterMetadata
{
    public int SchemaVersion { get; set; } = 1;
    public string EngineVersion { get; set; } = string.Empty;
    public string ProviderId { get; set; } = PromptEngineeringProviderIds.Generic;
    public string SourceFingerprint { get; set; } = string.Empty;
    public bool ManualOverride { get; set; }
    public string UpdatedAtLocal { get; set; } = string.Empty;
}

/// <summary>
/// Distinguishes an actual user-edited master prompt from legacy/generated text and detects when
/// GUI parameters changed after prompt compilation. This lets Diez preserve manual edits without
/// accidentally treating an obsolete legacy prompt as authoritative.
/// </summary>
internal static class PromptMasterMetadataStore
{
    private const string EntityKind = "DiezPromptMasterMetadata";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static PromptMasterMetadata? Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return null;
        try { return JsonSerializer.Deserialize<PromptMasterMetadata>(entity.Notes, JsonOptions); }
        catch { return null; }
    }

    public static string Fingerprint(
        PreviewProject project,
        int count,
        string? mustDo,
        string? mustNotDo,
        string? providerId,
        bool preferAdvancedModel)
    {
        var request = PromptEngineeringEngine.BuildRequest(
            project, count, mustDo, mustNotDo, providerId, preferAdvancedModel);
        var json = JsonSerializer.Serialize(request, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public static bool MatchesCurrent(
        PreviewProject project,
        PromptMasterMetadata? metadata,
        int count,
        string? mustDo,
        string? mustNotDo,
        string? providerId,
        bool preferAdvancedModel)
    {
        if (metadata is null) return false;
        if (!string.Equals(metadata.EngineVersion, PromptEngineeringEngine.EngineVersion, StringComparison.Ordinal)) return false;
        var normalized = PromptPreparationSettingsStore.NormalizeProvider(providerId);
        if (!string.Equals(metadata.ProviderId, normalized, StringComparison.OrdinalIgnoreCase)) return false;
        var fingerprint = Fingerprint(project, count, mustDo, mustNotDo, normalized, preferAdvancedModel);
        return string.Equals(metadata.SourceFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase);
    }

    public static void MarkGenerated(
        PreviewProject project,
        int count,
        string? mustDo,
        string? mustNotDo,
        string? providerId,
        bool preferAdvancedModel) =>
        Save(project, New(project, count, mustDo, mustNotDo, providerId, preferAdvancedModel, manual: false));

    public static void MarkManual(
        PreviewProject project,
        int count,
        string? mustDo,
        string? mustNotDo,
        string? providerId,
        bool preferAdvancedModel) =>
        Save(project, New(project, count, mustDo, mustNotDo, providerId, preferAdvancedModel, manual: true));

    private static PromptMasterMetadata New(
        PreviewProject project,
        int count,
        string? mustDo,
        string? mustNotDo,
        string? providerId,
        bool preferAdvancedModel,
        bool manual)
    {
        var normalized = PromptPreparationSettingsStore.NormalizeProvider(providerId);
        return new PromptMasterMetadata
        {
            EngineVersion = PromptEngineeringEngine.EngineVersion,
            ProviderId = normalized,
            SourceFingerprint = Fingerprint(project, count, mustDo, mustNotDo, normalized, preferAdvancedModel),
            ManualOverride = manual,
            UpdatedAtLocal = DateTimeOffset.Now.ToString("O")
        };
    }

    private static void Save(PreviewProject project, PromptMasterMetadata metadata)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity { Kind = EntityKind, Name = "Metadati master prompt", IsCandidate = false };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(metadata, JsonOptions);
    }
}
