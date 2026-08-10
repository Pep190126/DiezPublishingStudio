using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

/// <summary>
/// Book-type-native prompt knowledge. User text is additive: even with an empty brief,
/// Diez emits a substantial editorial/technical prompt appropriate to the selected book type.
/// </summary>
internal static class BookTypePromptProfileService
{
    private const string ColoringEntityKind = "DiezColoringPromptProfile";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal sealed class ColoringProfile
    {
        public string Style { get; set; } = "Bold & Easy";
        public string TargetAudience { get; set; } = "Bambini 6–9 anni";
        public string Difficulty { get; set; } = "Facile";
        public string LineWeight { get; set; } = "Spesso";
        public string Complexity { get; set; } = "Bassa";
        public string ElementDensity { get; set; } = "Bassa";
        public string Background { get; set; } = "Semplice / minimo";
        public string WhiteSpace { get; set; } = "Ampio";
        public bool ClosedAreas { get; set; } = true;
        public bool AvoidTinyAreas { get; set; } = true;
        public bool CleanContours { get; set; } = true;
        public bool BlackAndWhiteOnly { get; set; } = true;
        public bool NoGray { get; set; } = true;
        public bool NoShadows { get; set; } = true;
        public bool NoTextInsideImage { get; set; } = true;
        public bool SubjectClearlySeparated { get; set; } = true;
        public string CustomStyleNotes { get; set; } = string.Empty;
    }

    public static readonly string[] ColoringStyles =
    [
        "Bold & Easy",
        "Line Art pulita",
        "Line Art dettagliata",
        "Kawaii / Cartoon",
        "Mandala / Pattern",
        "Stile realistico semplificato",
        "Personalizzato"
    ];

    public static readonly string[] TargetAudiences =
    [
        "Prescolare 3–5 anni",
        "Bambini 6–9 anni",
        "Ragazzi 10–13 anni",
        "Adolescenti",
        "Adulti",
        "Tutte le età"
    ];

    public static readonly string[] Difficulties = ["Molto facile", "Facile", "Media", "Impegnativa"];
    public static readonly string[] LineWeights = ["Molto spesso", "Spesso", "Medio", "Sottile"];
    public static readonly string[] Complexities = ["Molto bassa", "Bassa", "Media", "Alta"];
    public static readonly string[] Densities = ["Molto bassa", "Bassa", "Media", "Alta"];
    public static readonly string[] Backgrounds = ["Nessuno / bianco", "Semplice / minimo", "Contestuale leggero", "Dettagliato"];
    public static readonly string[] WhiteSpaces = ["Molto ampio", "Ampio", "Medio", "Compatto"];

    public static ColoringProfile LoadColoring(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, ColoringEntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return new ColoringProfile();
        try { return JsonSerializer.Deserialize<ColoringProfile>(entity.Notes, JsonOptions) ?? new ColoringProfile(); }
        catch { return new ColoringProfile(); }
    }

