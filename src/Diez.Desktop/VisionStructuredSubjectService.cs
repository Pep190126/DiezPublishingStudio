using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps semantic Vision QA on the same structured subject/scene identities used by the renderer.
/// The real candidate pixels remain authoritative; this service only removes ambiguity in expected content.
/// </summary>
internal static class VisionStructuredSubjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Apply(PreviewProject project, AiExchangeWorkUnit unit, VisionValidationRequest request)
    {
        var model = MultiSubjectProfileService.Load(project);
        var active = MultiSubjectProfileService.ActiveSubjects(model);
        if (model.Enabled && active.Count > 0)
        {
            var position = Math.Max(1, unit.Position);
            var subject = active[(position - 1) % active.Count];
            request.Expected.ItemSubject = subject.Name;
            request.Expected.Subject = string.IsNullOrWhiteSpace(model.GroupDescription)
                ? request.Expected.Subject
                : PromptEnglishNormalizer.NormalizeProviderFacing(model.GroupDescription);
            request.Expected.ConsistencyRules = JoinRules(
                request.Expected.ConsistencyRules,
                MultiSubjectProfileService.BuildConsistencyRules(subject));
        }

        ApplyScene(project, unit, request);
    }

    public static void RewritePromptPack(string zipPath, PreviewProject project)
    {
        if (!File.Exists(zipPath)) return;
        var model = MultiSubjectProfileService.Load(project);
        var active = MultiSubjectProfileService.ActiveSubjects(model);
        var scenes = StructuredSceneProfileService.Load(project);
        var activeScenes = StructuredSceneProfileService.ActiveScenes(scenes);
        if ((!model.Enabled || active.Count == 0) && (!scenes.Enabled || activeScenes.Count == 0)) return;

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        var entry = zip.GetEntry("vision-manifest.json");
        if (entry is null) return;
        JsonObject? root;
        using (var source = entry.Open())
        using (var reader = new StreamReader(source, Encoding.UTF8, true))
        {
            try { root = JsonNode.Parse(reader.ReadToEnd())?.AsObject(); }
            catch { return; }
        }
        var requestArray = root?["requests"] as JsonArray ?? root?["items"] as JsonArray;
        if (requestArray is null) return;

        foreach (var request in requestArray.OfType<JsonObject>())
        {
            if (request["expected"] is not JsonObject expected) continue;
            var position = ParseInt(expected["series_position"]?.ToString(), 1);

            if (model.Enabled && active.Count > 0)
            {
                var subject = active[(Math.Max(1, position) - 1) % active.Count];
                expected["item_subject"] = subject.Name;
                if (!string.IsNullOrWhiteSpace(model.GroupDescription))
                    expected["subject"] = PromptEnglishNormalizer.NormalizeProviderFacing(model.GroupDescription);
                expected["consistency_rules"] = JoinRules(
                    expected["consistency_rules"]?.ToString() ?? string.Empty,
                    MultiSubjectProfileService.BuildConsistencyRules(subject));
                request["subject_id"] = subject.SubjectId;
                request["subject_name"] = subject.Name;
                request["subject_assignment"] = "STRUCTURED_MULTI_SUBJECT";
            }

            if (scenes.Enabled && activeScenes.Count > 0)
            {
                var scene = activeScenes[(Math.Max(1, position) - 1) % activeScenes.Count];
                var participants = StructuredSceneProfileService.Participants(project, scene);
                var participantNames = participants.Select(x => x.Name).ToArray();
                if (participantNames.Length > 0)
                    expected["item_subject"] = string.Join(", ", participantNames);
                expected["consistency_rules"] = JoinRules(
                    expected["consistency_rules"]?.ToString() ?? string.Empty,
                    BuildSceneVisionRules(project, scene));
                request["scene_id"] = scene.SceneId;
                request["scene_number"] = scene.Number;
                request["scene_name"] = scene.Name;
                request["scene_assignment"] = "STRUCTURED_SCENE_BY_STABLE_POSITION";
                request["scene_participant_subject_ids"] = new JsonArray(participants.Select(x => JsonValue.Create(x.SubjectId)).ToArray());
                request["scene_participant_subject_names"] = new JsonArray(participantNames.Select(x => JsonValue.Create(x)).ToArray());
            }
        }

        entry.Delete();
        var replacement = zip.CreateEntry("vision-manifest.json", CompressionLevel.Optimal);
        using var target = replacement.Open();
        using var writer = new StreamWriter(target, new UTF8Encoding(false));
        writer.Write(root!.ToJsonString(JsonOptions));
    }

    private static void ApplyScene(PreviewProject project, AiExchangeWorkUnit unit, VisionValidationRequest request)
    {
        var scene = StructuredSceneProfileService.SceneForPosition(project, unit.Position);
        if (scene is null) return;
        var participants = StructuredSceneProfileService.Participants(project, scene);
        if (participants.Count > 0)
            request.Expected.ItemSubject = string.Join(", ", participants.Select(x => x.Name));
        request.Expected.ConsistencyRules = JoinRules(
            request.Expected.ConsistencyRules,
            BuildSceneVisionRules(project, scene));
    }

    private static string BuildSceneVisionRules(PreviewProject project, StructuredSceneDefinition scene)
    {
        var participants = StructuredSceneProfileService.Participants(project, scene);
        var lines = new List<string>();
        var description = PromptEnglishNormalizer.NormalizeProviderFacing(scene.Description);
        if (!string.IsNullOrWhiteSpace(description))
            lines.Add("SCENE INTENT — HARD: " + description);
        if (participants.Count > 0)
        {
            lines.Add("SCENE PARTICIPANTS — HARD: " + string.Join(", ", participants.Select(x => x.Name)) + ". Every listed participant must be visibly present in the same unified scene.");
            foreach (var participant in participants)
            {
                var rules = MultiSubjectProfileService.BuildConsistencyRules(participant);
                if (!string.IsNullOrWhiteSpace(rules))
                    lines.Add("PARTICIPANT CONSISTENT [" + participant.Name + "]:\n" + rules);
            }
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string JoinRules(string? general, string? subject)
    {
        var a = (general ?? string.Empty).Trim();
        var b = (subject ?? string.Empty).Trim();
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;
        if (a.Contains(b, StringComparison.Ordinal)) return a;
        return a + Environment.NewLine + Environment.NewLine + "STRUCTURED SUBJECT/SCENE CONSISTENT:" + Environment.NewLine + b;
    }

    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out var parsed) ? parsed : fallback;
}
