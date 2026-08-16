using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

/// <summary>
/// Native prompt profile for image collections and editorial illustration sets.
/// Unlike Coloring Book, color treatment is selectable and may include grayscale.
/// </summary>
internal static class ImageCollectionPromptProfileService
{
    private const string EntityKind = "DiezImageCollectionPromptProfile";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal sealed class Profile
    {
        public string SubjectDescription { get; set; } = string.Empty;
        public string EnvironmentDescription { get; set; } = string.Empty;
        public string EditorialUse { get; set; } = "Illustrazione editoriale / saggio";
        public string ColorMode { get; set; } = "Scala di grigi — con sfumature";
        public string DetailLevel { get; set; } = "Medio";
        public string LineTreatment { get; set; } = "Contorno medio";
        public string RenderingStyle { get; set; } = "Illustrativo chiaro";
        public string Background { get; set; } = "Semplice / funzionale";
        public string Viewpoint { get; set; } = "Variabile secondo il soggetto";
        public bool KeepSubjectReadable { get; set; } = true;
        public bool AvoidTextInsideImage { get; set; } = true;
        public bool EditorialClarity { get; set; } = true;
        public bool SameScaleWhenSeries { get; set; } = true;
        public string Notes { get; set; } = string.Empty;
    }

    public static readonly string[] EditorialUses =
    [
        "Illustrazione editoriale / saggio",
        "Sequenza di esercizi / movimenti",
        "Illustrazione didattica",
        "Figura tecnica / manuale",
        "Schema anatomico semplificato",
        "Serie di riferimento coerente",
        "Raccolta artistica / concettuale",
        "Decorazione editoriale"
    ];

    public static readonly string[] ColorModes =
    [
        "Colore pieno",
        "Colore limitato / palette controllata",
        "Scala di grigi — con sfumature",
        "Bianco e nero puro — 2 colori",
        "Monocromatico — una tinta + bianco",
        "Automatico secondo il contenuto"
    ];

    public static readonly string[] DetailLevels =
    [
        "Molto schematico",
        "Basso",
        "Medio",
        "Alto",
        "Molto alto"
    ];

    public static readonly string[] LineTreatments =
    [
        "Senza contorno dominante",
        "Contorno molto sottile",
        "Contorno sottile",
        "Contorno medio",
        "Contorno spesso",
        "Contorno variabile"
    ];

    public static readonly string[] RenderingStyles =
    [
        "Illustrativo chiaro",
        "Line art editoriale",
        "Infografico / didattico",
        "Realistico semplificato",
        "Tecnico pulito",
        "Pittorico controllato",
        "Fotografico / realistico",
        "Personalizzato"
    ];

    public static readonly string[] Backgrounds =
    [
        "Nessuno / trasparente se supportato",
        "Bianco pulito",
        "Semplice / funzionale",
        "Contestuale leggero",
        "Ambientato / completo"
    ];

    public static readonly string[] Viewpoints =
    [
        "Variabile secondo il soggetto",
        "Frontale",
        "Tre quarti",
        "Laterale",
        "Dall'alto",
        "Stesso punto di vista per tutta la serie"
    ];

