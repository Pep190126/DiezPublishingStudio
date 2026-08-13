using System.Text.Json;

namespace DiezPublishingStudio;

internal sealed class ColoringCustomHardStyleState
{
    public int SchemaVersion { get; set; } = 1;
    public string Definition { get; set; } = string.Empty;
}

/// <summary>
/// Project-local source of truth for the exact Custom style definition. The legacy ColoringProfile
/// CustomStyleNotes field is still mirrored for compatibility, but renderer/Vision HARD resolution reads
/// this dedicated state first so unrelated legacy UI handlers cannot erase the user's style authority.
/// </summary>
internal static class ColoringCustomHardStyleStore
{
    private const string EntityKind = "DiezColoringCustomHardStyle";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(x => string.Equals(x.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is not null && !string.IsNullOrWhiteSpace(entity.Notes))
        {
            try
            {
                var state = JsonSerializer.Deserialize<ColoringCustomHardStyleState>(entity.Notes, JsonOptions);
                if (!string.IsNullOrWhiteSpace(state?.Definition)) return state.Definition.Trim();
            }
            catch { }
        }

        // Backward compatibility: migrate the former field lazily the first time a Custom project is read.
        var profile = BookTypePromptProfileService.LoadColoring(project);
        return string.Equals(profile.Style, "Custom", StringComparison.OrdinalIgnoreCase)
            ? (profile.CustomStyleNotes ?? string.Empty).Trim()
            : string.Empty;
    }

    public static void Save(PreviewProject project, string? definition)
    {
        var clean = (definition ?? string.Empty).Trim();
        var entity = project.Entities.FirstOrDefault(x => string.Equals(x.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity { Kind = EntityKind, Name = "Stile Custom HARD", IsCandidate = false };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(new ColoringCustomHardStyleState { Definition = clean }, JsonOptions);

        // Mirror into the legacy profile field so old project readers and existing prompt context remain compatible.
        var profile = BookTypePromptProfileService.LoadColoring(project);
        profile.CustomStyleNotes = clean;
        BookTypePromptProfileService.SaveColoring(project, profile);
    }
}
