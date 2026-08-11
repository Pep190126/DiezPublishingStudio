namespace DiezPublishingStudio;

/// <summary>
/// Single source-level pipeline for every visual Prompt Pack, including initial generation and
/// local corrections. UI code never assembles/enriches/finalizes ZIPs on its own.
/// </summary>
internal static class AiVisualPromptPackService
{
    internal readonly record struct BuildResult(
        bool Success,
        string Message,
        Guid PromptPackId,
        int WorkUnitCount,
        int IntakeImages,
        int BaseImages);

    public static async Task<BuildResult> BuildAsync(
        PreviewProject project,
        string projectPath,
        AiExchangeState state,
        IEnumerable<Guid> workUnitIds,
        string targetPath)
    {
        var ids = workUnitIds.Distinct().ToList();
        if (ids.Count == 0)
            return new BuildResult(false, "Nessuna Work Unit visuale selezionata.", Guid.Empty, 0, 0, 0);

        var units = state.WorkUnits.Where(u => ids.Contains(u.WorkUnitId)).ToList();
        if (units.Count != ids.Count)
            return new BuildResult(false, "Una o più Work Unit visuali non appartengono allo stato AI corrente.", Guid.Empty, units.Count, 0, 0);
        if (units.Any(u => !string.Equals(u.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase)))
            return new BuildResult(false, "Il Prompt Pack visuale può contenere solo Work Unit immagine.", Guid.Empty, units.Count, 0, 0);

        var built = await AiExchangePromptPackBuilder.BuildAsync(project, projectPath, state, ids, targetPath);
        if (!built.Success)
            return new BuildResult(false, built.Message, built.PromptPackId, units.Count, 0, 0);

        var enhanced = await AiExchangeImageRequestContextSafeEnhancer.EnhancePromptPackAsync(
            project, projectPath, state, ids, targetPath);
        if (!enhanced.Success)
            return new BuildResult(false,
                built.Message + " · Contesto visuale incompleto: " + enhanced.Message,
                built.PromptPackId, units.Count, enhanced.IntakeImages, enhanced.BaseImages);

        PromptPackPromptEngineeringFinalizer.Finalize(targetPath, project, state, ids);
        AiExchangeVisualLayoutSanitizer.Sanitize(targetPath);
        AiExchangeStateStore.Save(project, state);
        await ProjectFileStore.SaveAsync(projectPath, project);

        return new BuildResult(
            true,
            $"Prompt Pack pronto: {units.Count} Work Unit · 1 immagine per Work Unit · profilo {BookTypeProfileService.Get(project)} isolato · prompt engine v{PromptEngineeringEngine.EngineVersion}.",
            built.PromptPackId,
            units.Count,
            enhanced.IntakeImages,
            enhanced.BaseImages);
    }
}
