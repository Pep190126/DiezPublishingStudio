using System.Text.Json;

namespace DiezPublishingStudio;

/// <summary>
/// Independent persisted policy for the Bold & Easy production profile.
/// Visual Style and Bold & Easy are separate editorial dimensions. OFF is as authoritative as ON.
/// Thin/fine line weights always force OFF before prompt generation.
/// </summary>
internal static class ColoringBoldEasyPolicyStore
{
    private const string EntityKind = "DiezColoringBoldEasyPolicy";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class State
    {
        public int SchemaVersion { get; set; } = 1;
        public bool Enabled { get; set; }
        public string UpdatedAtLocal { get; set; } = string.Empty;
    }

    public static bool TryLoad(PreviewProject project, out bool enabled)
    {
        enabled = false;
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return false;
        try
        {
            var state = JsonSerializer.Deserialize<State>(entity.Notes, JsonOptions);
            if (state is null) return false;
            enabled = state.Enabled;
            return true;
        }
        catch { return false; }
    }

    public static bool Resolve(PreviewProject project, string? lineWeight, bool profileFallback)
    {
        if (BookTypePromptProfileService.IsThinLineWeight(lineWeight)) return false;
        return TryLoad(project, out var enabled) ? enabled : profileFallback;
    }

    public static void Save(PreviewProject project, bool enabled, string? lineWeight = null)
    {
        if (BookTypePromptProfileService.IsThinLineWeight(lineWeight)) enabled = false;
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = EntityKind,
                Name = "Bold & Easy HARD policy",
                IsCandidate = false
            };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(new State
        {
            Enabled = enabled,
            UpdatedAtLocal = DateTimeOffset.Now.ToString("O")
        }, JsonOptions);
    }
}
