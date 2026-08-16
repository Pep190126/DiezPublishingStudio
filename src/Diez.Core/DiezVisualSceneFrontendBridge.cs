using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

public sealed record DiezVisualConsistencyRuleDto(
    string Key,
    string Label,
    string Level,
    string Strategy,
    string Variation);

public sealed record DiezVisualSubjectDto(
    string SubjectId,
    string Name,
    string Description,
    IReadOnlyList<DiezVisualConsistencyRuleDto> Consistency);

public sealed record DiezVisualSceneDto(
    string SceneId,
    int Number,
    string Name,
    string Description,
    IReadOnlyList<string> ParticipantSubjectIds);

public sealed record DiezVisualSceneStateDto(
    bool MultiSubjectEnabled,
    int SubjectCount,
    string ActiveSubjectId,
    IReadOnlyList<DiezVisualSubjectDto> Subjects,
    bool ScenesEnabled,
    int SceneCount,
    string ActiveSceneId,
    IReadOnlyList<DiezVisualSceneDto> Scenes);

public sealed record DiezVisualSceneMutation(
    string ProjectJson,
    string Status,
    string Message,
    DiezVisualSceneStateDto State);

/// <summary>
/// UI-neutral boundary for the canonical visual cast + scene graph.
/// Stable SubjectId and SceneId stay inside Core; frontends edit names/descriptions and
/// participation without creating parallel UI-owned scene state.
/// </summary>
public static class DiezVisualSceneFrontendBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static readonly (string Key, string Label)[] ConsistencyKeys =
    [
        ("identity", "Identità / aspetto fisico"),
        ("outfit", "Outfit / accessori"),
        ("expression", "Espressione"),
        ("action", "Posa / azione"),
        ("framing", "Inquadratura / punto di vista"),
        ("co_scene", "Scene con altri soggetti/personaggi")
    ];

    public static DiezVisualSceneStateDto Read(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        return ProjectState(project);
    }

    public static DiezVisualSceneMutation ConfigureSubjects(string projectJson, bool enabled, int requestedCount)
    {
        var (root, project) = Parse(projectJson);
        var model = MultiSubjectProfileService.Load(project);
        model.Enabled = enabled;
        if (enabled) MultiSubjectProfileService.SetCount(model, Math.Clamp(requestedCount, 1, MultiSubjectProfileService.MaxSubjects));
        MultiSubjectProfileService.Save(project, model);
        return Result(root, project, "SAVED", enabled
            ? $"Soggetti strutturati attivi: {MultiSubjectProfileService.ActiveSubjects(model).Count}."
            : "Multi-soggetto disattivato; identità e storico restano conservati.");
    }

    public static DiezVisualSceneMutation SaveSubject(
        string projectJson,
        string subjectId,
        string? name,
        string? description)
    {
        var (root, project) = Parse(projectJson);
        var model = MultiSubjectProfileService.Load(project);
        var subject = model.Subjects.FirstOrDefault(x =>
            !x.Archived && string.Equals(x.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase));
        if (subject is null) return Result(root, project, "NOT_FOUND", "Soggetto/personaggio non trovato.");
        if (!MultiSubjectProfileService.TryRename(model, subject, name, out var error))
            return Result(root, project, "INVALID", error);

        subject.Description = (description ?? string.Empty).Trim();
        model.ActiveSubjectId = subject.SubjectId;
        MultiSubjectProfileService.Save(project, model);
        return Result(root, project, "SAVED", $"Soggetto '{subject.Name}' salvato.");
    }

    public static DiezVisualSceneMutation SaveConsistencyRule(
        string projectJson,
        string subjectId,
        string key,
        string? level,
        string? strategy,
        string? variation)
    {
        var (root, project) = Parse(projectJson);
        var model = MultiSubjectProfileService.Load(project);
        var subject = model.Subjects.FirstOrDefault(x =>
            !x.Archived && string.Equals(x.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase));
        if (subject is null) return Result(root, project, "NOT_FOUND", "Soggetto/personaggio non trovato.");

        var normalizedKey = NormalizeRuleKey(key);
        if (normalizedKey.Length == 0) return Result(root, project, "INVALID", "Regola Consistent non riconosciuta.");

        MultiSubjectProfileService.EnsureConsistencyDefaults(subject);
        var rule = subject.Consistency[normalizedKey];
        if (normalizedKey == "identity")
        {
            rule.Level = "LOCKED";
            rule.Strategy = "USER";
            rule.Variation = string.Empty;
        }
        else
        {
            rule.Level = NormalizeLevel(level);
            rule.Strategy = NormalizeStrategy(strategy);
            rule.Variation = (variation ?? string.Empty).Trim();
        }

        model.ActiveSubjectId = subject.SubjectId;
        MultiSubjectProfileService.Save(project, model);
        return Result(root, project, "SAVED", $"Consistent aggiornato per '{subject.Name}'.");
    }

    public static DiezVisualSceneMutation ConfigureScenes(string projectJson, bool enabled, int requestedCount)
    {
        var (root, project) = Parse(projectJson);
        var model = StructuredSceneProfileService.Load(project);
        model.Enabled = enabled;
        if (enabled) StructuredSceneProfileService.SetCount(model, Math.Clamp(requestedCount, 1, StructuredSceneProfileService.MaxScenes));
        StructuredSceneProfileService.Save(project, model);
        return Result(root, project, "SAVED", enabled
            ? $"Scene strutturate attive: {StructuredSceneProfileService.ActiveScenes(model).Count}."
            : "Scene strutturate disattivate; SceneId e storico restano conservati.");
    }

    public static DiezVisualSceneMutation SaveScene(
        string projectJson,
        string sceneId,
        string? name,
        string? description)
    {
        var (root, project) = Parse(projectJson);
        var model = StructuredSceneProfileService.Load(project);
        var scene = model.Scenes.FirstOrDefault(x =>
            !x.Archived && x.Included && string.Equals(x.SceneId, sceneId, StringComparison.OrdinalIgnoreCase));
        if (scene is null) return Result(root, project, "NOT_FOUND", "Scena non trovata.");
        if (!StructuredSceneProfileService.TryRename(model, scene, name, out var error))
            return Result(root, project, "INVALID", error);

        scene.Description = (description ?? string.Empty).Trim();
        model.ActiveSceneId = scene.SceneId;
        StructuredSceneProfileService.Save(project, model);
        return Result(root, project, "SAVED", $"Scena '{scene.Name}' salvata.");
    }

    public static DiezVisualSceneMutation SetSceneParticipation(
        string projectJson,
        string sceneId,
        string subjectId,
        bool participates)
    {
        var (root, project) = Parse(projectJson);
        var scenes = StructuredSceneProfileService.Load(project);
        var scene = StructuredSceneProfileService.ActiveScenes(scenes).FirstOrDefault(x =>
            string.Equals(x.SceneId, sceneId, StringComparison.OrdinalIgnoreCase));
        if (scene is null) return Result(root, project, "NOT_FOUND", "Scena non trovata.");

        var subjects = MultiSubjectProfileService.Load(project);
        var subject = MultiSubjectProfileService.ActiveSubjects(subjects).FirstOrDefault(x =>
            string.Equals(x.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase));
        if (subject is null) return Result(root, project, "NOT_FOUND", "Soggetto/personaggio non trovato.");

        StructuredSceneProfileService.SetSubjectParticipation(scenes, scene.SceneId, subject.SubjectId, participates);
        scenes.ActiveSceneId = scene.SceneId;
        subjects.ActiveSubjectId = subject.SubjectId;
        StructuredSceneProfileService.Save(project, scenes);
        MultiSubjectProfileService.Save(project, subjects);
        return Result(root, project, "SAVED", participates
            ? $"{subject.Name} partecipa a {scene.Name}."
            : $"{subject.Name} non partecipa a {scene.Name}.");
    }

    private static DiezVisualSceneMutation Result(JsonObject root, PreviewProject project, string status, string message)
    {
        MergeProject(root, project);
        return new DiezVisualSceneMutation(Write(root), status, message, ProjectState(project));
    }

    private static DiezVisualSceneStateDto ProjectState(PreviewProject project)
    {
        var multi = MultiSubjectProfileService.Load(project);
        var activeSubjects = MultiSubjectProfileService.ActiveSubjects(multi);
        var subjects = activeSubjects.Select(subject =>
        {
            MultiSubjectProfileService.EnsureConsistencyDefaults(subject);
            var rules = ConsistencyKeys.Select(x =>
            {
                var rule = subject.Consistency[x.Key];
                return new DiezVisualConsistencyRuleDto(
                    x.Key,
                    x.Label,
                    rule.Level,
                    rule.Strategy,
                    rule.Variation ?? string.Empty);
            }).ToList();
            return new DiezVisualSubjectDto(subject.SubjectId, subject.Name, subject.Description ?? string.Empty, rules);
        }).ToList();

        var sceneModel = StructuredSceneProfileService.Load(project);
        var activeScenes = StructuredSceneProfileService.ActiveScenes(sceneModel);
        var scenes = activeScenes.Select(scene => new DiezVisualSceneDto(
            scene.SceneId,
            scene.Number,
            scene.Name,
            scene.Description ?? string.Empty,
            (scene.ParticipantSubjectIds ?? []).ToList())).ToList();

        return new DiezVisualSceneStateDto(
            multi.Enabled,
            subjects.Count,
            multi.ActiveSubjectId ?? string.Empty,
            subjects,
            sceneModel.Enabled,
            scenes.Count,
            sceneModel.ActiveSceneId ?? string.Empty,
            scenes);
    }

    private static string NormalizeRuleKey(string? value)
    {
        var key = (value ?? string.Empty).Trim().ToLowerInvariant();
        return ConsistencyKeys.Any(x => x.Key == key) ? key : string.Empty;
    }

    private static string NormalizeLevel(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "LOCKED" => "LOCKED",
        "PREFERRED" => "PREFERRED",
        "FREE" => "FREE",
        _ => "PREFERRED"
    };

    private static string NormalizeStrategy(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "USER" => "USER",
        "AI" => "AI",
        "MIXED" => "MIXED",
        _ => "AI"
    };

    private static (JsonObject Root, PreviewProject Project) Parse(string projectJson)
    {
        var root = JsonNode.Parse(projectJson) as JsonObject
            ?? throw new InvalidDataException("Il JSON del progetto Diez non è valido.");
        var project = JsonSerializer.Deserialize<PreviewProject>(projectJson, JsonOptions)
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
        foreach (var entity in project.Entities)
        {
            if (JsonSerializer.SerializeToNode(entity, JsonOptions) is not JsonObject typed) continue;
            var id = Scalar(typed["EntityId"]);
            if (string.IsNullOrWhiteSpace(id)) continue;
            var existing = raw.OfType<JsonObject>().FirstOrDefault(x =>
                string.Equals(Scalar(x["EntityId"]), id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                raw.Add(typed);
                continue;
            }
            foreach (var pair in typed) existing[pair.Key] = pair.Value?.DeepClone();
        }
    }

    private static string Scalar(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        return node?.ToJsonString().Trim('"') ?? string.Empty;
    }

    private static string Write(JsonObject root) => root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
