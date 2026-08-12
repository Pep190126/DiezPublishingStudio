using System.IO.Compression;
using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// Compatibility wrapper around the Vision ZIP builder. It upgrades the human execution instructions
/// so they cannot contradict the canonical expected.hard_criteria added by the semantic specification.
/// </summary>
internal static class VisionValidationPromptPackHardStyleService
{
    public static async Task<(bool Success, string Message, Guid ValidationPackId)> BuildAsync(
        PreviewProject project,
        string projectPath,
        AiExchangeState exchange,
        IEnumerable<Guid> versionIds,
        string outputPath)
    {
        var result = await VisionValidationPromptPackService.BuildAsync(
            project, projectPath, exchange, versionIds, outputPath);
        if (!result.Success) return result;
        RewriteInstructions(EnsureZip(outputPath));
        return result with
        {
            Message = result.Message + " · Style match e composizione singola sono criteri HARD quando esplicitamente richiesti."
        };
    }

    internal static void RewriteInstructions(string zipPath)
    {
        if (!File.Exists(zipPath)) return;
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        var entry = zip.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/').TrimStart('/'), "instructions.md", StringComparison.OrdinalIgnoreCase));
        if (entry is null) return;
        string text;
        using (var source = entry.Open())
        using (var reader = new StreamReader(source, Encoding.UTF8, true))
            text = reader.ReadToEnd();

        text = text.Replace(
            "- `composition_readability` — SOFT\n- `style_quality` — SOFT",
            "- `single_composition` — HARD: exactly one unified composition unless the exact Work Unit explicitly requests multi-panel output\n- `style_match` — HARD when `expected.style` contains an explicitly selected style; a polished result in a materially different style is FAIL/HARD\n- `composition_readability` — SOFT only after single-composition compliance passes\n- `style_quality` — SOFT only for execution/taste differences inside the correctly matched selected style",
            StringComparison.Ordinal);
        text = text.Replace(
            "3. Evaluate semantic subject match, environment match, MUST DO, MUST NOT DO, Book-Type fitness, item-specific overrides, visible text/artifacts, anatomy/geometry, composition/readability and publication quality.",
            "3. Evaluate the exact atomic item subject, environment, MUST DO, MUST NOT DO, Book-Type fitness, item-specific overrides, visible text/artifacts, anatomy/geometry, single-composition compliance, explicit selected-style match and publication quality. Series-level wording never authorizes a triptych/grid or multiple sibling subjects inside one Work Unit.",
            StringComparison.Ordinal);
        text = text.Replace(
            "5. A HARD FAIL means the visible image materially violates an explicit hard requirement or is the wrong visual/content for the requested item. One HARD FAIL makes `overall_status = FAIL`.",
            "5. A HARD FAIL means the visible image materially violates an explicit hard requirement or is the wrong visual/content for the requested item. Explicit selected-style mismatch and unauthorized triptych/grid/multi-panel output are HARD failures. One HARD FAIL makes `overall_status = FAIL`.",
            StringComparison.Ordinal);

        entry.Delete();
        var replacement = zip.CreateEntry("instructions.md", CompressionLevel.Optimal);
        using var target = replacement.Open();
        using var writer = new StreamWriter(target, new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string EnsureZip(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? path : path + ".zip";
}
