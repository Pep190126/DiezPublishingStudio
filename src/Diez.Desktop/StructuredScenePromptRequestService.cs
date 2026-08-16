namespace DiezPublishingStudio;

/// <summary>
/// Keeps series-level environment context separate from scene-local text after the native Environment editor
/// has been switched into scene-definition mode. Scene intent is injected by SceneId elsewhere; the request's
/// Environment must therefore remain the generic series environment and never become the currently edited scene.
/// </summary>
internal static class StructuredScenePromptRequestService
{
    public static void Apply(PreviewProject project, PromptEngineeringRequest request)
    {
        var scenes = StructuredSceneProfileService.Load(project);
        if (!scenes.Enabled || StructuredSceneProfileService.ActiveScenes(scenes).Count == 0) return;

        request.Environment = StructuredSceneEnvironmentStore.Load(project, request.Environment).Trim();

        // Item-level environment overrides belong to the old free-text environment parser. Structured scenes
        // already provide exact per-Work-Unit scene intent, so stale parsed environment snippets must not compete.
        foreach (var item in request.ItemOverrides)
            item.Environment = string.Empty;
    }
}
