using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// Converts structured user choices into visual art direction instead of forwarding a flat settings list.
/// The compiler remains deterministic/auditable: it derives hierarchy, composition and craft instructions
/// from the selected Book Type, audience, difficulty, style and scene structure without inventing new facts.
/// </summary>
internal static class VisualPromptIntentSynthesizer
{
    public static string BuildSeriesDirection(PreviewProject project, PromptEngineeringRequest request)
    {
        var subject = Clean(request.Subject, "the requested subject");
        var environment = Clean(SeriesEnvironment(project, request.Environment), string.Empty);
        var isColoring = string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase);
        var style = isColoring
            ? Clean(ColoringIndependentHardProfileService.Resolve(project).Style, "Clean Line Art")
            : Clean(request.RenderingStyle, "professional illustration");

        var sb = new StringBuilder();
        sb.Append("SYNTHESIZED CREATIVE DIRECTION: ");
        if (isColoring)
        {
            var hard = ColoringIndependentHardProfileService.Resolve(project);
            sb.Append("Design a commercially publishable ").Append(style)
              .Append(" coloring-book series centered on ").Append(subject).Append(". ")
              .Append(AudienceDirection(request.Audience, request.Difficulty)).Append(' ')
              .Append(LineAndDetailDirection(hard.LineWeight, request.Complexity, request.Density, hard.BoldEasy)).Append(' ')
              .Append(BackgroundDirection(environment, request.Background)).Append(' ')
              .Append(hard.Cozy
                  ? "Keep the emotional read warm, gentle and inviting without sacrificing the selected visual style. "
                  : "Do not add a cozy/domestic mood unless the actual content requires it. ")
              .Append("Treat each Work Unit as one resolved page, not as a visual representation of the series or of the settings list.");
        }
        else
        {
            sb.Append("Create a coherent professional ").Append(style)
              .Append(" image series centered on ").Append(subject).Append(". ")
              .Append("Use ").Append(Clean(request.ColorMode, "the selected color treatment"))
              .Append(" with ").Append(Clean(request.DetailLevel, "medium"))
              .Append(" detail, and make the subject readable before secondary scenery. ")
              .Append(BackgroundDirection(environment, request.Background)).Append(' ')
              .Append("Each Work Unit must resolve into one deliberate composition with a clear focal hierarchy rather than a literal collage of prompt fields.");
        }
        return PromptEnglishNormalizer.NormalizeProviderFacing(sb.ToString()).Trim();
    }

    public static string BuildWorkUnitDirection(
        PreviewProject project,
        PromptEngineeringRequest request,
        string subject,
        StructuredSceneDefinition? scene,
        IReadOnlyList<MultiSubjectDefinition> participants)
    {
        var isColoring = string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase);
        var hard = isColoring ? ColoringIndependentHardProfileService.Resolve(project) : null;
        var style = isColoring
            ? Clean(hard!.Style, "Clean Line Art")
            : Clean(request.RenderingStyle, "professional illustration");
        var genericEnvironment = Clean(SeriesEnvironment(project, request.Environment), string.Empty);
        var sceneDescription = Clean(scene?.Description, string.Empty);
        var participantNames = participants.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

        var sb = new StringBuilder();
        sb.Append("ART DIRECTION — SYNTHESIZED: Build one ").Append(style).Append(" composition around ")
          .Append(Clean(subject, "the requested focal subject")).Append(" as the immediate focal read. ");

        if (!string.IsNullOrWhiteSpace(sceneDescription))
            sb.Append("Stage the action/relationship as follows: ").Append(sceneDescription).Append(". ");
        if (participantNames.Length > 1)
            sb.Append("Keep ").Append(string.Join(", ", participantNames))
              .Append(" together inside the same continuous scene, with the focal subject visually dominant and the other required participants clearly present but secondary. ");
        else if (participantNames.Length == 1)
            sb.Append("Ensure ").Append(participantNames[0]).Append(" is visibly present in the same continuous scene. ");

        if (!string.IsNullOrWhiteSpace(genericEnvironment))
            sb.Append("Use the series environment as supporting context — ").Append(genericEnvironment)
              .Append(" — but let the current scene action determine the local staging. ");

        if (isColoring)
        {
            sb.Append(AudienceDirection(request.Audience, request.Difficulty)).Append(' ')
              .Append(LineAndDetailDirection(hard!.LineWeight, request.Complexity, request.Density, hard.BoldEasy)).Append(' ')
              .Append(BackgroundDirection(string.Empty, request.Background));
            if (hard.Cozy)
                sb.Append(" The mood must read warm and comforting while remaining visibly ").Append(style).Append('.');
        }
        else
        {
            sb.Append("Use ").Append(Clean(request.DetailLevel, "medium"))
              .Append(" detail and ").Append(Clean(request.ColorMode, "the selected color treatment"))
              .Append("; resolve pose, camera/viewpoint and negative space in service of subject readability, not as separate checklist items.");
        }

        sb.Append(" If any generic choice conflicts with this exact Work Unit's subject, scene membership or explicit HARD instruction, the Work Unit-specific instruction wins.");
        return PromptEnglishNormalizer.NormalizeProviderFacing(sb.ToString()).Trim();
    }

    public static string SeriesEnvironment(PreviewProject project, string? fallback)
    {
        var scenes = StructuredSceneProfileService.Load(project);
        if (!scenes.Enabled) return fallback ?? string.Empty;
        return StructuredSceneEnvironmentStore.Load(project, fallback ?? string.Empty);
    }

    private static string AudienceDirection(string? audience, string? difficulty)
    {
        var a = Clean(audience, "the selected audience");
        var d = Clean(difficulty, "medium difficulty");
        return $"Shape the page for {a} at {d}: choose subject scale, spacing and visual complexity so the image reads clearly at publication size rather than merely satisfying the nominal settings.";
    }

    private static string LineAndDetailDirection(string? lineWeight, string? complexity, string? density, bool boldEasy)
    {
        var line = Clean(lineWeight, "medium");
        var complexityText = Clean(complexity, "medium");
        var densityText = Clean(density, "low to medium");
        if (boldEasy)
            return $"Use {line} contours with intentionally large readable forms, broad colorable regions and restrained interior detail; translate {complexityText} complexity and {densityText} density into clear grouped shapes rather than clutter.";
        return $"Use {line} contours and translate {complexityText} complexity with {densityText} element density into intentional visual rhythm; preserve the selected detail level without automatic Bold & Easy simplification.";
    }

    private static string BackgroundDirection(string? environment, string? background)
    {
        var mode = Clean(background, "a supporting contextual background");
        if (string.IsNullOrWhiteSpace(environment))
            return $"Treat the background as {mode}: it should reinforce depth and context while remaining subordinate to the focal subject.";
        return $"Stage the environment '{environment}' using {mode}; include only elements that clarify place, action or mood and remove filler that competes with the focal subject.";
    }

    private static string Clean(string? value, string fallback)
    {
        var normalized = PromptEnglishNormalizer.NormalizeProviderFacing(value).Trim();
        return normalized.Length == 0 ? fallback : normalized;
    }
}
