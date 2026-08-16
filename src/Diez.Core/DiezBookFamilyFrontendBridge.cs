using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

public sealed record DiezBookOptionValue(
    string Key,
    string Label,
    BookTypeAiOptionKind Kind,
    string Value,
    string DefaultValue,
    IReadOnlyList<string> Choices,
    string Help);

public sealed record DiezBookFamilyState(
    string BookType,
    IReadOnlyList<DiezBookOptionValue> Options,
    string Notes,
    string LegacyNotesDraft);

public sealed record DiezBookFamilyMutation(
    string ProjectJson,
    string Status,
    string Message,
    DiezBookFamilyState State);

/// <summary>
/// Canonical frontend boundary for the neutral book-family surface (Quiz, Data Collection, Other)
/// and for shared book options in any family. Values are persisted as Core DiezAiOption entities;
/// UnoUiState is read only as a legacy recovery source and is never written by this bridge.
/// </summary>
public static class DiezBookFamilyFrontendBridge
{
    private const string NotesKind = "DiezBookFamilyNotes";

    public static DiezBookFamilyState Read(string projectJson, string? requestedBookType = null)
    {
        var (root, project) = Parse(projectJson);
        var type = ResolveType(project, requestedBookType);
        return State(root, project, type);
    }

    public static DiezBookFamilyMutation Save(
        string projectJson,
        string? requestedBookType,
        IReadOnlyDictionary<string, string?> values,
        string? notes)
    {
        var (root, project) = Parse(projectJson);
        var type = ResolveType(project, requestedBookType);
        if (string.IsNullOrWhiteSpace(type))
            return new DiezBookFamilyMutation(Write(root), "INVALID", "Scegli prima il Tipo libro.", State(root, project, type));

        if (!string.Equals(BookTypeProfileService.Get(project), type, StringComparison.OrdinalIgnoreCase))
            BookTypeProfileService.Set(project, type);

        foreach (var definition in BookTypeAiOptionsCoreService.Definitions(project))
        {
            if (!values.TryGetValue(definition.Key, out var value)) continue;
            BookTypeAiOptionsCoreService.Set(project, definition, value);
        }
        SetNotes(project, type, notes);
        MergeProject(root, project);
        var state = State(root, project, type);
        return new DiezBookFamilyMutation(Write(root), "SAVED", $"Impostazioni {Friendly(type)} salvate nel Core.", state);
    }

    private static DiezBookFamilyState State(JsonObject root, PreviewProject project, string type)
    {
        if (!string.IsNullOrWhiteSpace(type) && !string.Equals(BookTypeProfileService.Get(project), type, StringComparison.OrdinalIgnoreCase))
            BookTypeProfileService.Set(project, type);

        var options = string.IsNullOrWhiteSpace(type)
            ? new List<DiezBookOptionValue>()
            : BookTypeAiOptionsCoreService.Definitions(project)
                .Select(definition => new DiezBookOptionValue(
                    definition.Key,
                    definition.Label,
                    definition.Kind,
                    BookTypeAiOptionsCoreService.Get(project, definition),
                    definition.DefaultValue,
                    definition.Choices?.ToList() ?? [],
                    definition.Help))
                .ToList();
        var legacy = root["UnoUiState"] as JsonObject;
        return new DiezBookFamilyState(
            type,
            options,
            GetNotes(project, type),
            LegacyNotes(legacy, type));
    }

    private static string ResolveType(PreviewProject project, string? requested)
    {
        var explicitType = BookTypeCatalog.Normalize(requested);
        if (!string.IsNullOrWhiteSpace(explicitType)) return explicitType;
        return BookTypeProfileService.Get(project);
    }

    private static string GetNotes(PreviewProject project, string type) =>
        project.Entities.FirstOrDefault(entity =>
            string.Equals(entity.Kind, NotesKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entity.Name, type, StringComparison.OrdinalIgnoreCase))?.Notes ?? string.Empty;

    private static void SetNotes(PreviewProject project, string type, string? notes)
    {
        var matches = project.Entities.Where(entity =>
            string.Equals(entity.Kind, NotesKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entity.Name, type, StringComparison.OrdinalIgnoreCase)).ToList();
        var entity = matches.FirstOrDefault();
        if (entity is null)
        {
            entity = new GraphEntity
            {
                EntityId = Guid.NewGuid(),
                Kind = NotesKind,
                Name = type,
                IsCandidate = false,
                Notes = notes?.Trim() ?? string.Empty
            };
            project.Entities.Add(entity);
        }
        else entity.Notes = notes?.Trim() ?? string.Empty;
        foreach (var duplicate in matches.Skip(1)) project.Entities.Remove(duplicate);
    }

    private static string LegacyNotes(JsonObject? legacy, string type)
    {
        if (legacy is null || string.IsNullOrWhiteSpace(type)) return string.Empty;
        var key = $"BookOptions.{type}.Notes";
        if (legacy[key] is JsonValue value && value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        return string.Empty;
    }

    private static string Friendly(string type) => type switch
    {
        BookTypeCatalog.Quiz => "Quiz / trivia",
        BookTypeCatalog.DataCollection => "Catalogo / raccolta dati",
        BookTypeCatalog.Other => "Altro tipo di libro",
        _ => type
    };

    private static (JsonObject Root, PreviewProject Project) Parse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("Il JSON del progetto Diez non è valido.");
        var project = JsonSerializer.Deserialize<PreviewProject>(json, JsonOptions)
            ?? throw new InvalidDataException("Il progetto Diez non può essere letto dal Core.");
        project.EditionMetadata ??= new EditionMetadata();
        project.AiProduction ??= new AiProductionSettings();
        project.AiProductionJobs ??= [];
        project.Materials ??= [];
        project.ContentNodes ??= [];
        project.IllustrationPlacements ??= [];
        project.Entities ??= [];
        project.Relations ??= [];
        project.BibleEntries ??= [];
        project.ConsistencyFacts ??= [];
        project.ConsistencyIssues ??= [];
        project.ConsistencyResolutions ??= [];
        project.RevisionCandidates ??= [];
        return (root, project);
    }

    private static void MergeProject(JsonObject root, PreviewProject project)
    {
        var raw = root["Entities"] as JsonArray ?? new JsonArray();
        root["Entities"] = raw;
        var ids = new HashSet<string>(project.Entities.Select(entity => entity.EntityId.ToString()), StringComparer.OrdinalIgnoreCase);
        foreach (var entity in project.Entities)
        {
            if (JsonSerializer.SerializeToNode(entity, JsonOptions) is not JsonObject typed) continue;
            var id = entity.EntityId.ToString();
            var existing = raw.OfType<JsonObject>().FirstOrDefault(obj =>
                string.Equals(Scalar(obj["EntityId"]), id, StringComparison.OrdinalIgnoreCase));
            if (existing is null) raw.Add(typed);
            else foreach (var pair in typed) existing[pair.Key] = pair.Value?.DeepClone();
        }
        for (var i = raw.Count - 1; i >= 0; i--)
        {
            if (raw[i] is not JsonObject obj) continue;
            var id = Scalar(obj["EntityId"]);
            if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id)) raw.RemoveAt(i);
        }
    }

    private static string Scalar(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        return node?.ToJsonString().Trim('"') ?? string.Empty;
    }

    private static string Write(JsonObject root) => root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}
