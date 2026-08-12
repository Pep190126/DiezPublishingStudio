using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// Stable semantic prompt + provider-specific execution guidance.
/// Provider differences change execution strategy without weakening Book Type constraints.
/// </summary>
internal static class PromptEngineeringCompiler
{
    public const string Version = "3.4";

    public static string BuildSeriesPrompt(
        PreviewProject project,
        int count,
        string? mustDo,
        string? mustNotDo,
        string? providerId,
        bool preferAdvancedModel)
    {
        var request = PromptEngineeringEngine.BuildRequest(
            project, count, mustDo, mustNotDo, providerId, preferAdvancedModel);
        var canonical = PromptEngineeringEngine.RenderSeries(request);
        var sb = new StringBuilder();
        sb.AppendLine($"DIEZ PROVIDER COMPILER v{Version}");
        sb.AppendLine(ProviderProfile(request));
        sb.AppendLine();
        var acceptanceGate = PublicationAcceptanceGate(request);
        if (!string.IsNullOrWhiteSpace(acceptanceGate))
        {
            sb.AppendLine(acceptanceGate);
            sb.AppendLine();
        }
        sb.AppendLine("=== CANONICAL DIEZ PRODUCTION SPECIFICATION ===");
        sb.AppendLine(canonical.Trim());
        return PromptEnglishNormalizer.NormalizeProviderFacing(sb.ToString());
    }

