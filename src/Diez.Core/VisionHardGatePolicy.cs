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
    public const string EnvironmentMatch = "environment_match";
    public const string SingleComposition = "single_composition";
    public const string StyleMatch = "style_match";
    public const string BoldEasyMatch = "bold_easy_match";
    public const string CozyMatch = "cozy_match";
    public const string LineWeightMatch = "line_weight_match";
    public const string BookTypeFit = "book_type_fit";
    public const string ColorOutputMatch = "color_output_match";
    public const string DrawingCraft = "drawing_craft";
    public const string ColorableRegions = "colorable_regions";
    public const string CleanContours = "clean_contours";
    public const string MicroDetailFit = "micro_detail_fit";
    public const string SubjectReadability = "subject_readability";
    public const string VisibleTextOrWatermark = "visible_text_or_watermark";
    public const string MustDo = "must_do";
    public const string MustNotDo = "must_not_do";

    private static readonly HashSet<string> AlwaysHard = new(StringComparer.OrdinalIgnoreCase)
    {
        SubjectMatch,
        SceneParticipantsMatch,
        EnvironmentMatch,
        SingleComposition,
        BoldEasyMatch,
        CozyMatch,
        LineWeightMatch,
        BookTypeFit,
        ColorOutputMatch,
        DrawingCraft,
        ColorableRegions,
        CleanContours,
        MicroDetailFit,
        SubjectReadability,
        VisibleTextOrWatermark,
        MustDo,
        MustNotDo
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
- `subject_match` — HARD: compare the visible primary subject with the atomic/structured subject expected for this Work Unit.
- `scene_participants_match` — HARD when structured scene participants are declared. Every selected participant must be visibly present in the SAME unified scene, with no omission, merge or substitution.
- `environment_match` — HARD when a Scene or environment has been explicitly specified. The local Scene wins over generic series scenery.
- `single_composition` — HARD: exactly one unified primary composition unless this exact Work Unit explicitly requests otherwise.
- `style_match` — HARD when an explicit selected style exists. A polished image in a different visual language is still FAIL/HARD.
- For `Kawaii`, the visible result must unmistakably read as Kawaii: simplified rounded construction, expressive cute facial language/proportions and friendly charm. Primitive geometric placeholder art, generic iconography or merely adding a smile is not a Kawaii PASS.
- `bold_easy_match` — HARD in BOTH directions. ON requires large simple readable forms, broad colorable regions, low clutter and restrained interior detail; thick outlines alone are insufficient. OFF must not be silently converted into Bold & Easy.
- `cozy_match` — HARD in BOTH directions. ON must visibly communicate a warm, comforting, gentle and inviting mood; a cold, empty, schematic or clinical page is not a Cozy PASS merely because characters smile.
- `line_weight_match` — HARD: the selected contour treatment must be visibly respected and internally coherent.
- `book_type_fit` — HARD: a Coloring candidate must look like a professionally publishable coloring page, not a diagram, logo, technical sheet, icon sheet, crude draft or unrelated image.
- `color_output_match` — HARD for pure-B/W Coloring: final pixels must respect the selected black/white contract; no gray/tonal substitute may pass.
- `drawing_craft` — HARD for Coloring: coherent anatomy/structure, intentional organic contours and usable geometry. Obvious malformed anatomy, primitive placeholder construction, accidental duplicates or meaningless filler fail.
- `colorable_regions` — HARD when closed regions are requested: the visible page must contain comfortably fillable, clearly bounded regions rather than ambiguous open/tangled cells.
- `clean_contours` — HARD when enabled: no broken, doubled, dirty, dangling or accidental contour collisions.
- `micro_detail_fit` — HARD when tiny areas are forbidden: detail must suit the selected audience/difficulty and avoid unusable micro-cells.
- `subject_readability` — HARD when enabled: required subjects must remain clearly separated from scenery and recognizable at reduced size.
- `visible_text_or_watermark` — HARD when text is forbidden: PASS means there is NO visible text, pseudo-text, watermark, signature, ID, filename or UI fragment.
- `must_do` / `must_not_do` — HARD when the user supplied these constraints; inspect the actual pixels rather than trusting the generator description.
- `style_quality` — SOFT/REVIEW only for aesthetic differences AFTER semantic style and craft requirements have passed.
- `composition_readability` — SOFT only after `single_composition` and required subject readability have passed.

One HARD failure forces `overall_status = FAIL` and blocks approval in Diez. Use REVIEW only for genuine ambiguity or soft quality judgment, never to excuse a visible mismatch with a user-selected HARD setting.
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
