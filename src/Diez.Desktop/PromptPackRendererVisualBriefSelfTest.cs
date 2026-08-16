namespace DiezPublishingStudio;

internal static class PromptPackRendererVisualBriefSelfTest
{
    public static void Run()
    {
        var source = """
Create ONE finished, publication-quality coloring-book illustration.
PRIMARY SUBJECT — HARD LOCK: one cat. The subject must be dominant.
COMPOSITION — HARD LOCK: one scene.
STYLE — HARD LOCK: Cute & Playful.
BOLD & EASY — HARD: ON.
COZY — HARD: ON.
USER REQUIREMENT — HARD: 3 animals carini e un'image per ogni animale
USER REQUIREMENT — HARD: one image per animal
USER REQUIREMENT — HARD: un’immagine per animale
USER REQUIREMENT — HARD: 1 image per character
VISIBLE CONTENT — HARD: include exactly 3 small stars on the collar.
""";

        var result = PromptPackRendererVisualBriefService.Build(source);
        Require(result.Contains("PRIMARY SUBJECT — HARD LOCK: one cat", StringComparison.OrdinalIgnoreCase),
            "Il soggetto atomico è stato perso.");
        Require(result.Contains("include exactly 3 small stars", StringComparison.OrdinalIgnoreCase),
            "Un conteggio visuale realmente item-specific è stato rimosso per errore.");
        foreach (var forbidden in new[]
                 {
                     "3 animals carini", "image per ogni animale", "one image per animal", "un’immagine per animale",
                     "1 image per character"
                 })
            Require(!result.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                "Direttiva di serie arrivata al renderer: " + forbidden);

        // Exact NuovoProgetto9 physical regression: an explicit Italian list describes the series,
        // not one Work Unit containing all three animals. The provider-facing normalizer must make
        // the list machine-readable before ResolveAtomicSubject assigns one item per Work Unit.
        var request = new PromptEngineeringRequest
        {
            BookType = BookTypeProfileService.ColoringBook,
            SeriesCount = 3,
            Subject = "gatto, cane, coniglio",
            Environment = "giardino e spazi all'aperto",
            MustDo = "1 image per ogni personaggio"
        };

        var expected = new[] { "one cat", "one dog", "one rabbit" };
        for (var i = 1; i <= 3; i++)
            Require(string.Equals(PromptPackProviderFacingService.ResolveAtomicSubject(request, i), expected[i - 1], StringComparison.OrdinalIgnoreCase),
                $"NuovoProgetto9 non decomposto: item {i} atteso {expected[i - 1]}.");

        var normalizedEnvironment = PromptEnglishNormalizer.NormalizeProviderFacing(request.Environment);
        Require(string.Equals(normalizedEnvironment, "garden and outdoor spaces", StringComparison.OrdinalIgnoreCase),
            "NuovoProgetto9 mantiene il setting italiano nel renderer prompt.");
        Require(string.Equals(PromptEnglishNormalizer.NormalizeProviderFacing(request.MustDo), "1 image per character", StringComparison.OrdinalIgnoreCase),
            "La direttiva di serie personaggio non viene normalizzata in inglese.");

        var normalizedSubject = PromptEnglishNormalizer.NormalizeProviderFacing(request.Subject);
        Require(string.Equals(normalizedSubject, "cat, dog, rabbit", StringComparison.OrdinalIgnoreCase),
            "La lista soggetti italiana non viene normalizzata in inglese.");
        Require(!PromptEnglishNormalizer.ContainsKnownItalianVisualVocabulary(normalizedSubject + "\n" + normalizedEnvironment),
            "Vocabolario italiano noto sopravvive dopo la normalizzazione NuovoProgetto9.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("RENDERER VISUAL BRIEF SELF-TEST: " + message);
    }
}
