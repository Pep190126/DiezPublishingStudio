using System.IO.Compression;
using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// Compatibility wrapper around the Vision ZIP builder. It upgrades the human execution instructions
/// so they cannot contradict the canonical expected.hard_criteria added by the semantic specification.
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
        {
            text = text.TrimEnd() + "\n\n" + $"""
{HardPolicyMarker}
The real candidate pixels are authoritative. Evaluate the exact Work Unit, not the series as a whole.

Required semantic checks:
- `subject_match` — HARD: compare the visible primary subject with `expected.item_subject`, the atomic/structured subject for this Work Unit. When structured multi-subject mode is active, `subject_id` is trusted Diez audit metadata and `subject_name` / `expected.item_subject` identify that exact subject profile.
- `scene_participants_match` — HARD when structured scene participants are declared in the expected specification or consistency rules. Every selected participant must be visibly present in the SAME unified scene, with no substitution by another structured cast member. Return NA only when no structured scene participants are expected.
- `single_composition` — HARD: exactly one unified primary composition unless this exact Work Unit explicitly requests otherwise.
- `style_match` — HARD: the visible image must materially match `expected.style`. A polished image in a different style is still FAIL/HARD.
- For `Kawaii`, realistic natural-history or engraving-like treatment, dense realistic hatching and anatomically literal documentary rendering are a HARD style mismatch when the page does not visibly read as Kawaii.
- For `Cartoon`, documentary/naturalistic rendering and photographic anatomy are a HARD mismatch when cartoon construction is absent.
- `bold_easy_match` — HARD in BOTH directions. If `expected.bold_easy=true`, the page must visibly satisfy the Bold & Easy profile. If false, the page must not be automatically simplified, enlarged or thickened into Bold & Easy against the selected style/line weight/complexity/density.
- `cozy_match` — HARD in BOTH directions. If `expected.cozy=true`, the page must visibly read as warm, comforting, gentle and inviting. If false, do not impose a Cozy mood, homelike staging or comforting decorative treatment unless another explicit requirement independently demands it.
- `line_weight_match` — HARD. Thin/Fine or Very thin/Extra Fine must remain visibly thin and must not be converted into Bold & Easy-like thick contours.
- `style_quality` — SOFT/REVIEW only for aesthetic or execution differences AFTER `style_match` has passed.
- `composition_readability` — SOFT only after `single_composition` has passed.

One HARD failure forces `overall_status = FAIL` and blocks approval in Diez. Use REVIEW only for genuine ambiguity or soft quality judgment, not for a visible subject/scene/style/Bold&Easy/Cozy/line-weight/composition mismatch.
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
