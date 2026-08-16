using System.Text.Json;

namespace DiezPublishingStudio;

/// <summary>
/// Preserves the series-level environment while the native Environment editor is temporarily reused
/// to edit one structured scene. This keeps generic context and scene-local intent semantically separate.
/// </summary>
internal static class StructuredSceneEnvironmentStore
{
    private const string EntityKind = "DiezStructuredSceneEnvironment";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class State
    {
        public int SchemaVersion { get; set; } = 1;
        public string GenericEnvironment { get; set; } = string.Empty;
    }

    public static string Load(PreviewProject project, string fallback)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return fallback ?? string.Empty;
        try
        {
            var state = JsonSerializer.Deserialize<State>(entity.Notes, JsonOptions);
            return state?.GenericEnvironment ?? fallback ?? string.Empty;
        }
        catch
        {
            return fallback ?? string.Empty;
        }
    }

    public static void Save(PreviewProject project, string? genericEnvironment)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = EntityKind,
                Name = "Ambientazione generale per scene strutturate",
                IsCandidate = false
            };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(new State
        {
            GenericEnvironment = genericEnvironment ?? string.Empty
        }, JsonOptions);
    }
}
