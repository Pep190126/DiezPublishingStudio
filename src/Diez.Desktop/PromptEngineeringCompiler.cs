using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// Stable semantic prompt + provider-specific execution guidance.
/// Provider differences change execution strategy without weakening Book Type constraints.
/// v3.6 adds a deterministic creative-director synthesis layer: user choices are transformed into
/// visual hierarchy/composition/craft instructions before the canonical audit specification.
/// </summary>
internal static class PromptEngineeringCompiler
{
    public const string Version = "3.6";

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
        StructuredSubjectPromptRequestService.Apply(project, request);
        var canonical = PromptEngineeringEngine.RenderSeries(request);
        var sb = new StringBuilder();
        sb.AppendLine($"DIEZ PROVIDER COMPILER v{Version}");
        sb.AppendLine(ProviderProfile(request));
        sb.AppendLine();
        var acceptanceGate = PublicationAcceptanceGate(project, request);
        if (!string.IsNullOrWhiteSpace(acceptanceGate))
        {
            sb.AppendLine(acceptanceGate);
            sb.AppendLine();
        }

        sb.AppendLine("=== SYNTHESIZED CREATIVE DIRECTOR BRIEF ===");
        sb.AppendLine(VisualPromptIntentSynthesizer.BuildSeriesDirection(project, request));
        sb.AppendLine();

        if (string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            var hard = ColoringIndependentHardProfileService.Resolve(project);
            sb.AppendLine("=== COLORING INDEPENDENT HARD PROFILE LOCKS ===");
            sb.AppendLine($"STYLE — HARD LOCK: {hard.Style}. {BookTypePromptProfileService.StyleHardDirectiveEnglish(hard.Style)}");
            sb.AppendLine(ColoringIndependentHardProfileService.BoldEasyDirective(hard.BoldEasy));
            sb.AppendLine(ColoringIndependentHardProfileService.CozyDirective(hard.Cozy));
            sb.AppendLine($"LINE WEIGHT — HARD: {PromptEnglishNormalizer.NormalizeProviderFacing(hard.LineWeight)}. " +
                          (BookTypePromptProfileService.IsThinLineWeight(hard.LineWeight)
                              ? "Keep contours visibly thin/fine; never thicken them into a Bold & Easy treatment."
                              : "The visible contour weight must materially respect this selected value."));
            sb.AppendLine();
        }

        AppendStructuredSubjectContract(sb, project);
        AppendStructuredSceneContract(sb, project, request);

