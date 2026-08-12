using System.IO.Compression;
using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// Compatibility wrapper around the Vision ZIP builder. It upgrades the human execution instructions
/// so they cannot contradict the canonical expected.hard_criteria added by the semantic specification.
/// </summary>
internal static class VisionValidationPromptPackHardStyleService
{
    private const string HardPolicyMarker = "## Diez 3.4 semantic HARD policy";

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
        return (
            true,
            result.Message + " · Style match e composizione singola sono criteri HARD quando esplicitamente richiesti.",
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

        // Keep compatibility replacements for older instruction templates when they match.
        text = text.Replace(
            "- `composition_readability` — SOFT\n- `style_quality` — SOFT",
            "- `single_composition` — HARD: exactly one unified composition unless the exact Work Unit explicitly requests multi-panel output\n- `style_match` — HARD when `expected.style` contains an explicitly selected style; a polished result in a materially different style is FAIL/HARD\n- `composition_readability` — SOFT only after single-composition compliance passes\n- `style_quality` — SOFT only for execution/taste differences inside the correctly matched selected style",
            StringComparison.Ordinal);
        text = text.Replace(
            "3. Evaluate semantic subject match, environment match, MUST DO, MUST NOT DO, Book-Type fitness, item-specific overrides, visible text/artifacts, anatomy/geometry, composition/readability and publication quality.",
            "3. Evaluate the exact atomic item subject, environment, MUST DO, MUST NOT DO, Book-Type fitness, item-specific overrides, visible text/artifacts, anatomy/geometry, single-composition compliance, explicit selected-style match and publication quality. Series-level wording never authorizes multiple sibling subjects inside one Work Unit.",
            StringComparison.Ordinal);
        text = text.Replace(
            "5. A HARD FAIL means the visible image materially violates an explicit hard requirement or is the wrong visual/content for the requested item. One HARD FAIL makes `overall_status = FAIL`.",
            "5. A HARD FAIL means the visible image materially violates an explicit hard requirement or is the wrong visual/content for the requested item. Explicit selected-style mismatch and unauthorized subdivision into multiple independent compositions are HARD failures. One HARD FAIL makes `overall_status = FAIL`.",
            StringComparison.Ordinal);

        // The base Vision template may evolve, so do not depend on exact replacement strings for the
        // safety-critical policy. Append one explicit section unconditionally (once) to the final file.
        if (!text.Contains(HardPolicyMarker, StringComparison.Ordinal))
        {
            text = text.TrimEnd() + "\n\n" + $"""
{HardPolicyMarker}
The real candidate pixels are authoritative. Evaluate the exact Work Unit, not the series as a whole.

Required semantic checks:
- `subject_match` — HARD: compare the visible primary subject with `expected.item_subject`, which is the atomic subject for this Work Unit. Series-level wording never authorizes multiple sibling subjects in one result.
- `single_composition` — HARD: the candidate must contain exactly one unified primary composition unless this exact Work Unit explicitly requests otherwise. A canvas visibly subdivided into independent alternatives/regions is FAIL/HARD.
- `style_match` — HARD whenever `expected.style` or the authoritative `generation_contract` declares a selected style. A professionally executed image in a materially different style is still FAIL/HARD.
- For `Kawaii / Cartoon`, realistic natural-history/engraving-like rendering, dense realistic hatching and anatomically literal documentary treatment are a HARD `style_match` failure when the image does not visibly read as cute/cartoon.
- `style_quality` — SOFT/REVIEW only for aesthetic or execution differences AFTER `style_match` has passed. Never use a soft `style_quality` opinion to excuse a failed selected-style match.
- `composition_readability` — SOFT only after `single_composition` has passed.

One HARD failure forces `overall_status = FAIL` and blocks approval in Diez. Use REVIEW only for genuine ambiguity or soft quality judgment, not for a visible subject/style/composition mismatch.
""".Trim();
        }

        entry.Delete();
        var replacement = zip.CreateEntry("instructions.md", CompressionLevel.Optimal);
        using var target = replacement.Open();
        using var writer = new StreamWriter(target, new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string EnsureZip(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? path : path + ".zip";
}
