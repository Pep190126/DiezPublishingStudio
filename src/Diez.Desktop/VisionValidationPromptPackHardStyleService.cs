using System.IO.Compression;
using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// Compatibility wrapper around the Vision ZIP builder. It upgrades the human execution instructions
/// so they cannot contradict the canonical expected.hard_criteria added by the semantic specification.
/// The semantic policy text itself lives in Diez.Core.
/// </summary>
internal static class VisionValidationPromptPackHardStyleService
{
    private const string HardPolicyMarker = "## Diez 3.6 semantic HARD policy";

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
        var zipPath = EnsureZip(outputPath);
        VisionStructuredSubjectService.RewritePromptPack(zipPath, project);
        RewriteInstructions(zipPath);
        return (
            true,
            result.Message + " · Style match, Bold & Easy, Cozy, soggetto/scena strutturati e composizione singola sono criteri HARD quando applicabili.",
            result.ValidationPackId);
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
            "- `single_composition` — HARD: exactly one unified composition unless the exact Work Unit explicitly requests multi-panel output\n- `scene_participants_match` — HARD when structured scene participants are expected: every listed participant must be visibly present in the same scene and unassigned cast members must not be introduced as substitutes\n- `style_match` — HARD when `expected.style` contains an explicitly selected style\n- `bold_easy_match` — HARD for both expected ON and expected OFF\n- `cozy_match` — HARD for both expected ON and expected OFF\n- `line_weight_match` — HARD for the selected contour weight, especially Thin/Fine and Very thin/Extra Fine\n- `composition_readability` — SOFT only after single-composition compliance passes\n- `style_quality` — SOFT only for execution/taste differences inside the correctly matched selected style",
            StringComparison.Ordinal);

        if (!text.Contains(HardPolicyMarker, StringComparison.Ordinal))
            text = text.TrimEnd() + "\n\n" + VisionHardGatePolicy.InstructionMarkdown();

        entry.Delete();
        var replacement = zip.CreateEntry("instructions.md", CompressionLevel.Optimal);
        using var target = replacement.Open();
        using var writer = new StreamWriter(target, new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string EnsureZip(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? path : path + ".zip";
}