        sb.AppendLine("=== CANONICAL DIEZ PRODUCTION SPECIFICATION ===");
        sb.AppendLine(canonical.Trim());
        return PromptEnglishNormalizer.NormalizeProviderFacing(sb.ToString());
    }

    private static void AppendStructuredSubjectContract(StringBuilder sb, PreviewProject project)
    {
        var model = MultiSubjectProfileService.Load(project);
        var subjects = MultiSubjectProfileService.ActiveSubjects(model);
        if (!model.Enabled || subjects.Count == 0) return;

        sb.AppendLine("=== STRUCTURED SUBJECT / CHARACTER CAST — AUTHORITATIVE ===");
        sb.AppendLine($"Explicit multi-subject mode is active with {subjects.Count} subject{(subjects.Count == 1 ? string.Empty : "s")}. Each subject has a stable internal SubjectId; names may be edited without changing identity. SubjectIds are audit metadata and must never be drawn inside the artwork.");
        sb.AppendLine("Work Units are assigned to these structured identities by Diez. Do not reinterpret the cast as one comma-separated visual subject and do not render the whole cast unless the exact Work Unit explicitly calls for multiple participants.");
        for (var i = 0; i < subjects.Count; i++)
        {
            var subject = subjects[i];
            sb.AppendLine($"- Subject slot {i + 1}: {subject.Name}.");
            if (!string.IsNullOrWhiteSpace(subject.Description))
                sb.AppendLine($"  Identity definition — HARD: {subject.Description.Trim()}");
        }
        var group = PromptEnglishNormalizer.NormalizeProviderFacing(model.GroupDescription).Trim();
        if (!string.IsNullOrWhiteSpace(group))
            sb.AppendLine("- Shared theme/group context: " + group);
        sb.AppendLine();
    }

    private static void AppendStructuredSceneContract(StringBuilder sb, PreviewProject project, PromptEngineeringRequest request)
    {
        var model = StructuredSceneProfileService.Load(project);
        var scenes = StructuredSceneProfileService.ActiveScenes(model);
        if (!model.Enabled || scenes.Count == 0) return;

        sb.AppendLine("=== STRUCTURED SCENES — AUTHORITATIVE ===");
        var genericEnvironment = VisualPromptIntentSynthesizer.SeriesEnvironment(project, request.Environment);
        if (!string.IsNullOrWhiteSpace(genericEnvironment))
            sb.AppendLine("Series-level environment context: " + genericEnvironment.Trim());
        sb.AppendLine("Each scene has a stable internal SceneId. SceneIds and SubjectIds are audit metadata only and must never appear inside the artwork. Scene membership overrides any generic assumption about which cast members should appear together.");
        foreach (var scene in scenes)
        {
            var description = PromptEnglishNormalizer.NormalizeProviderFacing(scene.Description).Trim();
            var participants = StructuredSceneProfileService.Participants(project, scene);
            sb.Append("- Scene ").Append(scene.Number).Append(" — ").Append(scene.Name);
            if (!string.IsNullOrWhiteSpace(description)) sb.Append(": ").Append(description);
            sb.AppendLine();
            if (participants.Count > 0)
                sb.AppendLine("  Required participants — HARD: " + string.Join(", ", participants.Select(x => x.Name)) + ".");
        }
        sb.AppendLine();
    }

    private static string PublicationAcceptanceGate(PreviewProject project, PromptEngineeringRequest request)
    {
        if (!string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var hard = ColoringIndependentHardProfileService.Resolve(project);
        var selectedStyle = PromptEnglishNormalizer.NormalizeProviderFacing(hard.Style);
        var styleClause = string.IsNullOrWhiteSpace(selectedStyle)
            ? string.Empty
            : $"\n\nSELECTED STYLE — HARD: the finished image must visibly conform to the selected style '{selectedStyle}'. A technically polished image rendered in a materially different style is non-compliant. Personal taste between two professional executions INSIDE the selected style remains SOFT/REVIEW; failure to match the selected style itself is HARD.";

        return ("""
=== COLORING PUBLICATION ACCEPTANCE GATE — HARD BOOK-TYPE REQUIREMENT ===
This is a craft/readiness requirement, not a subjective style preference. The final asset is UNACCEPTABLE and must be regenerated if it visibly resembles a rough draft, scribble, tracing exercise, placeholder, preschool doodle, low-effort clipart/icon, or a subject assembled from crude geometric primitives instead of a deliberately resolved professional coloring-book illustration.

HARD rejection conditions include obvious unfinished or amateur execution such as incoherent/malformed anatomy, hesitant or arbitrary contours, primitive body construction, accidental-looking joins/overlaps, meaningless filler marks, unresolved composition, or line work so crude that the page does not look like a finished commercial asset.

Simplicity is NOT a failure. A page may use few elements, large shapes and reduced detail when its selected parameters call for that treatment, but the simplicity must look intentional, polished, balanced, expressive and professionally drawn. Child-friendly does not mean child-drawn.

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
        sb.AppendLine("1. Read the synthesized creative-director brief first and form a single visual plan before rendering.");
        sb.AppendLine("2. Use the canonical production specification as verification/constraint data; never visualize its bullets, labels or metadata as artwork.");
        sb.AppendLine("3. Treat hard Book-Type constraints, the explicitly selected visual style, independent HARD profile states and explicit exclusions literally; exercise visual creativity only inside the remaining freedom.");
        sb.AppendLine("4. For edits, use the supplied source image itself as visual authority and preserve unmentioned structure while applying requested changes.");
        sb.AppendLine("5. Inspect the final asset against the quality gate before returning it; do not describe a non-compliant asset as compliant.");
        sb.AppendLine("6. When generic and Work Unit-specific instructions differ, preserve the exact subject/scene identity and Work Unit-specific intent.");
        return sb.ToString().Trim();
    }

    private static string Gemini(PromptEngineeringRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PROVIDER EXECUTION PROFILE — GEMINI NATIVE IMAGE GENERATION");
        sb.AppendLine(r.PreferAdvancedModel
            ? "Prefer the highest-fidelity Gemini Native Image model available for professional asset production."
            : "Use the Gemini Native Image capability selected by the environment.");
        sb.AppendLine("1. Use the synthesized creative-director brief to plan ONE coherent scene concept before interpreting the canonical settings.");
        sb.AppendLine("2. Plan focal point, subject pose, required scene relationships, visual hierarchy and negative space before rendering, then enforce the hard technical, selected-style, independent-profile and Book-Type constraints.");
        sb.AppendLine("3. Priority: item/scene identity > hard Book-Type rules > selected style / independent HARD profile locks > modification rules > LOCKED consistency > PREFERRED consistency > creative freedom.");
        sb.AppendLine("4. One Work Unit requests one image only; never convert the series count or structured scene list into a grid, sheet or multiple alternatives.");
        sb.AppendLine("5. For edits with supplied images, preserve unmentioned visual structure and change only what Diez explicitly requests.");
        sb.AppendLine("6. Check that the final image actually depicts the requested subject and scene participants, visibly matches all HARD profile locks and obeys exclusions before returning it.");
        return sb.ToString().Trim();
    }

    private static string Other(PromptEngineeringRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PROVIDER EXECUTION PROFILE — OTHER / USER-SELECTED IMAGE MODEL");
        sb.AppendLine("Use the strongest appropriate generation/editing model available on the selected platform.");
        sb.AppendLine("1. Use the synthesized creative direction as the visual plan and the canonical specification as its constraint/audit layer.");
        sb.AppendLine("2. When the platform has a dedicated exclusions/negative field, map the negative constraints there while retaining essential prohibitions in the main request.");
        sb.AppendLine("3. When native aspect-ratio, size or quality controls exist, map Diez technical values to those controls instead of relying only on prose.");
        sb.AppendLine("4. When real image/reference inputs are supported, attach the actual Diez files rather than reconstructing them from descriptions.");
        sb.AppendLine("5. Generate exactly the count required by the current Work Unit; series count is context only.");
        sb.AppendLine("6. Prefer the platform's highest-fidelity mode when requested, without trading away Book-Type, selected-style, scene-membership or independent-profile hard constraints.");
        return sb.ToString().Trim();
    }

    private static string Generic(PromptEngineeringRequest r)
    {
        return """
PROVIDER EXECUTION PROFILE — MODEL-AGNOSTIC / GENERIC
1. Form the visual plan from the synthesized creative-director brief before reading the canonical settings as constraints.
2. Keep subject, scene relationships, environment, style and composition coherent rather than visualizing prompt fields literally.
3. The explicitly selected visual style, scene membership and each independent HARD profile state are editorial requirements; do not substitute or ignore them.
4. Keep all exclusions active even if the target model has no dedicated exclusions field.
5. Map aspect ratio, pixel size, quality and real image inputs to native provider controls whenever available.
6. One Diez Work Unit equals one requested final image unless the Work Unit explicitly says otherwise.
7. Perform a final compliance check against the Book-Type, scene/subject identity, selected-style and independent HARD profile quality gates before accepting the asset.
""".Trim();
    }
}
