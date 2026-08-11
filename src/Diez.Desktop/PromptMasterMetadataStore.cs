using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal sealed class PromptMasterMetadata
{
    public int SchemaVersion { get; set; } = 3;
    public string EngineVersion { get; set; } = string.Empty;
    public string CompilerVersion { get; set; } = string.Empty;
    public string ProviderId { get; set; } = PromptEngineeringProviderIds.Generic;
    public string SourceFingerprint { get; set; } = string.Empty;
    public bool ManualOverride { get; set; }
    public string GeneratedBaselinePrompt { get; set; } = string.Empty;
    public string UpdatedAtLocal { get; set; } = string.Empty;
}

/// <summary>
/// Tracks semantic/compiler versions and the exact generated baseline that existed before a manual
/// edit. If structured parameters later change, Diez can preserve only user-added/changed lines
/// instead of appending an entire obsolete generated prompt below the new one.
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
        var envelope = new
        {
            semantic_engine = PromptEngineeringEngine.EngineVersion,
            provider_compiler = PromptEngineeringCompiler.Version,
            request
        };
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
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
        if (metadata is null || metadata.SchemaVersion < 3) return false;
        if (!string.Equals(metadata.EngineVersion, PromptEngineeringEngine.EngineVersion, StringComparison.Ordinal)) return false;
        if (!string.Equals(metadata.CompilerVersion, PromptEngineeringCompiler.Version, StringComparison.Ordinal)) return false;
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
        bool preferAdvancedModel)
    {
        var normalized = PromptPreparationSettingsStore.NormalizeProvider(providerId);
        var baseline = PromptEngineeringCompiler.BuildSeriesPrompt(
            project, count, mustDo, mustNotDo, normalized, preferAdvancedModel);
        Save(project, New(project, count, mustDo, mustNotDo, normalized, preferAdvancedModel, false, baseline));
    }

    public static void MarkManual(
        PreviewProject project,
        int count,
        string? mustDo,
        string? mustNotDo,
        string? providerId,
        bool preferAdvancedModel)
    {
        var normalized = PromptPreparationSettingsStore.NormalizeProvider(providerId);
        var current = Load(project);
        var currentFingerprint = Fingerprint(project, count, mustDo, mustNotDo, normalized, preferAdvancedModel);
        var baseline = current is not null &&
                       string.Equals(current.SourceFingerprint, currentFingerprint, StringComparison.OrdinalIgnoreCase) &&
                       !string.IsNullOrWhiteSpace(current.GeneratedBaselinePrompt)
            ? current.GeneratedBaselinePrompt
            : PromptEngineeringCompiler.BuildSeriesPrompt(project, count, mustDo, mustNotDo, normalized, preferAdvancedModel);
        Save(project, New(project, count, mustDo, mustNotDo, normalized, preferAdvancedModel, true, baseline));
    }

    /// <summary>
    /// Returns lines present in the manual prompt but not in the generated baseline. Changed lines
    /// naturally appear as additions; deleted generated lines are intentionally not returned because
    /// deleting text may not remove current hard Book-Type constraints.
    /// </summary>
    public static string ExtractManualDelta(PromptMasterMetadata? metadata, string? manualPrompt)
    {
        var manual = Lines(manualPrompt).ToList();
        if (manual.Count == 0) return string.Empty;
        var baseline = Lines(metadata?.GeneratedBaselinePrompt).ToHashSet(StringComparer.Ordinal);
        if (baseline.Count == 0) return string.Join(Environment.NewLine, manual);
        var delta = manual.Where(line => !baseline.Contains(line)).ToList();
        return string.Join(Environment.NewLine, delta);
    }

    private static PromptMasterMetadata New(
        PreviewProject project,
        int count,
        string? mustDo,
        string? mustNotDo,
        string providerId,
        bool preferAdvancedModel,
        bool manual,
        string baseline)
    {
        return new PromptMasterMetadata
        {
            SchemaVersion = 3,
            EngineVersion = PromptEngineeringEngine.EngineVersion,
            CompilerVersion = PromptEngineeringCompiler.Version,
            ProviderId = providerId,
            SourceFingerprint = Fingerprint(project, count, mustDo, mustNotDo, providerId, preferAdvancedModel),
            ManualOverride = manual,
            GeneratedBaselinePrompt = baseline ?? string.Empty,
            UpdatedAtLocal = DateTimeOffset.Now.ToString("O")
        };
    }

    private static IEnumerable<string> Lines(string? text) =>
        (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(x => x.TrimEnd())
            .Where(x => x.Length > 0);

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
