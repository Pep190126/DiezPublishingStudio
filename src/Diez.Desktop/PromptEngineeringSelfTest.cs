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

        var openai = PromptEngineeringCompiler.BuildSeriesPrompt(
            project, 3, "animali della jungla", string.Empty,
            PromptEngineeringProviderIds.OpenAi, true);
        var gemini = PromptEngineeringCompiler.BuildSeriesPrompt(
            project, 3, "animali della jungla", string.Empty,
            PromptEngineeringProviderIds.Gemini, true);
        var other = PromptEngineeringCompiler.BuildSeriesPrompt(
            project, 3, "animali della jungla", string.Empty,
            PromptEngineeringProviderIds.Other, true);
        var generic = PromptEngineeringCompiler.BuildSeriesPrompt(
            project, 3, "animali della jungla", string.Empty,
            PromptEngineeringProviderIds.Generic, true);

        Require(openai.Length >= 5000, $"Prompt OpenAI troppo debole/corto: {openai.Length} caratteri.");
        foreach (var prompt in new[] { openai, gemini, other, generic })
        {
            foreach (var required in new[]
            {
                "COMMERCIAL COLORING BOOK",
                "pure black #000000",
                "pure white #FFFFFF",
                "No photorealism",
                "recognizable anatomy",
                "random floating diamonds",
                "clipart",
                "PROFESSIONAL QUALITY GATE",
                "FAIL-SAFE / SELF-CHECK",
                "animali della jungla",
                "CANONICAL DIEZ PRODUCTION SPECIFICATION"
            })
                Require(prompt.Contains(required, StringComparison.OrdinalIgnoreCase), "Nucleo professionale mancante: " + required);
        }

        Require(openai.Contains("PROVIDER EXECUTION PROFILE — OPENAI", StringComparison.Ordinal), "Renderer OpenAI non specifico.");
        Require(openai.Contains("GPT Image 2", StringComparison.Ordinal), "Profilo OpenAI avanzato non indirizza la generazione immagini corrente.");
        Require(gemini.Contains("PROVIDER EXECUTION PROFILE — GEMINI", StringComparison.Ordinal), "Renderer Gemini non specifico.");
        Require(gemini.Contains("ONE coherent scene concept", StringComparison.Ordinal), "Gemini non usa strategia scene-first.");
        Require(other.Contains("PROVIDER EXECUTION PROFILE — OTHER", StringComparison.Ordinal), "Renderer Altro non specifico.");
        Require(other.Contains("native aspect-ratio", StringComparison.OrdinalIgnoreCase), "Altro non spiega il mapping dei controlli nativi.");
        Require(generic.Contains("MODEL-AGNOSTIC / GENERIC", StringComparison.Ordinal), "Renderer generico non tecnico.");
        Require(!string.Equals(openai, gemini, StringComparison.Ordinal) &&
                !string.Equals(openai, other, StringComparison.Ordinal) &&
                !string.Equals(gemini, other, StringComparison.Ordinal),
            "Le strategie provider-specific collassano nello stesso prompt.");

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
        var sparseProfile = BookTypePromptProfileService.LoadColoring(sparse);
        sparseProfile.Style = "Personalizzato";
        sparseProfile.CustomStyleNotes = string.Empty;
        sparseProfile.SubjectDescription = "animali della jungla";
        sparseProfile.EnvironmentDescription = "jungla";
        sparseProfile.TargetAudience = "Tutte le età";
        sparseProfile.Difficulty = "Facile";
        sparseProfile.LineWeight = "Spesso — Bold";
        sparseProfile.Complexity = "Bassa";
        sparseProfile.ElementDensity = "Bassa";
        sparseProfile.Background = "Nessuno / bianco";
        BookTypePromptProfileService.SaveColoring(sparse, sparseProfile);

        var sparsePrompt = PromptEngineeringCompiler.BuildSeriesPrompt(
            sparse, 3, "animali della jungla", string.Empty, PromptEngineeringProviderIds.Other, true);
        Require(sparsePrompt.Length >= 4500, "Con profilo Personalizzato e pochi parametri il prompt perde potenza.");
        foreach (var required in new[]
        {
            "PROFESSIONAL QUALITY GATE", "recognizable anatomy", "clipart", "random floating diamonds",
            "pure black #000000", "pure white #FFFFFF", "FAIL-SAFE / SELF-CHECK", "animali della jungla",
            "PROVIDER EXECUTION PROFILE — OTHER"
        })
            Require(sparsePrompt.Contains(required, StringComparison.OrdinalIgnoreCase),
                "Profilo Personalizzato minimale ha perso il vincolo: " + required);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("PROMPT ENGINEERING SELF-TEST: " + message);
    }
}
