namespace DiezPublishingStudio;

internal static class PromptManualReconciliationSelfTest
{
    public static void Run()
    {
        var project = ProjectFileStore.Create("Manual Prompt Reconciliation");
        BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
        var profile = BookTypePromptProfileService.LoadColoring(project);
        profile.SubjectDescription = "jungle animals";
        BookTypePromptProfileService.SaveColoring(project, profile);

        const int count = 3;
        const string mustDo = "friendly jungle animals";
        var baseline = PromptEngineeringCompiler.BuildSeriesPrompt(
            project, count, mustDo, string.Empty, PromptEngineeringProviderIds.OpenAi, true);
        PromptMasterStateStore.Save(project, new PromptMasterState
        {
            BookType = BookTypeProfileService.ColoringBook,
            ProviderId = PromptEngineeringProviderIds.OpenAi,
            PreferAdvancedModel = true,
            SeriesCount = count,
            MustDo = mustDo,
            Prompt = baseline
        });
        PromptMasterMetadataStore.MarkGenerated(
            project, count, mustDo, string.Empty, PromptEngineeringProviderIds.OpenAi, true);

        var manual = baseline + Environment.NewLine +
                     "USER CREATIVE NOTE: give the animal a curious, warm expression without adding decorative symbols.";
        PromptMasterStateStore.Save(project, new PromptMasterState
        {
            BookType = BookTypeProfileService.ColoringBook,
            ProviderId = PromptEngineeringProviderIds.OpenAi,
            PreferAdvancedModel = true,
            SeriesCount = count,
            MustDo = mustDo,
            Prompt = manual
        });
        PromptMasterMetadataStore.MarkManual(
            project, count, mustDo, string.Empty, PromptEngineeringProviderIds.OpenAi, true);
        var metadata = PromptMasterMetadataStore.Load(project);
        Require(metadata?.ManualOverride == true, "La modifica manuale non viene marcata.");
        Require(PromptMasterMetadataStore.MatchesCurrent(
            project, metadata, count, mustDo, string.Empty, PromptEngineeringProviderIds.OpenAi, true),
            "Il prompt manuale corrente non corrisponde al fingerprint dei parametri.");

        var delta = PromptMasterMetadataStore.ExtractManualDelta(metadata, manual);
        Require(delta.Contains("USER CREATIVE NOTE", StringComparison.Ordinal), "La riga manuale aggiunta non viene estratta.");
        Require(!delta.Contains("COMMERCIAL COLORING BOOK", StringComparison.Ordinal), "La baseline generata viene duplicata nel delta manuale.");

        profile.SubjectDescription = "jungle animals in movement";
        BookTypePromptProfileService.SaveColoring(project, profile);
        Require(!PromptMasterMetadataStore.MatchesCurrent(
            project, metadata, count, mustDo, string.Empty, PromptEngineeringProviderIds.OpenAi, true),
            "Il fingerprint non diventa stale dopo una modifica strutturata.");
        var deltaAfterChange = PromptMasterMetadataStore.ExtractManualDelta(metadata, manual);
        Require(string.Equals(deltaAfterChange.Trim(), delta.Trim(), StringComparison.Ordinal),
            "La modifica strutturata altera il delta manuale preservato.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("PROMPT MANUAL RECONCILIATION SELF-TEST: " + message);
    }
}
