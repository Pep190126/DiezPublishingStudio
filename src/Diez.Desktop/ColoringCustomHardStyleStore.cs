using System.Text.Json;

namespace DiezPublishingStudio;

internal sealed class ColoringCustomHardStyleState
{
    public int SchemaVersion { get; set; } = 2;
    public bool IsActive { get; set; }
    public string Definition { get; set; } = string.Empty;
}

/// <summary>
/// Project-local source of truth for the exact Custom style definition AND whether Custom is the active
/// style authority. The legacy ColoringProfile fields are mirrored for compatibility only; renderer/Vision
/// HARD resolution does not rely on legacy UI handlers preserving Style=Custom.
/// </summary>
internal static class ColoringCustomHardStyleStore
{
    private const string EntityKind = "DiezColoringCustomHardStyle";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ColoringCustomHardStyleState LoadState(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(x => string.Equals(x.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is not null && !string.IsNullOrWhiteSpace(entity.Notes))
        {
            try
            {
                var state = JsonSerializer.Deserialize<ColoringCustomHardStyleState>(entity.Notes, JsonOptions);
                if (state is not null)
                {
                    state.Definition = (state.Definition ?? string.Empty).Trim();
                    return state;
                }
            }
            catch { }
        }

        // Backward compatibility: a legacy project with Style=Custom is considered actively Custom.
        var profile = BookTypePromptProfileService.LoadColoring(project);
        var legacyDefinition = string.Equals(profile.Style, "Custom", StringComparison.OrdinalIgnoreCase)
            ? (profile.CustomStyleNotes ?? string.Empty).Trim()
            : string.Empty;
        return new ColoringCustomHardStyleState
        {
            IsActive = string.Equals(profile.Style, "Custom", StringComparison.OrdinalIgnoreCase),
            Definition = legacyDefinition
        };
    }

    public static string Load(PreviewProject project) => LoadState(project).Definition;

    public static bool IsActive(PreviewProject project) =>
        LoadState(project) is { IsActive: true, Definition.Length: > 0 };

    public static void Activate(PreviewProject project, string? definition)
    {
        var clean = (definition ?? string.Empty).Trim();
        Write(project, new ColoringCustomHardStyleState { IsActive = true, Definition = clean });

        // Compatibility mirror. Even if another legacy handler later changes Style, the dedicated IsActive
        // flag above remains authoritative until the user explicitly selects a non-Custom style.
        var profile = BookTypePromptProfileService.LoadColoring(project);
        profile.Style = "Custom";
        profile.CustomStyleNotes = clean;
        BookTypePromptProfileService.SaveColoring(project, profile);
    }

    public static void Deactivate(PreviewProject project)
    {
        var state = LoadState(project);
        state.IsActive = false;
        Write(project, state);
    }

    public static void Save(PreviewProject project, string? definition) => Activate(project, definition);

    private static void Write(PreviewProject project, ColoringCustomHardStyleState state)
    {
        state.SchemaVersion = 2;
        state.Definition = (state.Definition ?? string.Empty).Trim();
        var entity = project.Entities.FirstOrDefault(x => string.Equals(x.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity { Kind = EntityKind, Name = "Stile Custom HARD", IsCandidate = false };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(state, JsonOptions);
    }
}
