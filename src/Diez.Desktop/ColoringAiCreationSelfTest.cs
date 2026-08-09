namespace DiezPublishingStudio;

internal static class ColoringAiCreationSelfTest
{
    public static void Run()
    {
        Require(ColoringAiCreationUi.TryCount("1", out var one, out _) && one == 1,
            "Il Coloring Book non accetta una singola immagine.");
        Require(ColoringAiCreationUi.TryCount("37", out var many, out _) && many == 37,
            "Il numero preciso di immagini non viene conservato.");
        Require(ColoringAiCreationUi.TryCount("500", out var max, out _) && max == 500,
            "Il limite massimo previsto non è accettato.");
        Require(!ColoringAiCreationUi.TryCount("0", out _, out _),
            "Zero immagini non deve essere accettato.");
        Require(!ColoringAiCreationUi.TryCount("abc", out _, out _),
            "Un valore non numerico non deve essere accettato.");
        Require(!ColoringAiCreationUi.TryCount("501", out _, out _),
            "Il limite oltre 500 non deve essere accettato.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("COLORING AI SELF-TEST: " + message);
    }
}
