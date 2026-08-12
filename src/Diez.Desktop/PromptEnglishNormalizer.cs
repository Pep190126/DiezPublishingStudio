using System.Text.RegularExpressions;

namespace DiezPublishingStudio;

/// <summary>
/// Normalizes provider-facing visual prompt text without changing the values persisted in the project/UI.
/// This is deliberately domain-scoped: it translates Diez visual-profile vocabulary and common visual
/// intent phrases that can be emitted by the Italian UI, while leaving IDs, hashes and technical values intact.
/// </summary>
internal static class PromptEnglishNormalizer
{
    private static readonly (string Source, string Target)[] Replacements =
    [
        ("3 immagini separate di animali della jungla", "3 separate images of jungle animals"),
        ("3 immagini separate di animali della giungla", "3 separate images of jungle animals"),
        ("giungla leggibile e poco affollata", "clear, uncluttered jungle"),
        ("Variabile — contorni principali più spessi, dettagli più sottili", "Variable — stronger main contours, finer details"),
        ("Personalizzata — usa i pixel indicati", "Custom — use the specified pixel dimensions"),
        ("Dettaglio alto ma colorabile", "High detail while remaining colorable"),
        ("Stile realistico semplificato", "Simplified realistic style"),
        ("Molto sottile — Extra Fine", "Very thin — Extra Fine"),
        ("Molto spesso — Extra Bold", "Very thick — Extra Bold"),
        ("Prescolare 3–5 anni", "Preschool ages 3–5"),
        ("Bambini 6–9 anni", "Children ages 6–9"),
        ("Ragazzi 10–13 anni", "Children ages 10–13"),
        ("Nessuno / bianco", "None / white"),
        ("Semplice / minimo", "Simple / minimal"),
        ("Contestuale leggero", "Light contextual background"),
        ("Line Art dettagliata", "Detailed line art"),
        ("Line Art pulita", "Clean line art"),
        ("Massima / stampa", "Maximum / print"),
        ("Complexity: Media", "Complexity: Medium"),
        ("element density: Media", "element density: Medium"),
        ("Element density: Media", "Element density: Medium"),
        ("animali della jungla", "jungle animals"),
        ("animali della giungla", "jungle animals"),
        ("una scimmia su una liana", "a monkey on a vine"),
        ("vicino a una cascata", "near a waterfall"),
        ("cambia lo sfondo", "change the background"),
        ("senza cambiare il soggetto", "without changing the subject"),
        ("Molto dettagliato", "Very detailed"),
        ("Molto facile", "Very easy"),
        ("Molto bassa", "Very low"),
        ("Molto alta", "Very high"),
        ("Molto ampio", "Very large"),
        ("Molto compatto", "Very compact"),
        ("Spesso — Bold", "Thick — Bold"),
        ("Sottile — Fine", "Thin — Fine"),
        ("Tutte le età", "All ages"),
        ("Personalizzato", "Custom"),
        ("Adolescenti", "Teenagers"),
        ("Adulti", "Adults"),
        ("Impegnativa", "Challenging"),
        ("Dettagliato", "Detailed"),
        ("Verticale", "Portrait"),
        ("Orizzontale", "Landscape"),
        ("Quadrato", "Square"),
        ("Trasparente", "Transparent"),
        ("Linee pulite", "Clean lines"),
        ("Linee spesse", "Thick lines"),
        ("Linee sottili", "Thin lines"),
        ("Bianco", "White"),
        ("Facile", "Easy"),
        ("Bassa", "Low"),
        ("Alta", "High"),
        ("Ampio", "Large"),
        ("Medio", "Medium"),
        ("Compatto", "Compact"),
        ("Misto", "Mixed"),
        ("Bloccato", "Locked"),
        ("Preferito", "Preferred"),
        ("Libero", "Free"),
        ("Utente", "User"),
        ("Sì", "Yes"),
        ("giungla", "jungle"),
        ("jungla", "jungle"),
        ("animali", "animals"),
        ("immagini", "images"),
        ("immagine", "image"),
        ("scimmia", "monkey"),
        ("elefante", "elephant"),
        ("tigre", "tiger"),
        ("leone", "lion"),
        ("giraffa", "giraffe"),
        ("cascata", "waterfall"),
        ("liana", "vine")
    ];

    public static string NormalizeProviderFacing(string? text)
    {
        var output = (text ?? string.Empty).Replace("\r\n", "\n");
        foreach (var (source, target) in Replacements.OrderByDescending(x => x.Source.Length))
        {
            var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(source)}(?![\p{{L}}\p{{N}}])";
            output = Regex.Replace(
                output,
                pattern,
                _ => target,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return output.Trim();
    }

    public static bool ContainsKnownItalianVisualVocabulary(string? text)
    {
        var value = text ?? string.Empty;
        return Replacements.Any(x => Regex.IsMatch(
            value,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(x.Source)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }
}
