using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps semantic Vision QA on the same structured subject identity used by the renderer.
/// The real candidate pixels remain authoritative; this service only removes ambiguity in expected.item_subject.
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
        if (!model.Enabled || active.Count == 0) return;
        var position = Math.Max(1, unit.Position);
        var subject = active[(position - 1) % active.Count];
        request.Expected.ItemSubject = subject.Name;
        request.Expected.Subject = string.IsNullOrWhiteSpace(model.GroupDescription)
            ? request.Expected.Subject
            : PromptEnglishNormalizer.NormalizeProviderFacing(model.GroupDescription);
        var subjectRules = MultiSubjectProfileService.BuildConsistencyRules(subject);
        request.Expected.ConsistencyRules = JoinRules(request.Expected.ConsistencyRules, subjectRules);
    }

    public static void RewritePromptPack(string zipPath, PreviewProject project)
    {
        if (!File.Exists(zipPath)) return;
        var model = MultiSubjectProfileService.Load(project);
        var active = MultiSubjectProfileService.ActiveSubjects(model);
        if (!model.Enabled || active.Count == 0) return;

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
        if (root?["requests"] is not JsonArray requests) return;

        foreach (var request in requests.OfType<JsonObject>())
        {
            if (request["expected"] is not JsonObject expected) continue;
            var position = ParseInt(expected["series_position"]?.ToString(), 1);
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

        entry.Delete();
        var replacement = zip.CreateEntry("vision-manifest.json", CompressionLevel.Optimal);
        using var target = replacement.Open();
        using var writer = new StreamWriter(target, new UTF8Encoding(false));
        writer.Write(root.ToJsonString(JsonOptions));
    }

    private static string JoinRules(string? general, string? subject)
    {
        var a = (general ?? string.Empty).Trim();
        var b = (subject ?? string.Empty).Trim();
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;
        if (a.Contains(b, StringComparison.Ordinal)) return a;
        return a + Environment.NewLine + Environment.NewLine + "SUBJECT-SPECIFIC CONSISTENT:" + Environment.NewLine + b;
    }

    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out var parsed) ? parsed : fallback;
}
