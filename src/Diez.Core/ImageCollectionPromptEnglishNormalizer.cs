namespace DiezPublishingStudio;

/// <summary>
/// Provider-facing normalization for Image Collection / Illustrated Book vocabulary.
/// Persisted/UI values stay unchanged; only the prompt sent to an image model is normalized.
/// </summary>
internal static class ImageCollectionPromptEnglishNormalizer
{
    private static readonly (string Source, string Target)[] Replacements =
    [
        ("Illustrazione editoriale / saggio", "Editorial illustration / essay"),
        ("Sequenza di esercizi / movimenti", "Exercise / movement sequence"),
        ("Illustrazione didattica", "Educational illustration"),
        ("Figura tecnica / manuale", "Technical figure / manual"),
        ("Schema anatomico semplificato", "Simplified anatomical diagram"),
        ("Serie di riferimento coerente", "Consistent reference series"),
        ("Raccolta artistica / concettuale", "Artistic / conceptual collection"),
        ("Decorazione editoriale", "Editorial decoration"),
        ("Colore pieno", "Full color"),
        ("Colore limitato / palette controllata", "Limited color / controlled palette"),
        ("Scala di grigi — con sfumature", "Grayscale — tonal shading allowed"),
        ("Bianco e nero puro — 2 colori", "Pure black and white — two colors"),
        ("Monocromatico — una tinta + bianco", "Monochrome — one hue plus white"),
        ("Automatico secondo il contenuto", "Automatic according to content"),
        ("Molto schematico", "Very schematic"),
        ("Molto alto", "Very high"),
        ("Senza contorno dominante", "No dominant outline"),
        ("Contorno molto sottile", "Very thin outline"),
        ("Contorno sottile", "Thin outline"),
        ("Contorno medio", "Medium outline"),
        ("Contorno spesso", "Thick outline"),
        ("Contorno variabile", "Variable outline"),
        ("Illustrativo chiaro", "Clear illustrative"),
        ("Line art editoriale", "Editorial line art"),
        ("Infografico / didattico", "Infographic / educational"),
        ("Realistico semplificato", "Simplified realistic"),
        ("Tecnico pulito", "Clean technical"),
        ("Pittorico controllato", "Controlled painterly"),
        ("Fotografico / realistico", "Photographic / realistic"),
        ("Nessuno / trasparente se supportato", "None / transparent if supported"),
        ("Bianco pulito", "Clean white"),
        ("Semplice / funzionale", "Simple / functional"),
        ("Contestuale leggero", "Light contextual"),
        ("Ambientato / completo", "Fully contextualized"),
        ("Variabile secondo il soggetto", "Variable according to subject"),
        ("Frontale", "Front view"),
        ("Tre quarti", "Three-quarter view"),
        ("Laterale", "Side view"),
        ("Dall'alto", "Top view"),
        ("Stesso punto di vista per tutta la serie", "Same viewpoint throughout the series"),
        ("Alto", "High"),
        ("Basso", "Low")
    ];

    public static string Normalize(string? text)
    {
        var output = text ?? string.Empty;
        foreach (var (source, target) in Replacements.OrderByDescending(x => x.Source.Length))
            output = output.Replace(source, target, StringComparison.OrdinalIgnoreCase);
        return output;
    }

    public static bool ContainsKnownItalianProfileVocabulary(string? text) =>
        Replacements.Any(x => (text ?? string.Empty).Contains(x.Source, StringComparison.OrdinalIgnoreCase));
}