    public static Profile Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return new Profile();
        try { return JsonSerializer.Deserialize<Profile>(entity.Notes, JsonOptions) ?? new Profile(); }
        catch { return new Profile(); }
    }

    public static void Save(PreviewProject project, Profile profile)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity { Kind = EntityKind, Name = "Profilo Raccolta immagini", IsCandidate = false };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(profile, JsonOptions);
    }

    public static string BuildPromptBlock(PreviewProject project)
    {
        var p = Load(project);
        var sb = new StringBuilder();
        sb.AppendLine("PROFILO EDITORIALE RACCOLTA IMMAGINI:");
        sb.AppendLine($"- Uso editoriale: {p.EditorialUse}.");
        sb.AppendLine($"- Resa cromatica: {p.ColorMode}.");
        sb.AppendLine("- " + ColorModeRule(p.ColorMode));
        sb.AppendLine($"- Livello di dettaglio: {p.DetailLevel}.");
        sb.AppendLine($"- Trattamento linee/contorni: {p.LineTreatment}.");
        sb.AppendLine($"- Stile di resa: {p.RenderingStyle}.");
        sb.AppendLine($"- Sfondo: {p.Background}.");
        sb.AppendLine($"- Punto di vista: {p.Viewpoint}.");

        sb.AppendLine("SOGGETTO/I RICHIESTI DALL'UTENTE:");
        sb.AppendLine(string.IsNullOrWhiteSpace(p.SubjectDescription)
            ? "- Non specificato: proponi soggetti coerenti con l'uso editoriale e con le altre istruzioni."
            : "- " + p.SubjectDescription.Trim());
        sb.AppendLine("AMBIENTE / SCENARIO RICHIESTO DALL'UTENTE:");
        sb.AppendLine(string.IsNullOrWhiteSpace(p.EnvironmentDescription)
            ? "- Non specificato: usa un ambiente funzionale al contenuto, senza aggiungere dettagli inutili."
            : "- " + p.EnvironmentDescription.Trim());

        if (p.KeepSubjectReadable)
            sb.AppendLine("- Il soggetto principale deve essere immediatamente leggibile e separabile dallo sfondo, anche in dimensione editoriale ridotta.");
        if (p.AvoidTextInsideImage)
            sb.AppendLine("- Non inserire testo, numeri, etichette o didascalie dentro l'immagine salvo richiesta esplicita; Diez può aggiungerli successivamente come contenuto editoriale separato.");
        if (p.EditorialClarity)
            sb.AppendLine("- Privilegia chiarezza editoriale e comprensione rispetto a decorazioni che non aiutano il contenuto.");
        if (p.SameScaleWhenSeries)
            sb.AppendLine("- Quando le immagini appartengono a una sequenza comparabile, mantieni scala, proporzioni e inquadratura coerenti salvo variazione necessaria.");

        foreach (var rule in UseRules(p.EditorialUse, p.DetailLevel)) sb.AppendLine("- " + rule);
        if (!string.IsNullOrWhiteSpace(p.Notes)) sb.AppendLine("- Note aggiuntive dell'utente: " + p.Notes.Trim());
        return sb.ToString().Trim();
    }

    private static string ColorModeRule(string mode)
    {
        if (mode.StartsWith("Scala di grigi", StringComparison.OrdinalIgnoreCase))
            return "Usa solo valori dal bianco al nero, includendo sfumature di grigio quando utili a volume, profondità, separazione dei piani o leggibilità.";
        if (mode.StartsWith("Bianco e nero puro", StringComparison.OrdinalIgnoreCase))
            return "Usa esclusivamente nero puro #000000 e bianco puro #FFFFFF, senza grigi o valori intermedi.";
        if (mode.StartsWith("Colore limitato", StringComparison.OrdinalIgnoreCase))
            return "Usa una palette ridotta e coerente; evita colori casuali che non abbiano funzione editoriale o semantica.";
        if (mode.StartsWith("Monocromatico", StringComparison.OrdinalIgnoreCase))
            return "Usa una sola tinta principale con bianco e, se necessario, variazioni tonali coerenti della stessa tinta.";
        if (mode.StartsWith("Colore pieno", StringComparison.OrdinalIgnoreCase))
            return "Il colore è ammesso pienamente, ma deve restare coerente con soggetto, stile, scopo editoriale e Consistent.";
        return "Scegli la resa cromatica più utile alla comprensione e allo scopo editoriale, mantenendola coerente nella serie quando Consistent è attivo.";
    }

    private static IEnumerable<string> UseRules(string use, string detail)
    {
        if (use.Contains("esercizi", StringComparison.OrdinalIgnoreCase) || use.Contains("movimenti", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Sequenza esercizi: mostra con chiarezza posizione iniziale, azione e posizione finale; ogni figura deve rendere leggibile postura, appoggi e direzione del movimento.";
            yield return "Mantieni lo stesso soggetto/modello, abbigliamento, proporzioni e stile lungo la sequenza quando la continuità aiuta la comprensione.";
            yield return "Evita pose anatomiche ambigue, arti sovrapposti inutilmente o prospettive che nascondano il gesto da spiegare.";
        }
        else if (use.Contains("didattica", StringComparison.OrdinalIgnoreCase) || use.Contains("manuale", StringComparison.OrdinalIgnoreCase) || use.Contains("tecnica", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Illustrazione didattica/tecnica: evidenzia la relazione fra parti, azioni e oggetti; elimina dettagli decorativi che riducono la leggibilità.";
            yield return "Usa proporzioni, orientamento e gerarchia visiva consistenti per facilitare il confronto fra figure diverse.";
        }
        else if (use.Contains("anatomico", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Schema anatomico semplificato: privilegia correttezza delle proporzioni e chiarezza delle parti rilevanti, senza realismo superfluo.";
        }
        else if (use.Contains("saggio", StringComparison.OrdinalIgnoreCase) || use.Contains("editoriale", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Illustrazione per saggio: l'immagine deve aggiungere informazione o comprensione al testo, non essere semplice riempitivo decorativo.";
            yield return "Mantieni una resa sobria e coerente con un impaginato editoriale, con sufficiente contrasto anche in stampa.";
        }

        if (detail == "Molto schematico")
            yield return "Dettaglio molto schematico: conserva solo forme, gesti e relazioni essenziali alla comprensione.";
        else if (detail == "Basso")
            yield return "Dettaglio basso: pochi elementi ben definiti, nessun micro-dettaglio non necessario.";
        else if (detail == "Alto")
            yield return "Dettaglio alto: aggiungi informazioni visive utili senza compromettere gerarchia e leggibilità.";
        else if (detail == "Molto alto")
            yield return "Dettaglio molto alto: resa ricca e precisa, mantenendo comunque chiari soggetto principale e funzione editoriale.";
        else
            yield return "Dettaglio medio: equilibrio tra chiarezza, informazione e pulizia grafica.";
    }
}
