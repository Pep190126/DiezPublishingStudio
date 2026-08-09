namespace DiezPublishingStudio;

internal static class AiExchangeImportPipeline
{
    public static async Task<AiExchangeImportSummary> ImportAsync(
        PreviewProject project,
        string projectPath,
        AiExchangeState state,
        IEnumerable<string> zipPaths)
    {
        var result = await AiExchangeResponseImporter.ImportAsync(project, projectPath, state, zipPaths);
        var promoted = ReconcileCompletedCandidates(state);
        if (promoted > 0)
        {
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(projectPath, project);
        }

        var incomplete = Math.Max(0, result.Incomplete - promoted);
        var imported = result.Imported + promoted;
        return new AiExchangeImportSummary(
            result.Success || promoted > 0,
            imported,
            incomplete,
            result.Duplicates,
            result.Conflicts,
            result.Failed,
            $"Import AI: {imported} pronti/aggiornati · {incomplete} incompleti · {result.Duplicates} duplicati · {result.Conflicts} conflitti · {result.Failed} errori.");
    }

    internal static int ReconcileCompletedCandidates(AiExchangeState state)
    {
        var promoted = 0;
        foreach (var version in state.Versions.Where(v => v.Status == AiExchangeVersionStatuses.Incomplete))
        {
            var unit = state.WorkUnits.FirstOrDefault(w => w.WorkUnitId == version.WorkUnitId);
            if (unit is null) continue;
            var hasPrimary = version.MaterialId.HasValue || !string.IsNullOrWhiteSpace(version.TextContent);
            if (!hasPrimary) continue;
            if (string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(version.Description)) continue;
                version.DescriptionStatus = AiExchangeDescriptionStatuses.Valid;
            }
            version.Status = AiExchangeVersionStatuses.Candidate;
            promoted++;
        }
        return promoted;
    }
}
