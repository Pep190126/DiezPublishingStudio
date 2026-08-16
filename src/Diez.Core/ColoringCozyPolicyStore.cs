using System.Text.Json;

namespace DiezPublishingStudio;

/// <summary>
/// Independent persisted Cozy production policy. Cozy is not a visual style entry:
/// it is an orthogonal editorial mood/profile and OFF is as authoritative as ON.
/// </summary>
internal static class ColoringCozyPolicyStore
{
    private const string EntityKind = "DiezColoringCozyPolicy";
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

    public static bool Resolve(PreviewProject project, bool legacyStyleWasCozy = false)
    {
        return TryLoad(project, out var enabled) ? enabled : legacyStyleWasCozy;
    }

    public static void Save(PreviewProject project, bool enabled)
    {
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = EntityKind,
                Name = "Cozy HARD policy",
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
