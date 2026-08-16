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
        public string Style { get; set; } = "Clean Line Art";
        public bool BoldEasy { get; set; }
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

    /// <summary>
    /// Exactly one visual style per profile. Bold & Easy and Cozy are deliberately NOT style entries:
    /// both are independent bidirectional HARD production parameters and can be combined with any style.
    /// </summary>
    public static readonly string[] ColoringStyles =
    [
        "Kawaii",
        "Cartoon",
        "Chibi",
        "Cute & Playful",
        "Whimsical",
        "Storybook",
        "Fairy-Tale",
        "Cottagecore",
        "Clean Line Art",
        "Detailed Line Art",
        "Minimal",
        "Simple Shapes",
        "Realistic Simplified",
        "Semi-Realistic",
        "Botanical",
        "Nature Journal",
        "Folk Art",
        "Scandinavian",
        "Boho",
        "Vintage",
        "Retro",
        "Mid-Century",
        "Art Nouveau",
        "Art Deco",
        "Mandala",
        "Zentangle",
        "Geometric",
        "Pattern",
        "Stained Glass",
        "Woodcut / Linocut",
        "Tattoo Flash",
        "Fantasy",
        "Gothic",
        "Steampunk",
        "Doodle",
        "Comic",
        "Manga",
        "Anime-inspired",
        "Custom"
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
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return NormalizeColoringProfile(new ColoringProfile());
        try
        {
            var profile = JsonSerializer.Deserialize<ColoringProfile>(entity.Notes, JsonOptions) ?? new ColoringProfile();

            // Backward-compatible semantic migration. Old combined/orthogonal values must not survive as
            // renderer-facing style choices in the new model.
            if (string.Equals(profile.Style, "Bold & Easy", StringComparison.OrdinalIgnoreCase))
            {
                profile.Style = "Clean Line Art";
                profile.BoldEasy = true;
            }
            else if (string.Equals(profile.Style, "Cozy", StringComparison.OrdinalIgnoreCase))
            {
                profile.Style = "Clean Line Art";
                ColoringCozyPolicyStore.Save(project, true);
            }
            else if (string.Equals(profile.Style, "Kawaii / Cartoon", StringComparison.OrdinalIgnoreCase))
            {
                profile.Style = "Kawaii";
            }

            return NormalizeColoringProfile(profile);
        }
        catch { return NormalizeColoringProfile(new ColoringProfile()); }
    }

    public static void SaveColoring(PreviewProject project, ColoringProfile profile)
    {
        profile = NormalizeColoringProfile(profile);
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
        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
            return BuildColoringBlock(LoadColoring(project));
        if (string.Equals(type, BookTypeProfileService.ImageCollection, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase))
            return ImageCollectionPromptProfileService.BuildPromptBlock(project);

        return $"PROFILO EDITORIALE DEL TIPO LIBRO:\n- Tipo libro: {type}.\n- Mantieni struttura, tono, output e vincoli coerenti con questo Tipo libro.\n- Non omettere requisiti editoriali o tecnici impliciti nel formato scelto.";
    }

    public static string BuildColoringBlock(ColoringProfile source)
    {
        var p = NormalizeColoringProfile(source);
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

        sb.AppendLine($"- STILE — HARD: {p.Style}.");
        sb.AppendLine($"- BOLD & EASY — HARD: {(p.BoldEasy ? "ON" : "OFF")}.");
        sb.AppendLine("- " + BoldEasyRuleItalian(p.BoldEasy));
        if (IsThinLineWeight(p.LineWeight))
            sb.AppendLine("- CONFLITTO RISOLTO — HARD: lo spessore linee sottile/fine impone BOLD & EASY = OFF. Non ispessire le linee e non trasformare la pagina in un profilo Bold & Easy.");
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
        sb.AppendLine("- Non ritagliare parti importanti del soggetto; il posizionamento editoriale finale viene gestito dall'impaginazione.");
        return sb.ToString().Trim();
    }

    public static bool IsThinLineWeight(string? value)
    {
        var normalized = NormalizeLineWeight(value);
        return normalized.StartsWith("Sottile", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Molto sottile", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeColoringStyle(string? value)
    {
        var style = (value ?? string.Empty).Trim();
        if (style.Length == 0) return "Clean Line Art";
        return style switch
        {
            "Line Art pulita" => "Clean Line Art",
            "Line Art dettagliata" => "Detailed Line Art",
            "Stile realistico semplificato" => "Realistic Simplified",
            "Mandala / Pattern" => "Mandala",
            "Personalizzato" => "Custom",
            "Kawaii / Cartoon" => "Kawaii",
            "Bold & Easy" => "Clean Line Art",
            "Cozy" => "Clean Line Art",
            _ when ColoringStyles.Contains(style, StringComparer.OrdinalIgnoreCase) =>
                ColoringStyles.First(x => string.Equals(x, style, StringComparison.OrdinalIgnoreCase)),
            _ => "Custom"
        };
    }

    public static string StyleHardDirectiveEnglish(string? style)
    {
        var s = NormalizeColoringStyle(style);
        return s switch
        {
            "Kawaii" => "Use unmistakably cute Kawaii traits: simplified rounded forms, relatively large expressive eyes/head where appropriate, tiny/simple features and friendly charm; reject realistic natural-history rendering.",
            "Cartoon" => "Use unmistakably cartoon construction: simplified stylized anatomy, expressive features, clear shape language and intentionally non-photorealistic proportions; reject documentary/naturalistic rendering.",
            "Chibi" => "Use chibi proportions with an intentionally oversized head, compact simplified body, cute expressive face and reduced anatomical detail.",
            "Cute & Playful" => "Use friendly playful expressions, softened proportions, approachable forms and lively but simple visual storytelling.",
            "Whimsical" => "Use imaginative, charming, slightly fantastical shape language and playful visual details while keeping the subject readable and colorable.",
            "Storybook" => "Use polished children's-storybook illustration language with clear narrative staging, expressive characters and coherent simplified scenery.",
            "Fairy-Tale" => "Use an enchanted fairy-tale mood, graceful fantasy motifs and storybook-like stylization rather than modern documentary realism.",
            "Cottagecore" => "Use gentle rustic countryside motifs, cozy natural details, handmade charm and calm pastoral styling.",
            "Clean Line Art" => "Use clean controlled contours, deliberate line hierarchy, minimal stray marks and no painterly or tonal rendering.",
            "Detailed Line Art" => "Use rich controlled line detail and intricate but still colorable regions; detail must remain deliberate, separated and print-legible.",
            "Minimal" => "Use very few deliberate elements, strong negative space, economical contours and no unnecessary decorative detail.",
            "Simple Shapes" => "Build the illustration from clearly readable simplified organic shapes with low detail and large colorable regions, while still looking professionally drawn rather than primitive.",
            "Realistic Simplified" => "Keep believable anatomy/proportions but simplify textures and details into clear coloring-book line art with no photographic shading.",
            "Semi-Realistic" => "Keep recognizable realistic structure with moderate stylization and simplified line treatment; avoid photographic tonal rendering.",
            "Botanical" => "Use elegant botanical line-art conventions with recognizable plant forms, graceful contours and organized natural detail.",
            "Nature Journal" => "Use observational nature-illustration clarity with organized specimen-like detail, but keep the page colorable and free of text unless requested.",
            "Folk Art" => "Use decorative folk-art simplification, rhythmic motifs, handcrafted symmetry/asymmetry and distinctive ornamental shapes.",
            "Scandinavian" => "Use clean Scandinavian simplicity, balanced negative space, calm geometric/organic forms and restrained ornament.",
            "Boho" => "Use relaxed bohemian motifs, organic decorative forms and balanced ornamental rhythm without visual clutter.",
            "Vintage" => "Use period-inspired vintage illustration character and ornament while keeping line work clean and intentionally aged-looking rather than dirty or degraded.",
            "Retro" => "Use clearly retro graphic shape language and period-inspired simplified forms rather than contemporary realism.",
            "Mid-Century" => "Use mid-century modern stylization: simplified geometry, playful asymmetry, strong shape design and restrained detail.",
            "Art Nouveau" => "Use flowing organic curves, elegant decorative borders/motifs and sinuous natural forms characteristic of Art Nouveau.",
            "Art Deco" => "Use bold geometric symmetry, streamlined decorative structure and elegant Art Deco shape language.",
            "Mandala" => "Use centered organized radial structure, deliberate symmetry and comfortably colorable repeated regions.",
            "Zentangle" => "Use structured repetitive line patterns with intentional rhythm and separated colorable pattern cells; avoid random scribble texture.",
            "Geometric" => "Use deliberate geometric construction, clear repeated shapes and crisp organized spatial relationships.",
            "Pattern" => "Use a coherent repeat/pattern logic with intentional motif rhythm and clearly separated colorable areas.",
            "Stained Glass" => "Use stained-glass-like segmented regions bounded by strong clean contours, with deliberate large colorable cells.",
            "Woodcut / Linocut" => "Use deliberate carved-print line character and simplified high-contrast shapes without drifting into noisy accidental hatching or photographic engraving.",
            "Tattoo Flash" => "Use bold tattoo-flash composition, iconic silhouettes, confident clean contours and controlled internal detail.",
            "Fantasy" => "Use clearly fantastical design language, imaginative forms and coherent magical details while maintaining subject readability.",
            "Gothic" => "Use dark Gothic decorative motifs and dramatic stylization while preserving clean coloring-book contours and readable forms.",
            "Steampunk" => "Use coherent steampunk visual language with Victorian-mechanical motifs, gears/details and stylized industrial ornament without cluttering the focal subject.",
            "Doodle" => "Use intentional polished doodle-style line art with playful motifs and coherent composition; never resemble an unfinished scribble.",
            "Comic" => "Use comic-style shape language, expressive contours and dynamic readable staging without halftone/grayscale unless explicitly allowed by the Book Type.",
            "Manga" => "Use recognizable manga-style facial/anatomical stylization and clean ink-like contours, adapted to pure black/white coloring-page output.",
            "Anime-inspired" => "Use anime-inspired proportions, facial language and clean stylized contours, adapted to a colorable black/white page.",
            "Custom" => "Follow the user's custom style notes as the visible style authority; do not substitute a default house style.",
            _ => "The selected style must be clearly visible in the finished artwork and must not be replaced by another professional style."
        };
    }

    public static string BoldEasyHardDirectiveEnglish(bool enabled) => enabled
        ? "BOLD & EASY — HARD: ON. Use large simple readable forms, low visual clutter, broad colorable regions, restrained interior detail and confident easy-to-follow contours. Do not return a dense intricate page that merely has thick outlines."
        : "BOLD & EASY — HARD: OFF. Do not impose a Bold & Easy simplification profile. Do not automatically enlarge/simplify forms, reduce detail, or thicken contours to make the page Bold & Easy; obey the selected style, line weight, complexity and density exactly.";

    private static ColoringProfile NormalizeColoringProfile(ColoringProfile profile)
    {
        profile.Style = NormalizeColoringStyle(profile.Style);
        profile.LineWeight = NormalizeLineWeight(profile.LineWeight);
        // Bidirectional HARD rule: requesting genuinely thin/fine contours is incompatible with Bold & Easy.
        // Resolve this before prompt generation so the model never sees contradictory instructions.
        if (IsThinLineWeight(profile.LineWeight)) profile.BoldEasy = false;
        profile.BlackAndWhiteOnly = true;
        profile.NoGray = true;
        profile.NoShadows = true;
        return profile;
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
            return "Usa linee molto spesse e dominanti, senza sostituire lo stile visivo selezionato.";
        if (value.StartsWith("Spesso", StringComparison.Ordinal))
            return "Usa linee spesse, uniformi e molto leggibili, senza appesantire i dettagli interni.";
        if (value == "Medio")
            return "Usa linee di spessore medio, nitide e uniformi, con buon equilibrio tra leggibilità e dettaglio.";
        if (value.StartsWith("Molto sottile", StringComparison.Ordinal))
            return "HARD: usa linee molto sottili/fini, perfettamente nere, continue, separate e leggibili alla stampa; NON convertirle in contorni Bold & Easy.";
        if (value.StartsWith("Sottile", StringComparison.Ordinal))
            return "HARD: usa linee sottili/fini e nitide; NON ispessirle per una resa Bold & Easy.";
        return "Usa gerarchia di spessori: contorno principale più spesso, dettagli interni più sottili, sempre in nero puro e ben separati.";
    }

    private static string BoldEasyRuleItalian(bool enabled) => enabled
        ? "BOLD & EASY ON — HARD: forme grandi e semplici, regioni ampie da colorare, dettaglio interno ridotto, bassa confusione visiva e contorni facili da seguire, senza sostituire lo stile scelto."
        : "BOLD & EASY OFF — HARD: non applicare automaticamente semplificazione, forme sovradimensionate, riduzione del dettaglio o ispessimento dei contorni tipici del profilo Bold & Easy; rispettare stile, spessore linee, complessità e densità selezionati.";

    private static IEnumerable<string> StyleRules(string style)
    {
        var normalized = NormalizeColoringStyle(style);
        yield return $"STILE — HARD: il risultato deve essere visivamente riconoscibile come '{normalized}'. Uno stile professionale diverso non è equivalente e va rigenerato.";

        switch (normalized)
        {
            case "Kawaii":
                yield return "Kawaii: forme arrotondate, occhi/viso espressivi e proporzioni volutamente cute; vietata la deriva naturalistica/realistica.";
                break;
            case "Cartoon":
                yield return "Cartoon: anatomia e proporzioni chiaramente stilizzate, espressioni leggibili e shape language non fotografico.";
                break;
            case "Chibi":
                yield return "Chibi: testa volutamente sovradimensionata, corpo compatto e semplificato, espressione cute e dettaglio anatomico ridotto.";
                break;
            case "Clean Line Art":
                yield return "Clean Line Art: contorni puliti e controllati, gerarchia di linea deliberata, niente rendering pittorico o segni casuali.";
                break;
            case "Detailed Line Art":
                yield return "Detailed Line Art: dettaglio ricco e controllato, regioni ancora colorabili, linee separate e stampabili; niente rumore casuale.";
                break;
            case "Minimal":
                yield return "Minimal: pochissimi elementi deliberati, forte spazio negativo e nessun dettaglio decorativo non necessario.";
                break;
            case "Realistic Simplified":
                yield return "Realistic Simplified: anatomia credibile ma texture e micro-dettagli ridotti a line art chiara, senza ombre fotografiche.";
                break;
            case "Mandala":
                yield return "Mandala: struttura radiale ordinata, simmetria deliberata e celle sufficientemente grandi da colorare.";
                break;
            case "Zentangle":
                yield return "Zentangle: pattern strutturati e ritmici, non scarabocchi casuali; celle/pattern leggibili e deliberati.";
                break;
            case "Woodcut / Linocut":
                yield return "Woodcut/Linocut: carattere inciso deliberato e forme ad alto contrasto; evitare hatching rumoroso o deriva verso incisione fotografica.";
                break;
            case "Custom":
                yield return "Custom: le Note stile personalizzate sono l'autorità visiva e non vanno sostituite da uno stile predefinito.";
                break;
            default:
                yield return $"{normalized}: applicare in modo evidente i tratti distintivi dello stile selezionato mantenendo il risultato realmente colorabile e professionale.";
                break;
        }
    }
}