    public static void SaveColoring(PreviewProject project, ColoringProfile profile)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, ColoringEntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity { Kind = ColoringEntityKind, Name = "Profilo prompt Coloring", IsCandidate = false };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(profile, JsonOptions);
    }

    public static string BuildBookTypeBlock(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase) ||
            BookTypeProfileService.IsImageCollection(project))
            return BuildColoringBlock(LoadColoring(project));

        return $"PROFILO EDITORIALE DEL TIPO LIBRO:\n- Tipo libro: {type}.\n- Mantieni struttura, tono, output e vincoli coerenti con questo Tipo libro.\n- Non omettere requisiti editoriali o tecnici impliciti nel formato scelto.";
    }

    public static string BuildColoringBlock(ColoringProfile p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PROFILO EDITORIALE COLORING BOOK:");
        sb.AppendLine($"- Stile principale: {p.Style}.");
        sb.AppendLine($"- Pubblico / fascia: {p.TargetAudience}.");
        sb.AppendLine($"- Difficoltà di colorazione: {p.Difficulty}.");
        sb.AppendLine($"- Spessore linea: {p.LineWeight}.");
        sb.AppendLine($"- Complessità visiva: {p.Complexity}.");
        sb.AppendLine($"- Densità di elementi: {p.ElementDensity}.");
        sb.AppendLine($"- Sfondo: {p.Background}.");
        sb.AppendLine($"- Spazio bianco: {p.WhiteSpace}.");

        sb.AppendLine("- Obiettivo: produrre pagine realmente colorabili, leggibili a colpo d'occhio e adatte alla stampa, non semplici illustrazioni monocromatiche.");
        if (p.CleanContours) sb.AppendLine("- Contorni puliti, continui e facilmente distinguibili; evitare linee sporche, doppie o frammentate.");
        if (p.ClosedAreas) sb.AppendLine("- Preferire aree chiuse e chiaramente delimitate, facili da colorare senza ambiguità.");
        if (p.AvoidTinyAreas) sb.AppendLine("- Evitare micro-aree, dettagli minuscoli o incroci di linee che rendano difficile la colorazione.");
        if (p.SubjectClearlySeparated) sb.AppendLine("- Soggetto principale chiaramente separato dallo sfondo e leggibile anche in miniatura.");
        if (p.BlackAndWhiteOnly) sb.AppendLine("- Solo bianco e nero puro: linee nere su fondo bianco.");
        if (p.NoGray) sb.AppendLine("- Nessun grigio, mezzatinta, texture grigia, anti-shading visibile o riempimento tonale.");
        if (p.NoShadows) sb.AppendLine("- Nessuna ombra o sfumatura salvo richiesta esplicita dell'utente.");
        if (p.NoTextInsideImage) sb.AppendLine("- Nessun testo, lettera, numero, watermark, firma, ID, didascalia o nome file dentro l'immagine.");

        foreach (var rule in StyleRules(p.Style)) sb.AppendLine("- " + rule);
        if (!string.IsNullOrWhiteSpace(p.CustomStyleNotes)) sb.AppendLine("- Note stile personalizzate: " + p.CustomStyleNotes.Trim());

        sb.AppendLine("- Ogni tavola deve avere un soggetto/composizione distinta ma restare coerente con eventuali regole Consistent e paradigmi assegnati.");
        sb.AppendLine("- Non ritagliare parti importanti del soggetto e non posizionare dettagli essenziali troppo vicino ai bordi o al margine di sicurezza.");
        return sb.ToString().Trim();
    }

    private static IEnumerable<string> StyleRules(string style)
    {
        if (style.Contains("Bold & Easy", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Bold & Easy: contorni molto leggibili e prevalentemente spessi, forme grandi, semplici e ben separate.";
            yield return "Bold & Easy: pochi elementi principali per pagina, bassa densità, dettagli ridotti e niente zone minuscole.";
            yield return "Bold & Easy: composizione immediata, forte leggibilità e ampie aree da colorare.";
            yield break;
        }
        if (style.Contains("Line Art dettagliata", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Line Art dettagliata: dettaglio ricco ma ancora colorabile; linee nitide, niente chiaroscuro pittorico.";
            yield return "Distribuire il dettaglio senza creare rumore visivo o aree troppo piccole.";
            yield break;
        }
        if (style.Contains("Line Art", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Line Art pulita: disegno a contorno nitido, uniforme e professionale, senza rendering pittorico.";
            yield return "Usare gerarchie di linea semplici e leggibili per separare soggetto, dettagli e sfondo.";
            yield break;
        }
        if (style.Contains("Kawaii", StringComparison.OrdinalIgnoreCase) || style.Contains("Cartoon", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Kawaii/Cartoon: forme morbide, proporzioni semplici ed espressive, contorni chiari e aree ampie da colorare.";
            yield return "Evitare texture realistiche e micro-dettagli che contrastino con la semplicità cartoon.";
            yield break;
        }
        if (style.Contains("Mandala", StringComparison.OrdinalIgnoreCase) || style.Contains("Pattern", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Mandala/Pattern: struttura ordinata, ritmo visivo, simmetria o ripetizione coerente quando appropriato.";
            yield return "Mantenere tutte le celle/aree sufficientemente grandi da essere colorate comodamente.";
            yield break;
        }
        if (style.Contains("realistico", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Realistico semplificato: proporzioni credibili ma convertite in line art chiara e colorabile, senza ombre fotografiche.";
            yield return "Ridurre texture e dettagli fini alle sole informazioni utili alla riconoscibilità del soggetto.";
        }
    }
}
