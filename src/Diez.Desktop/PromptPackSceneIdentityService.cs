using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Adds stable SceneId and participant SubjectIds to both Prompt Pack Work Unit copies.
/// Scene/Subject IDs are audit metadata only and never enter the renderer VISUAL_ONLY prompt.
/// </summary>
internal static class PromptPackSceneIdentityService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Apply(string promptPackPath, PreviewProject project, AiExchangeState state, IEnumerable<Guid> workUnitIds)
    {
        if (!File.Exists(promptPackPath)) return;
        var scenes = StructuredSceneProfileService.Load(project);
        var active = StructuredSceneProfileService.ActiveScenes(scenes);
        if (!scenes.Enabled || active.Count == 0) return;

        var ids = workUnitIds.Distinct().ToHashSet();
        var units = state.WorkUnits
            .Where(x => ids.Contains(x.WorkUnitId))
            .OrderBy(x => x.Position)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (units.Count == 0) return;

        using var zip = ZipFile.Open(promptPackPath, ZipArchiveMode.Update);
        Rewrite(zip, "prompt-manifest.json", project, units, active);
        Rewrite(zip, "request-context.json", project, units, active);
    }

    private static void Rewrite(
        ZipArchive zip,
        string path,
        PreviewProject project,
        IReadOnlyList<AiExchangeWorkUnit> units,
        IReadOnlyList<StructuredSceneDefinition> scenes)
    {
        var entry = zip.GetEntry(path);
        if (entry is null) return;
        JsonObject? root;
        using (var source = entry.Open())
        using (var reader = new StreamReader(source, Encoding.UTF8, true))
        {
            try { root = JsonNode.Parse(reader.ReadToEnd())?.AsObject(); }
            catch { return; }
        }
        if (root?["work_units"] is not JsonArray array) return;

        foreach (var node in array.OfType<JsonObject>())
        {
            if (!Guid.TryParse(node["id"]?.ToString(), out var id)) continue;
            var unit = units.FirstOrDefault(x => x.WorkUnitId == id);
            if (unit is null) continue;
            var stablePosition = unit.Position > 0 ? unit.Position : IndexOf(units, unit) + 1;
            var scene = scenes[(Math.Max(1, stablePosition) - 1) % scenes.Count];
            var participants = StructuredSceneProfileService.Participants(project, scene);

            node["scene_id"] = scene.SceneId;
            node["scene_number"] = scene.Number;
            node["scene_name"] = scene.Name;
            node["scene_profile_schema_version"] = 1;
            node["scene_assignment"] = "STRUCTURED_SCENE_BY_STABLE_POSITION";
            node["scene_series_position"] = stablePosition;
            node["scene_participant_subject_ids"] = new JsonArray(participants.Select(x => JsonValue.Create(x.SubjectId)).ToArray());
            node["scene_participant_subject_names"] = new JsonArray(participants.Select(x => JsonValue.Create(x.Name)).ToArray());
        }

        entry.Delete();
        var replacement = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var target = replacement.Open();
        using var writer = new StreamWriter(target, new UTF8Encoding(false));
        writer.Write(root.ToJsonString(JsonOptions));
    }

    private static int IndexOf(IReadOnlyList<AiExchangeWorkUnit> units, AiExchangeWorkUnit target)
    {
        for (var i = 0; i < units.Count; i++)
            if (ReferenceEquals(units[i], target) || units[i].WorkUnitId == target.WorkUnitId) return i;
        return -1;
    }
}
