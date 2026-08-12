namespace DiezPublishingStudio;

internal sealed record ColoringIndependentHardProfile(
    string Style,
    string LineWeight,
    bool BoldEasy,
    bool Cozy);

/// <summary>
/// Central resolver for orthogonal Coloring constraints. Visual Style is one single selected HARD style;
/// Bold & Easy and Cozy are independent bidirectional HARD dimensions.
/// </summary>
internal static class ColoringIndependentHardProfileService
{
    public static IReadOnlyList<string> SelectableStyles =>
        BookTypePromptProfileService.ColoringStyles
            .Where(x => !string.Equals(x, "Cozy", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(x, "Bold & Easy", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(x, "Kawaii / Cartoon", StringComparison.OrdinalIgnoreCase))
            .Concat(CustomStyleLibraryService.SelectableLabels())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static ColoringIndependentHardProfile Resolve(PreviewProject project)
    {
        var p = BookTypePromptProfileService.LoadColoring(project);
        var rawStyle = (p.Style ?? string.Empty).Trim();
        var legacyCozy = string.Equals(rawStyle, "Cozy", StringComparison.OrdinalIgnoreCase);
        var legacyBold = string.Equals(rawStyle, "Bold & Easy", StringComparison.OrdinalIgnoreCase);

        string style;
        if (string.Equals(rawStyle, "Custom", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(p.CustomStyleNotes))
        {
            // There is no separate SOFT "style note" layer anymore: the user's Custom text IS the HARD style authority.
            style = p.CustomStyleNotes.Trim();
        }
        else if (CustomStyleLibraryService.TryResolve(rawStyle, out var libraryDefinition))
        {
            style = libraryDefinition;
        }
        else
        {
            style = rawStyle switch
            {
                "" => "Clean Line Art",
                var s when string.Equals(s, "Cozy", StringComparison.OrdinalIgnoreCase) => "Clean Line Art",
                var s when string.Equals(s, "Bold & Easy", StringComparison.OrdinalIgnoreCase) => "Clean Line Art",
                var s when string.Equals(s, "Kawaii / Cartoon", StringComparison.OrdinalIgnoreCase) => "Kawaii",
                _ => BookTypePromptProfileService.NormalizeColoringStyle(rawStyle)
            };
        }

        if (string.Equals(style, "Custom", StringComparison.OrdinalIgnoreCase))
            style = "Clean Line Art"; // Empty Custom cannot become an undefined renderer style.

        var bold = ColoringBoldEasyPolicyStore.Resolve(project, p.LineWeight, p.BoldEasy || legacyBold);
        var cozy = ColoringCozyPolicyStore.Resolve(project, legacyCozy);
        return new ColoringIndependentHardProfile(style, p.LineWeight, bold, cozy);
    }

    public static string BoldEasyDirective(bool enabled) => enabled
        ? "BOLD & EASY — HARD: ON. Use large simple readable forms, low visual clutter, broad colorable regions, restrained interior detail and confident easy-to-follow contours. Do not return a dense intricate page that merely has thick outlines."
        : "BOLD & EASY — HARD: OFF. Do not impose a Bold & Easy simplification profile. Do not automatically enlarge or oversimplify forms, reduce detail, or thicken contours to make the page Bold & Easy; obey the selected style, line weight, complexity and density exactly.";

    public static string CozyDirective(bool enabled) => enabled
        ? "COZY — HARD: ON. The finished page must visibly communicate a warm, comforting, gentle, inviting mood through friendly staging, soft approachable shape language and calm supportive details. Do not substitute a cold, harsh, threatening, clinical or documentary mood."
        : "COZY — HARD: OFF. Do not impose a Cozy mood or automatically add comforting domestic cues, soft cozy staging, warm decorative motifs or a gentle homelike atmosphere. Follow the selected visual style and requested scene without turning the page Cozy unless another explicit requirement independently demands a similar trait.";

    public static void PersistResolvedState(PreviewProject project, string? selectedStyle, string? lineWeight, bool boldEasy, bool cozy)
    {
        var p = BookTypePromptProfileService.LoadColoring(project);
        var style = (selectedStyle ?? string.Empty).Trim();
        if (CustomStyleLibraryService.TryResolve(style, out var libraryDefinition))
        {
            p.Style = "Custom";
            p.CustomStyleNotes = libraryDefinition;
        }
        else if (string.Equals(style, "Cozy", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(style, "Bold & Easy", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(style, "Kawaii / Cartoon", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(style, "Cozy", StringComparison.OrdinalIgnoreCase)) cozy = true;
            if (string.Equals(style, "Bold & Easy", StringComparison.OrdinalIgnoreCase)) boldEasy = true;
            style = string.Equals(style, "Kawaii / Cartoon", StringComparison.OrdinalIgnoreCase) ? "Kawaii" : "Clean Line Art";
            p.Style = BookTypePromptProfileService.NormalizeColoringStyle(style);
        }
        else
        {
            p.Style = BookTypePromptProfileService.NormalizeColoringStyle(style);
        }

        p.LineWeight = string.IsNullOrWhiteSpace(lineWeight) ? p.LineWeight : lineWeight.Trim();
        p.BoldEasy = BookTypePromptProfileService.IsThinLineWeight(p.LineWeight) ? false : boldEasy;
        BookTypePromptProfileService.SaveColoring(project, p);
        ColoringBoldEasyPolicyStore.Save(project, p.BoldEasy, p.LineWeight);
        ColoringCozyPolicyStore.Save(project, cozy);
    }
}
