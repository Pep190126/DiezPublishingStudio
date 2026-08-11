namespace DiezPublishingStudio;

internal static class PromptEngineeringSelfTest
{
    public static void Run()
    {
        var project = ProjectFileStore.Create("Prompt Engineering Test");
        BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);

        var coloring = BookTypePromptProfileService.LoadColoring(project);
        coloring.SubjectDescription = "animali della jungla\nImmagine 3: una scimmia su una liana";
        coloring.EnvironmentDescription = "giungla leggibile e poco affollata\nImmagine 3: vicino a una cascata";
        coloring.Style = "Bold & Easy";
        coloring.TargetAudience = "Bambini 6–9 anni";
        coloring.LineWeight = "Spesso — Bold";
        BookTypePromptProfileService.SaveColoring(project, coloring);

        var openai = PromptEngineeringEngine.BuildSeriesPrompt(
            project, 3, "animali della jungla", string.Empty,
            PromptEngineeringProviderIds.OpenAi, true);
        var gemini = PromptEngineeringEngine.BuildSeriesPrompt(
            project, 3, "animali della jungla", string.Empty,
            PromptEngineeringProviderIds.Gemini, true);
        var other = PromptEngineeringEngine.BuildSeriesPrompt(
            project, 3, "animali della jungla", string.Empty,
            PromptEngineeringProviderIds.Other, true);
        var generic = PromptEngineeringEngine.BuildSeriesPrompt(
            project, 3, "animali della jungla", string.Empty,
            PromptEngineeringProviderIds.Generic, true);

        Require(openai.Length >= 4500, $"Prompt OpenAI troppo debole/corto: {openai.Length} caratteri.");
        foreach (var prompt in new[] { openai, gemini, other, generic })
        {
            foreach (var required in new[]
            {
                "COMMERCIAL COLORING BOOK",
                "pure black #000000",
                "pure white #FFFFFF",
                "No photorealism",
                "recognizable anatomy",
                "random floating symbols",
                "clipart",
                "PROFESSIONAL QUALITY GATE",
                "FAIL-SAFE / SELF-CHECK",
                "animali della jungla"
            })
                Require(prompt.Contains(required, StringComparison.OrdinalIgnoreCase), "Nucleo professionale mancante: " + required);
        }

        Require(openai.Contains("OPENAI IMAGE GENERATION", StringComparison.Ordinal), "Renderer OpenAI non specifico.");
        Require(gemini.Contains("GEMINI IMAGE GENERATION", StringComparison.Ordinal), "Renderer Gemini non specifico.");
        Require(other.Contains("OTHER / USER-SELECTED IMAGE MODEL", StringComparison.Ordinal), "Renderer Altro non specifico.");
        Require(generic.Contains("MODEL-AGNOSTIC IMAGE GENERATION", StringComparison.Ordinal), "Renderer generico non tecnico.");
        Require(!string.Equals(openai, gemini, StringComparison.Ordinal), "OpenAI e Gemini producono lo stesso prompt.");

        PromptMasterStateStore.Save(project, new PromptMasterState
        {
            BookType = BookTypeProfileService.ColoringBook,
            ProviderId = PromptEngineeringProviderIds.OpenAi,
            PreferAdvancedModel = true,
            SeriesCount = 3,
            MustDo = "animali della jungla",
            Prompt = openai
        });

        var item1 = PromptEngineeringEngine.BuildItemPrompt(project, openai, 3, 1, "IMG-001", PromptEngineeringProviderIds.OpenAi, true);
        var item3 = PromptEngineeringEngine.BuildItemPrompt(project, openai, 3, 3, "IMG-003", PromptEngineeringProviderIds.OpenAi, true);
        Require(item1.Contains("Generate EXACTLY ONE image", StringComparison.Ordinal), "Work Unit non impone esattamente un'immagine.");
        Require(item1.Contains("item 1 of 3", StringComparison.OrdinalIgnoreCase), "Posizione item 1 mancante.");
        Require(!item1.Contains("near a waterfall", StringComparison.OrdinalIgnoreCase) &&
                !item1.Contains("vicino a una cascata", StringComparison.OrdinalIgnoreCase),
            "Override immagine 3 contaminato nell'immagine 1.");
        Require(item3.Contains("una scimmia su una liana", StringComparison.OrdinalIgnoreCase), "Override soggetto immagine 3 mancante.");
        Require(item3.Contains("vicino a una cascata", StringComparison.OrdinalIgnoreCase), "Override ambiente immagine 3 mancante.");
        Require(item3.Contains("not generate a grid", StringComparison.OrdinalIgnoreCase), "Contratto anti-collage mancante.");

        var sparse = ProjectFileStore.Create("Sparse Prompt Test");
        BookTypeProfileService.Set(sparse, BookTypeProfileService.ColoringBook);
        var sparsePrompt = PromptEngineeringEngine.BuildSeriesPrompt(
            sparse, 1, string.Empty, string.Empty, PromptEngineeringProviderIds.Other, true);
        Require(sparsePrompt.Length >= 4000, "Con parametri opzionali vuoti il prompt perde potenza.");
        Require(sparsePrompt.Contains("PROFESSIONAL QUALITY GATE", StringComparison.Ordinal), "Quality gate assente nel prompt minimale.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("PROMPT ENGINEERING SELF-TEST: " + message);
    }
}
