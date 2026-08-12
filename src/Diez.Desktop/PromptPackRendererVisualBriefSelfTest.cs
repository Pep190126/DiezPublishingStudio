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
VISIBLE CONTENT — HARD: include exactly 3 small stars on the collar.
""";

        var result = PromptPackRendererVisualBriefService.Build(source);
        Require(result.Contains("PRIMARY SUBJECT — HARD LOCK: one cat", StringComparison.OrdinalIgnoreCase),
            "Il soggetto atomico è stato perso.");
        Require(result.Contains("include exactly 3 small stars", StringComparison.OrdinalIgnoreCase),
            "Un conteggio visuale realmente item-specific è stato rimosso per errore.");
        foreach (var forbidden in new[]
                 {
                     "3 animals carini", "image per ogni animale", "one image per animal", "un’immagine per animale"
                 })
            Require(!result.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                "Direttiva di serie arrivata al renderer: " + forbidden);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("RENDERER VISUAL BRIEF SELF-TEST: " + message);
    }
}
