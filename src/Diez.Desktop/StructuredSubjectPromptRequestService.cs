namespace DiezPublishingStudio;

/// <summary>
/// Reconciles the legacy single subject field with the structured multi-subject model. The reused native
/// TextBox may contain the currently selected subject description while Multi is ON; provider-facing master
/// prompts must instead treat GroupDescription as shared theme and stable SubjectIds as item identities.
/// </summary>
internal static class StructuredSubjectPromptRequestService
{
    public static void Apply(PreviewProject project, PromptEngineeringRequest request)
    {
        var model = MultiSubjectProfileService.Load(project);
        if (!model.Enabled || MultiSubjectProfileService.ActiveSubjects(model).Count == 0) return;

        request.Subject = PromptEnglishNormalizer.NormalizeProviderFacing(model.GroupDescription);
        foreach (var item in request.ItemOverrides)
            item.Subject = string.Empty; // Structured SubjectId assignment supersedes legacy text parsing.
    }
}
