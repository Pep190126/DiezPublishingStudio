using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Adds stable internal subject identity metadata to both Prompt Pack Work Unit copies.
/// SubjectId is transport/audit metadata only and is deliberately excluded from the image-model prompt.
/// </summary>
internal static class PromptPackSubjectIdentityService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Apply(string promptPackPath, PreviewProject project, AiExchangeState state, IEnumerable<Guid> workUnitIds)
    {
        if (!File.Exists(promptPackPath)) return;
        var model = MultiSubjectProfileService.Load(project);
        var active = MultiSubjectProfileService.ActiveSubjects(model);
        if (!model.Enabled || active.Count == 0) return;

        var ids = workUnitIds.Distinct().ToHashSet();
        var units = state.WorkUnits
            .Where(x => ids.Contains(x.WorkUnitId))
            .OrderBy(x => x.Position)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (units.Count == 0) return;

        using var zip = ZipFile.Open(promptPackPath, ZipArchiveMode.Update);
        Rewrite(zip, "prompt-manifest.json", units, active);
        Rewrite(zip, "request-context.json", units, active);
    }

    private static void Rewrite(ZipArchive zip, string path, IReadOnlyList<AiExchangeWorkUnit> units, IReadOnlyList<MultiSubjectDefinition> subjects)
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
            var index = units.FindIndex(x => x.WorkUnitId == id);
            if (index < 0) continue;
            var subject = subjects[index % subjects.Count];
            node["subject_id"] = subject.SubjectId;
            node["subject_name"] = subject.Name;
            node["subject_profile_schema_version"] = 1;
            node["subject_assignment"] = "STRUCTURED_MULTI_SUBJECT";
        }

        entry.Delete();
        var replacement = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var target = replacement.Open();
        using var writer = new StreamWriter(target, new UTF8Encoding(false));
        writer.Write(root.ToJsonString(JsonOptions));
    }

    private static int FindIndex(this IReadOnlyList<AiExchangeWorkUnit> units, Func<AiExchangeWorkUnit, bool> predicate)
    {
        for (var i = 0; i < units.Count; i++) if (predicate(units[i])) return i;
        return -1;
    }
}
