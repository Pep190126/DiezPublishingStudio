using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal sealed class StructuredSceneProfile
{
    public int SchemaVersion { get; set; } = 1;
    public bool Enabled { get; set; }
    public int RequestedCount { get; set; } = 1;
    public string ActiveSceneId { get; set; } = string.Empty;
    public List<StructuredSceneDefinition> Scenes { get; set; } = [];
}

internal sealed class StructuredSceneDefinition
{
    public string SceneId { get; set; } = Guid.NewGuid().ToString("D");
    public int Number { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Included { get; set; } = true;
    public bool Archived { get; set; }
    public List<string> ParticipantSubjectIds { get; set; } = [];

    public override string ToString()
    {
        var title = string.IsNullOrWhiteSpace(Name) ? $"Scena {Math.Max(1, Number)}" : Name.Trim();
        return $"{Math.Max(1, Number)} — {title}";
    }
}

/// <summary>
/// Optional structured scene graph. SceneId is stable and internal; display number/name may change freely.
/// Scene membership is canonical here and is referenced by SubjectId, never by mutable subject names.
/// </summary>
internal static class StructuredSceneProfileService
{
    private const string EntityKind = "DiezStructuredSceneProfile";
    public const int MaxScenes = 120;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static StructuredSceneProfile Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        StructuredSceneProfile model;
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) model = new StructuredSceneProfile();
        else
        {
            try { model = JsonSerializer.Deserialize<StructuredSceneProfile>(entity.Notes, JsonOptions) ?? new StructuredSceneProfile(); }
            catch { model = new StructuredSceneProfile(); }
        }
        Normalize(model);
        return model;
    }

    public static void Save(PreviewProject project, StructuredSceneProfile model)
    {
        Normalize(model);
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity { Kind = EntityKind, Name = "Scene strutturate", IsCandidate = false };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(model, JsonOptions);
    }

    public static IReadOnlyList<StructuredSceneDefinition> ActiveScenes(StructuredSceneProfile model) =>
        model.Scenes.Where(x => !x.Archived && x.Included).OrderBy(x => x.Number).Take(MaxScenes).ToList();

    public static StructuredSceneDefinition? ActiveScene(StructuredSceneProfile model)
    {
        var active = ActiveScenes(model);
        return active.FirstOrDefault(x => string.Equals(x.SceneId, model.ActiveSceneId, StringComparison.OrdinalIgnoreCase))
               ?? active.FirstOrDefault();
    }

    public static void SetCount(StructuredSceneProfile model, int requested)
    {
        requested = Math.Clamp(requested, 1, MaxScenes);
        model.RequestedCount = requested;
        var available = model.Scenes.Where(x => !x.Archived).OrderBy(x => x.Number).ToList();
        while (available.Count < requested)
        {
            var created = NewScene(model.Scenes.Count + 1);
            model.Scenes.Add(created);
            available.Add(created);
        }
        for (var i = 0; i < available.Count; i++) available[i].Included = i < requested;
        RenumberActive(model);
        var active = ActiveScenes(model);
        if (active.Count > 0 && active.All(x => !string.Equals(x.SceneId, model.ActiveSceneId, StringComparison.OrdinalIgnoreCase)))
            model.ActiveSceneId = active[0].SceneId;
    }

    public static StructuredSceneDefinition Add(StructuredSceneProfile model)
    {
        var active = ActiveScenes(model);
        if (active.Count >= MaxScenes) return active[^1];
        var reusable = model.Scenes.FirstOrDefault(x => !x.Archived && !x.Included);
        if (reusable is not null)
        {
            reusable.Included = true;
            model.RequestedCount = ActiveScenes(model).Count;
            RenumberActive(model);
            model.ActiveSceneId = reusable.SceneId;
            return reusable;
        }
        var created = NewScene(model.Scenes.Count + 1);
        model.Scenes.Add(created);
        model.RequestedCount = ActiveScenes(model).Count;
        RenumberActive(model);
        model.ActiveSceneId = created.SceneId;
        return created;
    }

    public static void RemoveFromActiveScenes(StructuredSceneProfile model, string? sceneId)
    {
        var active = ActiveScenes(model);
        if (active.Count <= 1) return;
        var target = active.FirstOrDefault(x => string.Equals(x.SceneId, sceneId, StringComparison.OrdinalIgnoreCase));
        if (target is null) return;
        target.Included = false;
        model.RequestedCount = ActiveScenes(model).Count;
        RenumberActive(model);
        model.ActiveSceneId = ActiveScenes(model).First().SceneId;
    }

    public static bool TryRename(StructuredSceneProfile model, StructuredSceneDefinition scene, string? name, out string error)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length == 0)
        {
            error = "Il nome della scena non può essere vuoto.";
            return false;
        }
        if (model.Scenes.Any(x => !x.Archived && !ReferenceEquals(x, scene) && string.Equals(x.Name.Trim(), clean, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Esiste già una scena con questo nome.";
            return false;
        }
        scene.Name = clean;
        error = string.Empty;
        return true;
    }

    public static void SetSubjectParticipation(StructuredSceneProfile model, string sceneId, string subjectId, bool participates)
    {
        var scene = model.Scenes.FirstOrDefault(x => string.Equals(x.SceneId, sceneId, StringComparison.OrdinalIgnoreCase));
        if (scene is null || string.IsNullOrWhiteSpace(subjectId)) return;
        scene.ParticipantSubjectIds ??= [];
        scene.ParticipantSubjectIds.RemoveAll(x => string.Equals(x, subjectId, StringComparison.OrdinalIgnoreCase));
        if (participates) scene.ParticipantSubjectIds.Add(subjectId.Trim());
    }

    public static IReadOnlyList<MultiSubjectDefinition> Participants(PreviewProject project, StructuredSceneDefinition scene)
    {
        var multi = MultiSubjectProfileService.Load(project);
        var active = MultiSubjectProfileService.ActiveSubjects(multi);
        if (!multi.Enabled || active.Count == 0) return [];
        var wanted = new HashSet<string>(scene.ParticipantSubjectIds ?? [], StringComparer.OrdinalIgnoreCase);
        return active.Where(x => wanted.Contains(x.SubjectId)).ToList();
    }

    public static StructuredSceneDefinition? SceneForWorkUnit(PreviewProject project, AiExchangeWorkUnit unit)
    {
        var model = Load(project);
        var active = ActiveScenes(model);
        if (!model.Enabled || active.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(unit.SceneId))
        {
            var explicitScene = active.FirstOrDefault(x => string.Equals(x.SceneId, unit.SceneId, StringComparison.OrdinalIgnoreCase));
            if (explicitScene is not null) return explicitScene;
        }
        var position = Math.Max(1, unit.Position);
        return active[(position - 1) % active.Count];
    }

    public static void SynchronizeWorkUnits(PreviewProject project, AiExchangeState state)
    {
        var model = Load(project);
        var active = ActiveScenes(model);
        if (!model.Enabled || active.Count == 0) return;
        foreach (var unit in state.WorkUnits.Where(x => string.Equals(x.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase)))
        {
            var existing = active.FirstOrDefault(x => string.Equals(x.SceneId, unit.SceneId, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) continue;
            var position = Math.Max(1, unit.Position);
            unit.SceneId = active[(position - 1) % active.Count].SceneId;
        }
    }

    public static string BuildSceneIntent(PreviewProject project, StructuredSceneDefinition scene)
    {
        var participants = Participants(project, scene);
        var sb = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(scene.Name) ? $"Scene {scene.Number}" : scene.Name.Trim();
        sb.Append("Scene ").Append(scene.Number).Append(" — ").Append(title);
        if (!string.IsNullOrWhiteSpace(scene.Description)) sb.Append(": ").Append(scene.Description.Trim());
        if (participants.Count > 0) sb.Append(". Required participants: ").Append(string.Join(", ", participants.Select(x => x.Name))).Append('.');
        return sb.ToString();
    }

    private static void Normalize(StructuredSceneProfile model)
    {
        model.Scenes ??= [];
        model.RequestedCount = Math.Clamp(model.RequestedCount <= 0 ? 1 : model.RequestedCount, 1, MaxScenes);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < model.Scenes.Count; i++)
        {
            var scene = model.Scenes[i];
            if (string.IsNullOrWhiteSpace(scene.SceneId) || !ids.Add(scene.SceneId)) scene.SceneId = Guid.NewGuid().ToString("D");
            ids.Add(scene.SceneId);
            if (scene.Number <= 0) scene.Number = i + 1;
            scene.Name = string.IsNullOrWhiteSpace(scene.Name) ? $"Scena {scene.Number}" : scene.Name.Trim();
            scene.Description ??= string.Empty;
            scene.ParticipantSubjectIds ??= [];
            scene.ParticipantSubjectIds = scene.ParticipantSubjectIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        if (model.Enabled) SetCount(model, model.RequestedCount);
    }

    private static void RenumberActive(StructuredSceneProfile model)
    {
        var active = model.Scenes.Where(x => !x.Archived && x.Included).OrderBy(x => x.Number).ToList();
        for (var i = 0; i < active.Count; i++) active[i].Number = i + 1;
    }

    private static StructuredSceneDefinition NewScene(int number) => new()
    {
        Number = Math.Max(1, number),
        Name = $"Scena {Math.Max(1, number)}"
    };
}
