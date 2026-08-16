namespace DiezPublishingStudio;

internal static class VisionStyleHardGateSelfTest
{
    public static void Run()
    {
        var project = ProjectFileStore.Create("Vision Coloring HARD Regression");
        BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
        var profile = BookTypePromptProfileService.LoadColoring(project);
        profile.SubjectDescription = "3 animali diversi 3 immagini";
        profile.EnvironmentDescription = "jungla";
        profile.Style = "Kawaii";
        profile.TargetAudience = "Bambini 6–9 anni";
        profile.Difficulty = "Facile";
        profile.LineWeight = "Sottile — Fine";
        profile.BoldEasy = true; // normalization/policy must force this OFF because line weight is thin.
        profile.Complexity = "Media";
        profile.ElementDensity = "Media";
        profile.Background = "Contestuale leggero";
        BookTypePromptProfileService.SaveColoring(project, profile);
        ColoringBoldEasyPolicyStore.Save(project, true, profile.LineWeight);
        ColoringCozyPolicyStore.Save(project, true);

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
        Require(request.Expected.Style.Equals("Kawaii", StringComparison.OrdinalIgnoreCase),
            "Vision non usa lo stile singolo Kawaii.");
        Require(!request.Expected.BoldEasy, "Linee Thin/Fine non forzano expected.bold_easy=false.");
        Require(request.Expected.Cozy, "Cozy ON non arriva in expected.cozy.");
        Require(request.GenerationContract.Contains("STYLE — HARD LOCK: Kawaii", StringComparison.OrdinalIgnoreCase),
            "Generation contract Vision non contiene STYLE hard lock Kawaii.");
        Require(request.GenerationContract.Contains("BOLD & EASY — HARD: OFF", StringComparison.Ordinal),
            "Generation contract Vision non contiene Bold & Easy OFF HARD.");
        Require(request.GenerationContract.Contains("COZY — HARD: ON", StringComparison.Ordinal),
            "Generation contract Vision non contiene Cozy ON HARD.");
        Require(request.GenerationContract.Contains("Thin — Fine", StringComparison.OrdinalIgnoreCase),
            "Generation contract Vision non conserva lo spessore Thin/Fine.");
        Require(request.GenerationContract.Contains("COMPOSITION — HARD LOCK", StringComparison.Ordinal),
            "Generation contract Vision non contiene composizione singola HARD.");
        Require(request.GenerationContract.Contains("realistic natural-history", StringComparison.OrdinalIgnoreCase),
            "Il Kawaii gate non respinge la resa realistica osservata nella prova fisica.");
        Require(request.Expected.HardCriteria.Any(c => c.Contains("STYLE MATCH IS HARD", StringComparison.OrdinalIgnoreCase)),
            "Expected Vision non classifica lo style match come HARD.");
        Require(request.Expected.HardCriteria.Any(c => c.Contains("BOLD & EASY MATCH IS HARD", StringComparison.OrdinalIgnoreCase)),
            "Expected Vision non classifica Bold & Easy ON/OFF come HARD.");
        Require(request.Expected.HardCriteria.Any(c => c.Contains("COZY MATCH IS HARD", StringComparison.OrdinalIgnoreCase)),
            "Expected Vision non classifica Cozy ON/OFF come HARD.");
        Require(request.Expected.HardCriteria.Any(c => c.Contains("LINE WEIGHT MATCH IS HARD", StringComparison.OrdinalIgnoreCase)),
            "Expected Vision non classifica lo spessore linea come HARD.");
        Require(request.Expected.HardCriteria.Any(c => c.Contains("triptych", StringComparison.OrdinalIgnoreCase)),
            "Expected Vision non classifica il triptych come HARD.");

        // Simulate a validator that detects authoritative mismatches but incorrectly labels them SOFT.
        // Diez must enforce project policy and promote every semantic mismatch below to HARD/FAIL,
        // including the structured scene-participant membership check.
        VisionValidationStore.Apply(project, state, new VisionValidationResult
        {
            VersionId = version.VersionId,
            WorkUnitId = unit.WorkUnitId,
            CandidateVersion = 1,
            ContentSha256 = version.ContentSha256,
            ProviderId = "self-test",
            OverallStatus = VisionValidationStatuses.Review,
            Confidence = 0.99,
            ObservedDescription = "A realistic cold natural-history engraving with thick contours, three panels and a missing required scene participant.",
            Summary = "Style, independent profiles, line weight, composition and scene membership are wrong.",
            Checks =
            [
                Check("style_match", "Realistic engraved anatomy; not Kawaii."),
                Check("bold_easy_match", "Visible thick simplified Bold & Easy-like contours despite expected OFF."),
                Check("cozy_match", "Cold documentary mood despite expected Cozy ON."),
                Check("line_weight_match", "Contours are thick despite Thin/Fine expectation."),
                Check("single_composition", "Three separate bordered panels are visible."),
                Check("scene_participants_match", "A required structured scene participant is missing.")
            ]
        });

        var stored = VisionValidationStore.Get(project, version.VersionId);
        Require(stored is not null && stored.BlocksApproval, "HARD mismatches non bloccano l'approvazione.");
        Require(stored!.OverallStatus == VisionValidationStatuses.Fail, "HARD mismatches non forzano overall FAIL.");
        var hardKeys = new[]
        {
            "style_match", "bold_easy_match", "cozy_match", "line_weight_match", "single_composition",
            "scene_participants_match"
        };
        Require(stored.Checks.Where(c => hardKeys.Contains(c.Key, StringComparer.OrdinalIgnoreCase)).All(c => c.Severity == VisionSeverity.Hard),
            "Diez non promuove tutti i controlli semantici autoritativi a HARD.");
        Require(version.Status == AiExchangeVersionStatuses.Incomplete,
            "Candidate con HARD FAIL non viene bloccata come INCOMPLETE.");
        Require(!AiExchangeApprovalService.CanApprove(project, state, version.VersionId, out _),
            "AiExchangeApprovalService consente ancora l'approvazione dopo scene_participants_match HARD FAIL.");
    }

    private static VisionValidationCheck Check(string key, string evidence) => new()
    {
        Key = key,
        Status = VisionCheckStatuses.Fail,
        Severity = VisionSeverity.Soft,
        Confidence = 0.99,
        Evidence = evidence
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("VISION STYLE HARD GATE SELF-TEST: " + message);
    }
}
