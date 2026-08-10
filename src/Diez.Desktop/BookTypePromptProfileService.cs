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
        public string SubjectDescription { get; set; } = string.Empty;
        public string EnvironmentDescription { get; set; } = string.Empty;
        public string Style { get; set; } = "Bold & Easy";
        public string TargetAudience { get; set; } = "Bambini 6–9 anni";
        public string Difficulty { get; set; } = "Facile";
        public string LineWeight { get; set; } = "Spesso — Bold";
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
    public static readonly string[] LineWeights =
    [
        "Molto spesso — Extra Bold",
        "Spesso — Bold",
        "Medio",
        "Sottile — Fine",
        "Molto sottile — Extra Fine",
        "Variabile — contorni principali più spessi, dettagli più sottili"
    ];
    public static readonly string[] Complexities = ["Molto bassa", "Bassa", "Media", "Alta"];
    public static readonly string[] Densities = ["Molto bassa", "Bassa", "Media", "Alta"];
    public static readonly string[] Backgrounds = ["Nessuno / bianco", "Semplice / minimo", "Contestuale leggero", "Dettagliato"];
    public static readonly string[] WhiteSpaces = ["Molto ampio", "Ampio", "Medio", "Compatto"];

    public static ColoringProfile LoadColoring(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, ColoringEntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return new ColoringProfile();
        try
        {
            var profile = JsonSerializer.Deserialize<ColoringProfile>(entity.Notes, JsonOptions) ?? new ColoringProfile();
            profile.LineWeight = NormalizeLineWeight(profile.LineWeight);
            profile.BlackAndWhiteOnly = true;
            profile.NoGray = true;
            profile.NoShadows = true;
            return profile;
        }
        catch { return new ColoringProfile(); }
    }

    public static void SaveColoring(PreviewProject project, ColoringProfile profile)
    {
        profile.LineWeight = NormalizeLineWeight(profile.LineWeight);
        profile.BlackAndWhiteOnly = true;
        profile.NoGray = true;
        profile.NoShadows = true;
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

        sb.AppendLine("SOGGETTO/I RICHIESTI DALL'UTENTE:");
        if (string.IsNullOrWhiteSpace(p.SubjectDescription))
            sb.AppendLine("- Non specificato: Diez/AI può proporre soggetti coerenti con tema, stile e fascia scelta, senza contraddire gli altri vincoli.");
        else
            sb.AppendLine("- " + p.SubjectDescription.Trim());

        sb.AppendLine("AMBIENTE / SCENARIO RICHIESTO DALL'UTENTE:");
        if (string.IsNullOrWhiteSpace(p.EnvironmentDescription))
            sb.AppendLine("- Non specificato: usa uno scenario compatibile con soggetto, stile, densità e regole dello sfondo selezionate.");
        else
            sb.AppendLine("- " + p.EnvironmentDescription.Trim());

        sb.AppendLine($"- Stile principale: {p.Style}.");
        sb.AppendLine($"- Pubblico / fascia: {p.TargetAudience}.");
        sb.AppendLine($"- Difficoltà di colorazione: {p.Difficulty}.");
        sb.AppendLine($"- Spessore linee selezionato: {p.LineWeight}.");
        sb.AppendLine("- " + LineWeightRule(p.LineWeight));
        sb.AppendLine($"- Complessità visiva: {p.Complexity}.");
        sb.AppendLine($"- Densità di elementi: {p.ElementDensity}.");
        sb.AppendLine($"- Sfondo: {p.Background}.");
        sb.AppendLine($"- Spazio bianco: {p.WhiteSpace}.");

        sb.AppendLine("- VINCOLO CROMATICO ASSOLUTO: l'immagine finale deve contenere ESATTAMENTE DUE SOLI COLORI: nero puro #000000 e bianco puro #FFFFFF.");
        sb.AppendLine("- Non sono ammessi grigi, mezzetinte, colori, gradienti, ombre, sfumature, texture tonali o livelli cromatici intermedi.");
        sb.AppendLine("- Il fondo è bianco puro e tutte le linee/aree scure sono nero puro; nessun terzo valore cromatico deve comparire nel risultato finale.");
        sb.AppendLine("- Se il formato raster introduce antialiasing grigio, il risultato finale deve essere normalizzato/binarizzato a solo nero e bianco prima dell'uso editoriale.");
        sb.AppendLine("- Obiettivo: produrre pagine realmente colorabili, leggibili a colpo d'occhio e adatte alla stampa, non semplici illustrazioni monocromatiche.");
        if (p.CleanContours) sb.AppendLine("- Contorni puliti, continui e facilmente distinguibili; evitare linee sporche, doppie o frammentate.");
        if (p.ClosedAreas) sb.AppendLine("- Preferire aree chiuse e chiaramente delimitate, facili da colorare senza ambiguità.");
        if (p.AvoidTinyAreas) sb.AppendLine("- Evitare micro-aree, dettagli minuscoli o incroci di linee che rendano difficile la colorazione.");
        if (p.SubjectClearlySeparated) sb.AppendLine("- Soggetto principale chiaramente separato dallo sfondo e leggibile anche in miniatura.");
        if (p.NoTextInsideImage) sb.AppendLine("- Nessun testo, lettera, numero, watermark, firma, ID, didascalia o nome file dentro l'immagine.");

        foreach (var rule in StyleRules(p.Style)) sb.AppendLine("- " + rule);
        if (!string.IsNullOrWhiteSpace(p.CustomStyleNotes)) sb.AppendLine("- Note stile personalizzate: " + p.CustomStyleNotes.Trim());

        sb.AppendLine("- Ogni tavola deve avere un soggetto/composizione distinta ma restare coerente con eventuali regole Consistent e paradigmi assegnati.");
        sb.AppendLine("- Non ritagliare parti importanti del soggetto e non posizionare dettagli essenziali troppo vicino ai bordi o al margine di sicurezza.");
        return sb.ToString().Trim();
    }

    private static string NormalizeLineWeight(string? value) => value switch
    {
        "Molto spesso" => "Molto spesso — Extra Bold",
        "Spesso" => "Spesso — Bold",
        "Sottile" => "Sottile — Fine",
        "Molto sottile" => "Molto sottile — Extra Fine",
        null or "" => "Spesso — Bold",
        _ when LineWeights.Contains(value, StringComparer.Ordinal) => value!,
        _ => "Spesso — Bold"
    };

    private static string LineWeightRule(string value)
    {
        if (value.StartsWith("Molto spesso", StringComparison.Ordinal))
            return "Usa linee molto spesse e dominanti, adatte a Bold & Easy e a soggetti con grandi aree da colorare.";
        if (value.StartsWith("Spesso", StringComparison.Ordinal))
            return "Usa linee spesse, uniformi e molto leggibili, senza appesantire i dettagli interni.";
        if (value == "Medio")
            return "Usa linee di spessore medio, nitide e uniformi, con buon equilibrio tra leggibilità e dettaglio.";
        if (value.StartsWith("Molto sottile", StringComparison.Ordinal))
            return "Usa linee molto sottili solo se restano perfettamente nere, continue, separate e leggibili alla dimensione di stampa finale.";
        if (value.StartsWith("Sottile", StringComparison.Ordinal))
            return "Usa linee sottili e nitide, adatte a illustrazioni dettagliate, senza grigi o perdita di continuità alla stampa.";
        return "Usa gerarchia di spessori: contorno principale più spesso, dettagli interni più sottili, sempre in nero puro e ben separati.";
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
            yield return "Line Art dettagliata: dettaglio ricco ma ancora colorabile; le linee possono essere anche sottili, purché nitide, continue, ben separate e stampabili.";
            yield return "Line Art dettagliata: usare linee sottili dove servono al dettaglio senza trasformarle in grigi, texture tonali o chiaroscuro.";
            yield return "Distribuire il dettaglio senza creare rumore visivo o aree troppo piccole da colorare.";
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
