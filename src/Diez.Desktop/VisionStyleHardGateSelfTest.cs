namespace DiezPublishingStudio;

internal static class VisionStyleHardGateSelfTest
{
    public static void Run()
    {
        var project = ProjectFileStore.Create("Vision Kawaii Style Regression");
        BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
        var profile = BookTypePromptProfileService.LoadColoring(project);
        profile.SubjectDescription = "3 animali diversi 3 immagini";
        profile.EnvironmentDescription = "jungla";
        profile.Style = "Kawaii / Cartoon";
        profile.TargetAudience = "Bambini 6–9 anni";
        profile.Difficulty = "Facile";
        profile.LineWeight = "Spesso — Bold";
        profile.Complexity = "Bassa";
        profile.ElementDensity = "Bassa";
        profile.Background = "Contestuale leggero";
        BookTypePromptProfileService.SaveColoring(project, profile);

        const string mustDo = "3 immagini di animali della jungla, riempi lo sfondo con ambientazione jungla";
        PromptPreparationSettingsStore.Save(project, new PromptPreparationSettings
        {
            ProviderId = PromptEngineeringProviderIds.OpenAi,
            PreferAdvancedModel = true
        });
        PromptMasterStateStore.Save(project, new PromptMasterState
        {
            BookType = BookTypeProfileService.ColoringBook,
            ProviderId = PromptEngineeringProviderIds.OpenAi,
            PreferAdvancedModel = true,
            SeriesCount = 3,
            MustDo = mustDo,
            Prompt = PromptEngineeringCompiler.BuildSeriesPrompt(
                project, 3, mustDo, string.Empty, PromptEngineeringProviderIds.OpenAi, true)
        });

        var unit = new AiExchangeWorkUnit
        {
            WorkUnitId = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            Code = "IMG-001",
            ContentType = AiExchangeContentTypes.Image,
            Mode = AiExchangeModes.AiOnly,
            Position = 1
        };
        var version = new AiExchangeVersion
        {
            VersionId = Guid.NewGuid(),
            WorkUnitId = unit.WorkUnitId,
            VersionNumber = 1,
            Status = AiExchangeVersionStatuses.Candidate,
            Description = "A black-and-white animal illustration.",
            DescriptionStatus = AiExchangeDescriptionStatuses.Valid,
            ContentSha256 = new string('a', 64)
        };
        var state = new AiExchangeState
        {
            WorkUnits = [unit],
            Versions = [version]
        };

        var request = VisionValidationSpecificationBuilder.Build(
            project,
            state,
            unit,
            version,
            Guid.NewGuid(),
            "candidate.png",
            3,
            PromptEngineeringProviderIds.OpenAi);

        Require(request.Expected.ItemSubject.Equals("one monkey", StringComparison.OrdinalIgnoreCase),
            "Vision continua a usare il soggetto di serie invece del soggetto atomico IMG-001: " + request.Expected.ItemSubject);
        Require(request.GenerationContract.Contains("STYLE — HARD LOCK: Kawaii / Cartoon", StringComparison.OrdinalIgnoreCase),
            "Generation contract Vision non contiene STYLE hard lock Kawaii.");
        Require(request.GenerationContract.Contains("COMPOSITION — HARD LOCK", StringComparison.Ordinal),
            "Generation contract Vision non contiene composizione singola HARD.");
        Require(request.GenerationContract.Contains("engraving", StringComparison.OrdinalIgnoreCase) &&
                request.GenerationContract.Contains("cross-hatching", StringComparison.OrdinalIgnoreCase),
            "Il Kawaii gate non vieta la resa realistica/incisione osservata nella prova fisica.");
        Require(request.Expected.HardCriteria.Any(c => c.Contains("STYLE MATCH IS HARD", StringComparison.OrdinalIgnoreCase)),
            "Expected Vision non classifica lo style match come HARD.");
        Require(request.Expected.HardCriteria.Any(c => c.Contains("triptych", StringComparison.OrdinalIgnoreCase)),
            "Expected Vision non classifica il triptych come HARD.");

        // Simulate a validator that correctly notices the mismatch but incorrectly labels it SOFT.
        // Diez must enforce the project policy and promote the semantic mismatch to HARD/FAIL.
        VisionValidationStore.Apply(project, state, new VisionValidationResult
        {
            VersionId = version.VersionId,
            WorkUnitId = unit.WorkUnitId,
            CandidateVersion = 1,
            ContentSha256 = version.ContentSha256,
            ProviderId = "self-test",
            OverallStatus = VisionValidationStatuses.Review,
            Confidence = 0.99,
            ObservedDescription = "A realistic natural-history engraving of a monkey, tiger and elephant in three panels.",
            Summary = "Subject family is recognizable but style and composition are wrong.",
            Checks =
            [
                new VisionValidationCheck
                {
                    Key = "style_match",
                    Status = VisionCheckStatuses.Fail,
                    Severity = VisionSeverity.Soft,
                    Confidence = 0.99,
                    Evidence = "Realistic engraved anatomy and dense hatching; not Kawaii / Cartoon."
                },
                new VisionValidationCheck
                {
                    Key = "single_composition",
                    Status = VisionCheckStatuses.Fail,
                    Severity = VisionSeverity.Soft,
                    Confidence = 0.99,
                    Evidence = "Three separate bordered panels are visible."
                }
            ]
        });

        var stored = VisionValidationStore.Get(project, version.VersionId);
        Require(stored is not null && stored.BlocksApproval, "Style/composition mismatch non blocca l'approvazione.");
        Require(stored!.OverallStatus == VisionValidationStatuses.Fail, "HARD style/composition mismatch non forza overall FAIL.");
        Require(stored.Checks.Where(c => c.Key is "style_match" or "single_composition").All(c => c.Severity == VisionSeverity.Hard),
            "Diez non promuove style_match/single_composition a HARD.");
        Require(version.Status == AiExchangeVersionStatuses.Incomplete,
            "Candidate con style/composition HARD FAIL non viene bloccata come INCOMPLETE.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("VISION STYLE HARD GATE SELF-TEST: " + message);
    }
}