    private static string PublicationAcceptanceGate(PromptEngineeringRequest request)
    {
        if (!string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var selectedStyle = PromptEnglishNormalizer.NormalizeProviderFacing(request.Style);
        var styleClause = string.IsNullOrWhiteSpace(selectedStyle)
            ? string.Empty
            : $"\n\nSELECTED STYLE — HARD: the finished image must visibly conform to the selected style '{selectedStyle}'. A technically polished image rendered in a materially different style is non-compliant. Personal taste between two professional executions INSIDE the selected style remains SOFT/REVIEW; failure to match the selected style itself is HARD.";

        return ("""
=== COLORING PUBLICATION ACCEPTANCE GATE — HARD BOOK-TYPE REQUIREMENT ===
This is a craft/readiness requirement, not a subjective style preference. The final asset is UNACCEPTABLE and must be regenerated if it visibly resembles a rough draft, scribble, tracing exercise, placeholder, preschool doodle, low-effort clipart/icon, or a subject assembled from crude geometric primitives instead of a deliberately resolved professional coloring-book illustration.

HARD rejection conditions include obvious unfinished or amateur execution such as incoherent/malformed anatomy, hesitant or arbitrary contours, primitive body construction, accidental-looking joins/overlaps, meaningless filler marks, unresolved composition, or line work so crude that the page does not look like a finished commercial asset.

Simplicity is NOT a failure. A Preschool or Bold & Easy page may use few elements, large shapes and reduced detail, but that simplicity must look intentional, polished, balanced, expressive and professionally drawn. Child-friendly does not mean child-drawn.

Before returning the asset, perform this publication test: could this plausibly appear as a finished page in a professionally published commercial coloring book for the selected audience without an illustrator having to redraw it? If the answer is no, regenerate before returning.

Independent QA instruction: an obvious failure of this craft/readiness requirement must be reported as book_type_fit = FAIL/HARD even when deterministic raster checks such as dimensions, DPI and pure black/white all pass.
""" + styleClause).Trim();
    }

    private static string ProviderProfile(PromptEngineeringRequest request) => request.ProviderId switch
    {
        PromptEngineeringProviderIds.OpenAi => OpenAi(request),
        PromptEngineeringProviderIds.Gemini => Gemini(request),
        PromptEngineeringProviderIds.Other => Other(request),
        _ => Generic(request)
    };

    private static string OpenAi(PromptEngineeringRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PROVIDER EXECUTION PROFILE — OPENAI IMAGE GENERATION");
        sb.AppendLine(r.PreferAdvancedModel
            ? "Prefer GPT Image 2 or the current higher-quality successor available in the OpenAI environment."
            : "Use the OpenAI image-generation capability selected by the environment.");
        sb.AppendLine("1. Read the complete production specification before rendering and resolve instruction priority first.");
        sb.AppendLine("2. Convert subject, environment and style into one coherent visual composition; never turn production bullets into visible labels, badges or decorative marks.");
        sb.AppendLine("3. Treat hard Book-Type constraints, the explicitly selected visual style and explicit exclusions literally; exercise visual creativity only inside the remaining freedom.");
        sb.AppendLine("4. For edits, use the supplied source image itself as visual authority and preserve unmentioned structure while applying requested changes.");
        sb.AppendLine("5. Inspect the final asset against the quality gate before returning it; do not describe a non-compliant asset as compliant.");
        sb.AppendLine("6. Keep the instruction hierarchy precise and avoid inventing alternate interpretations of explicit technical values.");
        return sb.ToString().Trim();
    }

    private static string Gemini(PromptEngineeringRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PROVIDER EXECUTION PROFILE — GEMINI NATIVE IMAGE GENERATION");
        sb.AppendLine(r.PreferAdvancedModel
            ? "Prefer the highest-fidelity Gemini Native Image model available for professional asset production."
            : "Use the Gemini Native Image capability selected by the environment.");
        sb.AppendLine("1. First synthesize subject + context/background + visual style into ONE coherent scene concept; do not treat the brief as disconnected keywords.");
        sb.AppendLine("2. Plan focal point, subject pose, scene relationships and composition before rendering, then enforce the hard technical, selected-style and Book-Type constraints.");
        sb.AppendLine("3. Priority: item instruction > hard Book-Type rules > selected style hard lock > modification rules > LOCKED consistency > PREFERRED consistency > creative freedom.");
        sb.AppendLine("4. One Work Unit requests one image only; never convert the series count into a grid, sheet or multiple alternatives.");
        sb.AppendLine("5. For edits with supplied images, preserve unmentioned visual structure and change only what Diez explicitly requests.");
        sb.AppendLine("6. Check that the final image actually depicts the requested subject, visibly matches the selected style and obeys exclusions before returning it.");
        return sb.ToString().Trim();
    }

    private static string Other(PromptEngineeringRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PROVIDER EXECUTION PROFILE — OTHER / USER-SELECTED IMAGE MODEL");
        sb.AppendLine("Use the strongest appropriate generation/editing model available on the selected platform.");
        sb.AppendLine("1. Preserve the canonical Diez specification; do not shorten it by deleting hard constraints, selected style, exclusions, item overrides or consistency rules.");
        sb.AppendLine("2. When the platform has a dedicated exclusions/negative field, map the negative constraints there while retaining essential prohibitions in the main request.");
        sb.AppendLine("3. When native aspect-ratio, size or quality controls exist, map Diez technical values to those controls instead of relying only on prose.");
        sb.AppendLine("4. When real image/reference inputs are supported, attach the actual Diez files rather than reconstructing them from descriptions.");
        sb.AppendLine("5. Generate exactly the count required by the current Work Unit; series count is context only.");
        sb.AppendLine("6. Prefer the platform's highest-fidelity mode when requested, without trading away Book-Type or selected-style hard constraints.");
        return sb.ToString().Trim();
    }

    private static string Generic(PromptEngineeringRequest r)
    {
        return """
PROVIDER EXECUTION PROFILE — MODEL-AGNOSTIC / GENERIC
1. Resolve the semantic scene first, then enforce hard visual and technical constraints.
2. Keep subject, environment, style and composition coherent rather than visualizing prompt bullets literally.
3. The explicitly selected visual style is a hard editorial requirement; do not substitute a different professional style.
4. Keep all exclusions active even if the target model has no dedicated exclusions field.
5. Map aspect ratio, pixel size, quality and real image inputs to native provider controls whenever available.
6. One Diez Work Unit equals one requested final image unless the Work Unit explicitly says otherwise.
7. Perform a final compliance check against the Book-Type and selected-style quality gates before accepting the asset.
""".Trim();
    }
}
