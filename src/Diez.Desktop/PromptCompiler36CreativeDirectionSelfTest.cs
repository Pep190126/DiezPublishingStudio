namespace DiezPublishingStudio;

internal static class PromptCompiler36CreativeDirectionSelfTest
{
    public static void Run()
    {
        Require(PromptEngineeringCompiler.Version == "3.6", "versione compiler inattesa.");

        var project = ProjectFileStore.Create("Compiler 3.6 Scene Direction");
        BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
        var coloring = BookTypePromptProfileService.LoadColoring(project);
        coloring.SubjectDescription = "friendly woodland characters";
        coloring.EnvironmentDescription = "a quiet woodland garden";
        coloring.Style = "Kawaii";
        coloring.TargetAudience = "Bambini 6–9 anni";
        coloring.Difficulty = "Facile";
        coloring.LineWeight = "Sottile — Fine";
        coloring.Complexity = "Bassa";
        coloring.ElementDensity = "Bassa";
        coloring.Background = "Contestuale leggero";
        BookTypePromptProfileService.SaveColoring(project, coloring);
        ColoringBoldEasyPolicyStore.Save(project, false, coloring.LineWeight);
        ColoringCozyPolicyStore.Save(project, true);

        var multi = MultiSubjectProfileService.Load(project);
        multi.Enabled = true;
        MultiSubjectProfileService.SetCount(multi, 2);
        var subjects = MultiSubjectProfileService.ActiveSubjects(multi).ToList();
        Rename(multi, subjects[0], "Milo");
        Rename(multi, subjects[1], "Luna");
        subjects[0].Description = "small cat with a heart-shaped patch above the left eye";
        subjects[1].Description = "friendly dog with long floppy ears";
        MultiSubjectProfileService.Save(project, multi);

        StructuredSceneEnvironmentStore.Save(project, "a quiet woodland garden");
        var scenes = StructuredSceneProfileService.Load(project);
        scenes.Enabled = true;
        var scene = StructuredSceneProfileService.Add(scenes);
        if (!StructuredSceneProfileService.TryRename(scenes, scene, "Butterfly game", out var sceneError))
            throw new InvalidOperationException("PROMPT COMPILER 3.6 SELF-TEST: " + sceneError);
        scene.Description = "Milo chases a butterfly while Luna watches nearby";
        StructuredSceneProfileService.SetSubjectParticipation(scenes, scene.SceneId, subjects[0].SubjectId, true);
        StructuredSceneProfileService.SetSubjectParticipation(scenes, scene.SceneId, subjects[1].SubjectId, true);
        scenes.ActiveSceneId = scene.SceneId;
        StructuredSceneProfileService.Save(project, scenes);

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
            SeriesCount = 1,
            Prompt = PromptEngineeringCompiler.BuildSeriesPrompt(
                project, 1, string.Empty, string.Empty, PromptEngineeringProviderIds.OpenAi, true)
        });

        var unit = new AiExchangeWorkUnit
        {
            WorkUnitId = Guid.NewGuid(),
            Code = "IMG-001",
            ContentType = AiExchangeContentTypes.Image,
            Mode = AiExchangeModes.AiOnly,
            Position = 1
        };
        var settings = PromptPreparationSettingsStore.Load(project);
        var raw = PromptPackProviderFacingService.BuildImageGenerationPrompt(project, unit, 1, 1, settings);
        var renderer = PromptPackRendererVisualBriefService.Build(raw);
        var lines = renderer.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Require(lines.Length > 4, "renderer brief troppo corto.");
        Require(lines[0].StartsWith("Create ONE finished, publication-quality coloring-book illustration.", StringComparison.Ordinal),
            "single-image anchor non è la prima istruzione.");
        Require(lines[1].StartsWith("ART DIRECTION — SYNTHESIZED:", StringComparison.Ordinal),
            "art direction sintetizzata non segue immediatamente il single-image anchor.");
        Require(lines[1].Contains("Milo", StringComparison.OrdinalIgnoreCase), "art direction non usa il soggetto focale.");
        Require(lines[1].Contains("Milo chases a butterfly while Luna watches nearby", StringComparison.OrdinalIgnoreCase),
            "art direction non usa l'azione della scena corrente.");
        Require(lines[1].Contains("Milo, Luna", StringComparison.OrdinalIgnoreCase),
            "art direction non usa la membership strutturata della scena.");
        Require(lines[1].Contains("current scene action determine the local staging", StringComparison.OrdinalIgnoreCase),
            "la scena locale non prevale sull'ambientazione generale.");
        Require(renderer.Contains("SCENE PARTICIPANTS — HARD LOCK: Milo, Luna", StringComparison.Ordinal),
            "partecipanti scena non arrivano come HARD lock.");
        Require(renderer.Contains("SCENE INTENT — HARD LOCK: Butterfly game", StringComparison.Ordinal),
            "intent della scena non arriva come HARD lock.");
        Require(renderer.Contains("STYLE — HARD LOCK: Kawaii", StringComparison.OrdinalIgnoreCase),
            "stile selezionato non resta HARD dopo la sintesi.");
        Require(renderer.Contains("COZY — HARD: ON", StringComparison.Ordinal),
            "Cozy non resta HARD dopo la sintesi.");
        Require(renderer.Contains("visibly thin, fine, crisp black contours", StringComparison.OrdinalIgnoreCase),
            "Thin/Fine non viene tradotto in istruzione visuale performante.");
        Require(!renderer.Contains(scene.SceneId, StringComparison.OrdinalIgnoreCase), "SceneId interno è trapelato nel renderer.");
        Require(!renderer.Contains(subjects[0].SubjectId, StringComparison.OrdinalIgnoreCase) &&
                !renderer.Contains(subjects[1].SubjectId, StringComparison.OrdinalIgnoreCase),
            "SubjectId interno è trapelato nel renderer.");
        foreach (var forbidden in new[] { "FRESH GENERATION", "DIEZ RENDER REQUEST ID", "SERIES ROLE", "FINAL CHECK — HARD" })
            Require(!renderer.Contains(forbidden, StringComparison.OrdinalIgnoreCase), "orchestrazione nel renderer: " + forbidden);
    }

    private static void Rename(MultiSubjectProfile model, MultiSubjectDefinition subject, string name)
    {
        if (!MultiSubjectProfileService.TryRename(model, subject, name, out var error))
            throw new InvalidOperationException("PROMPT COMPILER 3.6 SELF-TEST: " + error);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("PROMPT COMPILER 3.6 SELF-TEST: " + message);
    }
}
