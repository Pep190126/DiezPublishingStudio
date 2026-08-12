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
VISIBLE CONTENT — HARD: include exactly 3 small stars on the collar.
""";

        var result = PromptPackRendererVisualBriefService.Build(source);
        Require(result.Contains("PRIMARY SUBJECT — HARD LOCK: one cat", StringComparison.OrdinalIgnoreCase),
            "Il soggetto atomico è stato perso.");
        Require(result.Contains("include exactly 3 small stars", StringComparison.OrdinalIgnoreCase),
            "Un conteggio visuale realmente item-specific è stato rimosso per errore.");
        Require(!result.Contains("3 animals carini", StringComparison.OrdinalIgnoreCase),
            "La direttiva di serie italiana è arrivata al renderer.");
        Require(!result.Contains("image per ogni animale", StringComparison.OrdinalIgnoreCase),
            "La direttiva one-image-per-animal è arrivata al renderer.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("RENDERER VISUAL BRIEF SELF-TEST: " + message);
    }
}
