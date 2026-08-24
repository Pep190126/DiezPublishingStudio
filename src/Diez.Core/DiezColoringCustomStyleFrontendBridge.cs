using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

public sealed record DiezColoringCustomStyleStateDto(
    bool IsActive,
    string Definition,
    bool ArchiveInLibrary,
    IReadOnlyList<string> LibraryLabels);

public sealed record DiezColoringCustomStyleMutation(
    string ProjectJson,
    string Status,
    string Message,
    DiezColoringCustomStyleStateDto State);

/// <summary>
/// UI-neutral boundary for the user-authored Custom coloring style. The definition always belongs
/// to the current project; copying it into the local reusable library happens only after explicit opt-in.
/// </summary>
public static class DiezColoringCustomStyleFrontendBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static DiezColoringCustomStyleStateDto Read(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        return State(project);
    }

    public static string ResolveLibraryDefinition(string? label)
    {
        return CustomStyleLibraryService.TryResolve(label, out var definition) ? definition : string.Empty;
    }

    public static DiezColoringCustomStyleMutation Save(
        string projectJson,
        bool active,
        string? definition,
        bool archiveInLibrary)
    {
        var (root, project) = Parse(projectJson);
        if (!active)
        {
            ColoringCustomHardStyleStore.Deactivate(project);
            MergeEntities(root, project);
            return new DiezColoringCustomStyleMutation(
                Write(root), "SAVED", "Stile Custom non attivo per questo progetto.", State(project));
        }

        var clean = (definition ?? string.Empty).Trim();
        if (clean.Length == 0)
            return new DiezColoringCustomStyleMutation(
                projectJson, "INVALID", "Scrivi lo stile Custom prima di salvarlo.", State(project));

        ColoringCustomHardStyleStore.Activate(project, clean, archiveInLibrary);
        if (archiveInLibrary) CustomStyleLibraryService.Add(clean);
        MergeEntities(root, project);
        return new DiezColoringCustomStyleMutation(
            Write(root),
            "SAVED",
            archiveInLibrary
                ? "Stile Custom salvato nel progetto e aggiunto alla libreria degli stili."
                : "Stile Custom salvato solo in questo progetto.",
            State(project));
    }

    private static DiezColoringCustomStyleStateDto State(PreviewProject project)
    {
        var state = ColoringCustomHardStyleStore.LoadState(project);
        return new DiezColoringCustomStyleStateDto(
            state.IsActive,
            state.Definition ?? string.Empty,
            state.ArchiveInLibrary,
            CustomStyleLibraryService.SelectableLabels());
    }

    private static (JsonObject Root, PreviewProject Project) Parse(string projectJson)
    {
        var root = JsonNode.Parse(projectJson) as JsonObject
            ?? throw new InvalidDataException("Il JSON del progetto Diez non è valido.");
        var project = JsonSerializer.Deserialize<PreviewProject>(projectJson, JsonOptions)
            ?? throw new InvalidDataException("Il progetto Diez non può essere letto dal Core.");
        project.Entities ??= [];
        project.AiProductionJobs ??= [];
        project.Materials ??= [];
        project.ContentNodes ??= [];
        project.IllustrationPlacements ??= [];
        project.Relations ??= [];
        project.BibleEntries ??= [];
        project.ConsistencyFacts ??= [];
        project.ConsistencyIssues ??= [];
        project.ConsistencyResolutions ??= [];
        project.RevisionCandidates ??= [];
        return (root, project);
    }

    private static void MergeEntities(JsonObject root, PreviewProject project)
    {
        var raw = root["Entities"] as JsonArray ?? new JsonArray();
        root["Entities"] = raw;
        foreach (var entity in project.Entities)
        {
            if (JsonSerializer.SerializeToNode(entity, JsonOptions) is not JsonObject typed) continue;
            var id = Scalar(typed["EntityId"]);
            if (string.IsNullOrWhiteSpace(id)) continue;
            var existing = raw.OfType<JsonObject>().FirstOrDefault(x =>
                string.Equals(Scalar(x["EntityId"]), id, StringComparison.OrdinalIgnoreCase));
            if (existing is null) raw.Add(typed);
            else foreach (var pair in typed) existing[pair.Key] = pair.Value?.DeepClone();
        }
    }

    private static string Scalar(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        return node?.ToJsonString().Trim('"') ?? string.Empty;
    }

    private static string Write(JsonObject root) => root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
