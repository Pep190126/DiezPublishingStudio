namespace DiezPublishingStudio;

internal static class MultiSubjectProfileSelfTest
{
    public static void Run()
    {
        var project = ProjectFileStore.Create("Multi Subject Contract");
        BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
        var profile = BookTypePromptProfileService.LoadColoring(project);
        profile.SubjectDescription = "animali domestici";
        profile.EnvironmentDescription = "garden";
        profile.Style = "Kawaii";
        profile.LineWeight = "Medio";
        BookTypePromptProfileService.SaveColoring(project, profile);

        var model = MultiSubjectProfileService.Load(project);
        model.Enabled = true;
        model.GroupDescription = "animali domestici";
        MultiSubjectProfileService.SetCount(model, 3);
        var subjects = MultiSubjectProfileService.ActiveSubjects(model).ToList();
        Require(subjects.Count == 3, "SetCount non crea tre soggetti attivi.");
        Require(subjects.Select(x => x.SubjectId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3,
            "SubjectId non univoci.");

        Rename(model, subjects[0], "Milo");
        Rename(model, subjects[1], "Luna");
        Rename(model, subjects[2], "Toby");
        subjects[0].Description = "small cat with a round face and a heart-shaped patch above the left eye";
        subjects[1].Description = "friendly dog with long floppy ears and a small round nose";
        subjects[2].Description = "young rabbit with one ear slightly bent forward";
        subjects[0].Consistency["outfit"].Level = "FREE";
        subjects[0].Consistency["outfit"].Strategy = "USER";
        subjects[0].Consistency["outfit"].Variation = "outfit may change, but the heart-shaped patch never changes";
        model.ActiveSubjectId = subjects[0].SubjectId;
        MultiSubjectProfileService.Save(project, model);

        var reloaded = MultiSubjectProfileService.Load(project);
        Require(MultiSubjectProfileService.ActiveSubjects(reloaded).Select(x => x.SubjectId)
                    .SequenceEqual(subjects.Select(x => x.SubjectId), StringComparer.OrdinalIgnoreCase),
            "SubjectId non stabili dopo persistenza.");
        Require(MultiSubjectProfileService.SubjectForItem(project, 1).Name == "Milo", "Item 1 non risolve Milo.");
        Require(MultiSubjectProfileService.SubjectForItem(project, 2).Name == "Luna", "Item 2 non risolve Luna.");
        Require(MultiSubjectProfileService.SubjectForItem(project, 3).Name == "Toby", "Item 3 non risolve Toby.");
        Require(MultiSubjectProfileService.SubjectForItem(project, 4).SubjectId == subjects[0].SubjectId,
            "Il riuso ciclico non conserva il SubjectId del primo soggetto.");

        ImageCollectionWorkspaceService.SetConsistencyRules(project, "Consistent enabled");
        var settings = new PromptPreparationSettings { ProviderId = PromptEngineeringProviderIds.OpenAi, PreferAdvancedModel = true };
        var prompts = new List<string>();
        for (var i = 1; i <= 3; i++)
        {
            var unit = new AiExchangeWorkUnit
            {
                Code = $"IMG-{i:000}",
                ContentType = AiExchangeContentTypes.Image,
                Mode = AiExchangeModes.AiOnly,
                Position = i
            };
            prompts.Add(PromptPackProviderFacingService.BuildImageGenerationPrompt(project, unit, 3, i, settings));
        }

        foreach (var expected in new[] { "Milo", "Luna", "Toby" })
        {
            var index = Array.IndexOf(new[] { "Milo", "Luna", "Toby" }, expected);
            Require(prompts[index].Contains("PRIMARY SUBJECT — HARD LOCK: " + expected, StringComparison.Ordinal),
                "Renderer brief non usa il soggetto strutturato: " + expected);
        }
        Require(prompts.Distinct(StringComparer.Ordinal).Count() == 3, "I tre soggetti strutturati producono prompt identici.");
        Require(prompts[0].Contains("heart-shaped patch", StringComparison.OrdinalIgnoreCase),
            "Descrizione identitaria di Milo non arriva al renderer.");
        Require(prompts[0].Contains("SUBJECT-SPECIFIC CONSISTENT", StringComparison.Ordinal),
            "Consistent specifico del soggetto non arriva al renderer.");
        Require(prompts[0].Contains("Outfit / accessories — FREE — decision owner: USER", StringComparison.OrdinalIgnoreCase),
            "Regola outfit specifica non viene serializzata.");
        Require(!prompts[1].Contains("heart-shaped patch", StringComparison.OrdinalIgnoreCase),
            "La descrizione di Milo contamina Luna.");

        // Lowering the requested cast size is non-destructive: IDs/history stay in the model.
        var beforeAllIds = reloaded.Subjects.Select(x => x.SubjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        MultiSubjectProfileService.SetCount(reloaded, 2);
        Require(MultiSubjectProfileService.ActiveSubjects(reloaded).Count == 2, "Riduzione cast non porta a due attivi.");
        Require(reloaded.Subjects.All(x => beforeAllIds.Contains(x.SubjectId)), "Riduzione cast cancella SubjectId storici.");

        // Custom is not a soft note: its text is the resolved HARD style authority.
        var customDefinition = "soft rounded 1960s children's ink illustration with playful asymmetry and simple organic contours";
        profile = BookTypePromptProfileService.LoadColoring(project);
        profile.Style = "Custom";
        profile.CustomStyleNotes = customDefinition;
        BookTypePromptProfileService.SaveColoring(project, profile);
        Require(ColoringIndependentHardProfileService.Resolve(project).Style == customDefinition,
            "Il testo Custom non diventa l'autorità stile HARD.");

        var saved = CustomStyleLibraryService.Add(customDefinition);
        Require(ColoringIndependentHardProfileService.SelectableStyles.Contains(saved.Label, StringComparer.OrdinalIgnoreCase),
            "Stile Custom autorizzato non compare nella libreria selezionabile.");
        ColoringIndependentHardProfileService.PersistResolvedState(project, saved.Label, "Medio", false, false);
        var afterLibrarySelection = BookTypePromptProfileService.LoadColoring(project);
        Require(string.Equals(afterLibrarySelection.Style, "Custom", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(afterLibrarySelection.CustomStyleNotes, customDefinition, StringComparison.Ordinal),
            "Selezione Custom dalla libreria non ripristina la definizione HARD nel progetto.");
    }

    private static void Rename(MultiSubjectProfile model, MultiSubjectDefinition subject, string name)
    {
        if (!MultiSubjectProfileService.TryRename(model, subject, name, out var error))
            throw new InvalidOperationException("MULTI SUBJECT SELF-TEST: " + error);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("MULTI SUBJECT SELF-TEST: " + message);
    }
}
