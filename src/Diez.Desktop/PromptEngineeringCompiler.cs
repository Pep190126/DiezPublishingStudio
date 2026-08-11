using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// Stable semantic prompt + provider-specific execution guidance.
/// Provider differences change execution strategy without weakening Book Type constraints.
/// </summary>
internal static class PromptEngineeringCompiler
{
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
        return ProviderProfile(request) + Environment.NewLine + Environment.NewLine +
               "=== CANONICAL DIEZ PRODUCTION SPECIFICATION ===" + Environment.NewLine + canonical.Trim();
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
        sb.AppendLine("3. Treat hard Book-Type constraints and explicit exclusions literally; exercise visual creativity only inside the remaining freedom.");
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
        sb.AppendLine("2. Plan focal point, subject pose, scene relationships and composition before rendering, then enforce the hard technical and Book-Type constraints.");
        sb.AppendLine("3. Priority: item instruction > hard Book-Type rules > modification rules > LOCKED consistency > PREFERRED consistency > style preferences > creative freedom.");
        sb.AppendLine("4. One Work Unit requests one image only; never convert the series count into a grid, sheet or multiple alternatives.");
        sb.AppendLine("5. For edits with supplied images, preserve unmentioned visual structure and change only what Diez explicitly requests.");
        sb.AppendLine("6. Check that the final image actually depicts the requested subject and obeys exclusions before returning it.");
        return sb.ToString().Trim();
    }

    private static string Other(PromptEngineeringRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PROVIDER EXECUTION PROFILE — OTHER / USER-SELECTED IMAGE MODEL");
        sb.AppendLine("Use the strongest appropriate generation/editing model available on the selected platform.");
        sb.AppendLine("1. Preserve the canonical Diez specification; do not shorten it by deleting hard constraints, exclusions, item overrides or consistency rules.");
        sb.AppendLine("2. When the platform has a dedicated exclusions/negative field, map the negative constraints there while retaining essential prohibitions in the main request.");
        sb.AppendLine("3. When native aspect-ratio, size or quality controls exist, map Diez technical values to those controls instead of relying only on prose.");
        sb.AppendLine("4. When real image/reference inputs are supported, attach the actual Diez files rather than reconstructing them from descriptions.");
        sb.AppendLine("5. Generate exactly the count required by the current Work Unit; series count is context only.");
        sb.AppendLine("6. Prefer the platform's highest-fidelity mode when requested, without trading away Book-Type hard constraints.");
        return sb.ToString().Trim();
    }

    private static string Generic(PromptEngineeringRequest r)
    {
        return """
PROVIDER EXECUTION PROFILE — MODEL-AGNOSTIC / GENERIC
1. Resolve the semantic scene first, then enforce hard visual and technical constraints.
2. Keep subject, environment, style and composition coherent rather than visualizing prompt bullets literally.
3. Keep all exclusions active even if the target model has no dedicated exclusions field.
4. Map aspect ratio, pixel size, quality and real image inputs to native provider controls whenever available.
5. One Diez Work Unit equals one requested final image unless the Work Unit explicitly says otherwise.
6. Perform a final compliance check against the Book-Type quality gate before accepting the asset.
""".Trim();
    }
}
