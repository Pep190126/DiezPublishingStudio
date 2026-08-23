using System.Runtime.CompilerServices;
using System.Text.Json;
using DiezPublishingStudio;

internal static class Round51LegacyPromptMigrationRegression
{
    [ModuleInitializer]
    internal static void Run()
    {
        LegacyRequirementIsMigrated();
        Round5ExecutionMethodIsCompatible();
        RealMultiImageLayoutStillFails();
        VisualBridgeRejectsUnresolvedAggregateSubject();
    }

    private static void LegacyRequirementIsMigrated()
    {
        var source = """
Create ONE finished, publication-quality coloring-book illustration.
PRIMARY SUBJECT — HARD LOCK: friendly Halloween ghost.
- USER REQUIREMENT — HARD: 3 images: 1 per ogni soggetto; friendly smiling expression
STYLE — HARD LOCK: Cute & Playful.
COMPOSITION — HARD LOCK: exactly ONE unified continuous composition with one primary scene.
""";
        var prompt = PromptPackRendererVisualBriefService.Build(source);
        if (prompt.Contains("3 images", StringComparison.OrdinalIgnoreCase))
            throw new Exception("ROUND51_REGRESSION: legacy '3 images' survived renderer migration.");
        if (!prompt.Contains("friendly smiling expression", StringComparison.OrdinalIgnoreCase))
            throw new Exception("ROUND51_REGRESSION: meaningful sibling HARD requirement was lost.");
        if (!prompt.Contains("friendly Halloween ghost", StringComparison.OrdinalIgnoreCase))
            throw new Exception("ROUND51_REGRESSION: atomic subject was lost.");
        PromptPackRendererVisualBriefService.EnsureVisualOnly(prompt);
    }

    private static void Round5ExecutionMethodIsCompatible()
    {
        var source = """
Create ONE finished, publication-quality coloring-book illustration.
PRIMARY SUBJECT — HARD LOCK: cute black cat.
RENDERING METHOD — HARD: use a native generative IMAGE model/tool capable of producing finished illustration artwork. If native image generation is unavailable, return FAILED/INCOMPLETE instead of fabricating an approximate drawing.
QUALITY FIRST — HARD: create the page as a professional organic illustration first.
COLORING APPEARANCE — HARD: visually use black ink on a clean white background.
""";
        var prompt = PromptPackRendererVisualBriefService.Build(source);
        if (prompt.Contains("FAILED/INCOMPLETE", StringComparison.OrdinalIgnoreCase))
            throw new Exception("ROUND51_REGRESSION: transport status leaked into renderer brief.");
        if (!prompt.Contains("native generative image capability", StringComparison.OrdinalIgnoreCase))
            throw new Exception("ROUND51_REGRESSION: native image-generation requirement was lost.");
        PromptPackRendererVisualBriefService.EnsureVisualOnly(prompt);
    }

    private static void RealMultiImageLayoutStillFails()
    {
        var source = """
Create ONE finished, publication-quality coloring-book illustration.
PRIMARY SUBJECT — HARD LOCK: haunted house.
USER REQUIREMENT — HARD: arrange the result as a triptych composition.
""";
        try
        {
            _ = PromptPackRendererVisualBriefService.Build(source);
            throw new Exception("ROUND51_REGRESSION: true triptych layout contamination was accepted.");
        }
        catch (InvalidOperationException)
        {
            // Expected: migration must not weaken the genuine atomic-layout gate.
        }
    }

    private static void VisualBridgeRejectsUnresolvedAggregateSubject()
    {
        var project = new PreviewProject { Name = "Round51 regression" };
        BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
        VisualBookPlanService.Save(project, 3, false);
        var profile = BookTypePromptProfileService.LoadColoring(project);
        profile.SubjectDescription = "3 soggetti di Halloween";
        profile.EnvironmentDescription = "simple Halloween porch";
        profile.Style = "Cute & Playful";
        profile.BoldEasy = true;
        profile.LineWeight = "Thick";
        profile.ClosedAreas = true;
        profile.CleanContours = true;
        profile.NoTextInsideImage = true;
        profile.SubjectClearlySeparated = true;
        BookTypePromptProfileService.SaveColoring(project, profile);
        ColoringIndependentHardProfileService.PersistResolvedState(project, "Cute & Playful", "Thick", true, false);

        var json = JsonSerializer.Serialize(project);
        try
        {
            _ = DiezVisualBookFrontendBridge.BuildPromptPack(
                json,
                "3 images: 1 per ogni soggetto",
                string.Empty,
                PromptEngineeringProviderIds.Generic,
                true);
            throw new Exception("ROUND51_REGRESSION: unresolved aggregate subject was delegated to the renderer instead of being blocked by Diez.");
        }
        catch (InvalidOperationException ex)
        {
            if (!ex.Message.Contains("Piano soggetti non risolto", StringComparison.OrdinalIgnoreCase))
                throw new Exception("ROUND51_REGRESSION: aggregate subject was blocked for the wrong reason: " + ex.Message);
        }
    }
}
