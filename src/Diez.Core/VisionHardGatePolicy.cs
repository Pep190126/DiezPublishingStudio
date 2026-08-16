namespace DiezPublishingStudio;

internal sealed record VisionHardGateCheck(
    string Key,
    string Status,
    string Severity,
    bool BlocksApproval);

internal sealed record VisionHardGateAggregate(
    string OverallStatus,
    bool BlocksApproval,
    int HardFailureCount,
    int ReviewCount);

/// <summary>
/// Canonical framework-wide Vision semantic gate policy. This class deliberately has
/// no dependency on provider adapters, AI Exchange DTOs or UI types, so every frontend
/// can enforce the same approval semantics.
/// </summary>
internal static class VisionHardGatePolicy
{
    public const string Pass = "PASS";
    public const string Review = "REVIEW";
    public const string Fail = "FAIL";
    public const string NotApplicable = "NA";
    public const string Hard = "HARD";
    public const string Soft = "SOFT";

    public const string SubjectMatch = "subject_match";
    public const string SceneParticipantsMatch = "scene_participants_match";
    public const string SingleComposition = "single_composition";
    public const string StyleMatch = "style_match";
    public const string BoldEasyMatch = "bold_easy_match";
    public const string CozyMatch = "cozy_match";
    public const string LineWeightMatch = "line_weight_match";

    private static readonly HashSet<string> AlwaysHard = new(StringComparer.OrdinalIgnoreCase)
    {
        SubjectMatch,
        SceneParticipantsMatch,
        SingleComposition,
        BoldEasyMatch,
        CozyMatch,
        LineWeightMatch
    };

    public static bool IsHard(string? key, string? selectedStyle = null)
    {
        var normalized = (key ?? string.Empty).Trim();
        if (AlwaysHard.Contains(normalized)) return true;
        return string.Equals(normalized, StyleMatch, StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(selectedStyle);
    }

    public static VisionHardGateCheck Enforce(
        string? key,
        string? status,
        string? severity,
        string? selectedStyle = null)
    {
        var normalizedKey = (key ?? string.Empty).Trim();
        var normalizedStatus = NormalizeStatus(status);
        var effectiveSeverity = IsHard(normalizedKey, selectedStyle)
            ? Hard
            : NormalizeSeverity(severity);
        var blocks = string.Equals(effectiveSeverity, Hard, StringComparison.Ordinal) &&
                     string.Equals(normalizedStatus, Fail, StringComparison.Ordinal);
        return new VisionHardGateCheck(normalizedKey, normalizedStatus, effectiveSeverity, blocks);
    }

    public static VisionHardGateAggregate Aggregate(IEnumerable<VisionHardGateCheck> checks)
    {
        var list = checks?.ToList() ?? [];
        var hardFailures = list.Count(x => x.BlocksApproval);
        if (hardFailures > 0)
            return new VisionHardGateAggregate(Fail, true, hardFailures, 0);

        var reviews = list.Count(x =>
            string.Equals(x.Status, Review, StringComparison.Ordinal) ||
            (string.Equals(x.Status, Fail, StringComparison.Ordinal) &&
             string.Equals(x.Severity, Soft, StringComparison.Ordinal)));
        return reviews > 0
            ? new VisionHardGateAggregate(Review, false, 0, reviews)
            : new VisionHardGateAggregate(Pass, false, 0, 0);
    }

    public static string InstructionMarkdown() => """
## Diez 3.6 semantic HARD policy
The real candidate pixels are authoritative. Evaluate the exact Work Unit, not the series as a whole.

Required semantic checks:
- `subject_match` — HARD: compare the visible primary subject with `expected.item_subject`, the atomic/structured subject for this Work Unit. When structured multi-subject mode is active, `subject_id` is trusted Diez audit metadata and `subject_name` / `expected.item_subject` identify that exact subject profile.
- `scene_participants_match` — HARD when structured scene participants are declared in the expected specification or consistency rules. Every selected participant must be visibly present in the SAME unified scene, with no substitution by another structured cast member. Return NA only when no structured scene participants are expected.
- `single_composition` — HARD: exactly one unified primary composition unless this exact Work Unit explicitly requests otherwise.
- `style_match` — HARD when an explicit selected style exists: the visible image must materially match `expected.style`. A polished image in a different style is still FAIL/HARD.
- For `Kawaii`, realistic natural-history or engraving-like treatment, dense realistic hatching and anatomically literal documentary rendering are a HARD style mismatch when the page does not visibly read as Kawaii.
- For `Cartoon`, documentary/naturalistic rendering and photographic anatomy are a HARD mismatch when cartoon construction is absent.
- `bold_easy_match` — HARD in BOTH directions. If `expected.bold_easy=true`, the page must visibly satisfy the Bold & Easy profile. If false, the page must not be automatically simplified, enlarged or thickened into Bold & Easy against the selected style/line weight/complexity/density.
- `cozy_match` — HARD in BOTH directions. If `expected.cozy=true`, the page must visibly read as warm, comforting, gentle and inviting. If false, do not impose a Cozy mood, homelike staging or comforting decorative treatment unless another explicit requirement independently demands it.
- `line_weight_match` — HARD. Thin/Fine or Very thin/Extra Fine must remain visibly thin and must not be converted into Bold & Easy-like thick contours.
- `style_quality` — SOFT/REVIEW only for aesthetic or execution differences AFTER `style_match` has passed.
- `composition_readability` — SOFT only after `single_composition` has passed.

One HARD failure forces `overall_status = FAIL` and blocks approval in Diez. Use REVIEW only for genuine ambiguity or soft quality judgment, not for a visible subject/scene/style/Bold&Easy/Cozy/line-weight/composition mismatch.
""".Trim();

    private static string NormalizeStatus(string? status)
    {
        var value = (status ?? string.Empty).Trim().ToUpperInvariant();
        return value switch
        {
            Pass => Pass,
            Fail => Fail,
            NotApplicable => NotApplicable,
            _ => Review
        };
    }

    private static string NormalizeSeverity(string? severity) =>
        string.Equals((severity ?? string.Empty).Trim(), Hard, StringComparison.OrdinalIgnoreCase)
            ? Hard
            : Soft;
}
