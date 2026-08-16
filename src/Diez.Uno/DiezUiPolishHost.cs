using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DiezPublishingStudio.UnoSpike;

/// <summary>
/// Presentation-only layer for the Uno frontend.
/// It keeps persisted/core values canonical while rendering friendly Italian labels.
/// </summary>
internal sealed class DiezUiPolishHost : ContentControl
{
    private static readonly SolidColorBrush Napoli = Brush("#007FFF");
    private static readonly SolidColorBrush NapoliDark = Brush("#005EB8");
    private static readonly SolidColorBrush NapoliDeep = Brush("#004A91");
    private static readonly SolidColorBrush NapoliSoft = Brush("#D9ECFF");
    private static readonly SolidColorBrush NapoliVerySoft = Brush("#EFF7FF");
    private static readonly SolidColorBrush White = Brush("#FFFFFF");
    private static readonly SolidColorBrush Ink = Brush("#12304A");
    private static readonly SolidColorBrush BorderBlue = Brush("#9CCFFF");

    private bool _applying;

    public DiezUiPolishHost(UIElement content)
    {
        Content = content;
        Background = Napoli;
        Loaded += (_, _) => ApplyNow();
        LayoutUpdated += (_, _) => ApplyNow();
    }

    private void ApplyNow()
    {
        if (_applying || Content is not DependencyObject root) return;
        _applying = true;
        try { PolishTree(root); }
        finally { _applying = false; }
    }

    private static void PolishTree(DependencyObject node)
    {
        Polish(node);
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
            PolishTree(VisualTreeHelper.GetChild(node, i));
    }

    private static void Polish(DependencyObject node)
    {
        switch (node)
        {
            case Page page:
                page.Background = Napoli;
                break;

            case ScrollViewer scroll:
                scroll.Background = Grid.GetColumn(scroll) == 0 ? NapoliDeep : Napoli;
                break;

            case Border border:
                border.Background = NapoliDark;
                border.BorderBrush = BorderBlue;
                break;

            case TextBlock text:
                if (string.Equals(text.Text, "Diez Publishing Studio", StringComparison.Ordinal))
                {
                    text.Text = "Diez ∞ Publishing Studio";
                    text.FontSize = 24;
                    text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                }
                else
                {
                    text.Text = Italian(text.Text);
                }
                text.Foreground = White;
                break;

            case TextBox box:
                box.Background = White;
                box.Foreground = Ink;
                box.BorderBrush = BorderBlue;
                box.PlaceholderText = Italian(box.PlaceholderText);
                break;

            case ComboBox combo:
                combo.Background = White;
                combo.Foreground = Ink;
                combo.BorderBrush = BorderBlue;
                LocalizeCombo(combo);
                break;

            case Button button:
                button.Background = NapoliDark;
                button.Foreground = White;
                button.BorderBrush = BorderBlue;
                if (button.Content is string buttonText)
                    button.Content = Italian(buttonText);
                break;

            case CheckBox check:
                check.Foreground = White;
                if (check.Content is string checkText)
                    check.Content = Italian(checkText);
                break;

            case ListView list:
                list.Background = NapoliVerySoft;
                list.Foreground = Ink;
                list.BorderBrush = BorderBlue;
                LocalizeStringList(list);
                break;
        }
    }

    private static void LocalizeCombo(ComboBox combo)
    {
        if (combo.ItemsSource is not IEnumerable source || combo.ItemsSource is IEnumerable<UiChoice>) return;

        var raw = source.Cast<object?>().ToList();
        if (raw.Count == 0 || raw.Any(item => item is not string)) return;

        var selected = combo.SelectedItem?.ToString() ?? string.Empty;
        var choices = raw.Cast<string>()
            .Select(value => new UiChoice(value, Italian(value)))
            .ToList();

        combo.DisplayMemberPath = nameof(UiChoice.Display);
        combo.ItemsSource = choices;
        combo.SelectedItem = choices.FirstOrDefault(x => string.Equals(x.Value, selected, StringComparison.OrdinalIgnoreCase))
            ?? choices.FirstOrDefault();
    }

    private static void LocalizeStringList(ListView list)
    {
        if (list.ItemsSource is not IEnumerable source || list.ItemsSource is IEnumerable<UiChoice>) return;
        var raw = source.Cast<object?>().ToList();
        if (raw.Count == 0 || raw.Any(item => item is not string)) return;

        var translated = raw.Cast<string>().Select(Italian).ToList();
        if (translated.SequenceEqual(raw.Cast<string>(), StringComparer.Ordinal)) return;
        list.ItemsSource = translated;
    }

    private sealed record UiChoice(string Value, string Display)
    {
        public override string ToString() => Value;
    }

    private static string Italian(string? source)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return text;

        if (Translations.TryGetValue(text, out var exact)) return exact;

        // Only UI terminology is replaced here. User/project content is never rewritten in storage.
        return text
            .Replace("Home / Progetto", "Progetto", StringComparison.Ordinal)
            .Replace("Percorso libro", "Tipo di libro", StringComparison.Ordinal)
            .Replace("Editable Master", "Testo principale modificabile", StringComparison.Ordinal)
            .Replace("Content Graph / Bible", "Mappa dei contenuti / Guida del progetto", StringComparison.Ordinal)
            .Replace("Consistency Review", "Controllo coerenza", StringComparison.Ordinal)
            .Replace("Revision Candidate", "Proposta di revisione", StringComparison.Ordinal)
            .Replace("AI Production / Human Prompt / Exchange", "Produzione con AI", StringComparison.Ordinal)
            .Replace("AI Production / Exchange", "Produzione con AI", StringComparison.Ordinal)
            .Replace("Prompt Pack", "Pacchetto di istruzioni", StringComparison.Ordinal)
            .Replace("Prompt", "Istruzioni", StringComparison.Ordinal)
            .Replace("prompt", "istruzioni", StringComparison.Ordinal)
            .Replace("Response Review", "Controllo della risposta", StringComparison.Ordinal)
            .Replace("Vision HARD gates", "Controlli obbligatori delle immagini", StringComparison.Ordinal)
            .Replace("Vision HARD", "Controlli obbligatori", StringComparison.Ordinal)
            .Replace("Vision", "Controllo immagini", StringComparison.Ordinal)
            .Replace("HARD", "obbligatorio", StringComparison.Ordinal)
            .Replace("Bold & Easy", "Linee marcate e forme semplici", StringComparison.Ordinal)
            .Replace("Cozy", "Atmosfera accogliente", StringComparison.Ordinal)
            .Replace("Consistent", "Coerenza", StringComparison.Ordinal)
            .Replace("Provider AI", "Servizio AI", StringComparison.Ordinal)
            .Replace("Provider", "Servizio AI", StringComparison.Ordinal)
            .Replace("Human prompt", "Istruzioni per l’AI", StringComparison.Ordinal)
            .Replace("job AI", "attività AI", StringComparison.OrdinalIgnoreCase)
            .Replace("Job", "Attività", StringComparison.Ordinal)
            .Replace("job", "attività", StringComparison.Ordinal)
            .Replace("Output", "Risultato", StringComparison.Ordinal)
            .Replace("Metadata", "Dati dell’edizione", StringComparison.Ordinal)
            .Replace("metadata", "dati dell’edizione", StringComparison.Ordinal)
            .Replace("Bible", "Guida del progetto", StringComparison.Ordinal)
            .Replace("Handoff", "Consegna", StringComparison.Ordinal)
            .Replace("handoff", "consegna", StringComparison.Ordinal)
            .Replace("freeze", "copia bloccata", StringComparison.OrdinalIgnoreCase)
            .Replace("workspace", "area di lavoro", StringComparison.Ordinal)
            .Replace("Workspace", "Area di lavoro", StringComparison.Ordinal)
            .Replace("Review", "Controllo", StringComparison.Ordinal)
            .Replace("review", "controllo", StringComparison.Ordinal)
            .Replace("Export", "Esportazione", StringComparison.Ordinal)
            .Replace("export", "esportazione", StringComparison.Ordinal);
    }

    private static readonly IReadOnlyDictionary<string, string> Translations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Uno Platform · workspace stabile"] = "Il tuo spazio di lavoro",
            ["Home / Progetto"] = "Progetto",
            ["Percorso libro"] = "Tipo di libro",
            ["Visual 1/4 · Quantità"] = "Immagini 1/4 · Quantità e contenuto",
            ["Visual 2/4 · Prompt"] = "Immagini 2/4 · Istruzioni",
            ["Visual 3/4 · Prompt Pack"] = "Immagini 3/4 · Pacchetto per l’AI",
            ["Visual 4/4 · Vision"] = "Immagini 4/4 · Controllo finale",
            ["Scene / Soggetti"] = "Scene e soggetti",
            ["Word Search"] = "Cerca parole",
            ["Narrativa / Manuale"] = "Romanzo, racconto e manuale",
            ["Editable Master"] = "Testo principale modificabile",
            ["Content Graph / Bible"] = "Mappa dei contenuti / Guida del progetto",
            ["Consistency Review"] = "Controllo coerenza",
            ["AI Production / Exchange"] = "Produzione con AI",
            ["Export / Finalizzazione"] = "Esportazione e versione finale",
            ["Libreria finalizzati"] = "Libri finalizzati",
            ["Coloring book"] = "Libro da colorare",
            ["Coloring Book"] = "Libro da colorare",
            ["Quiz / trivia"] = "Quiz e domande",
            ["Catalogo / raccolta dati"] = "Catalogo e raccolta dati",
            ["Altro"] = "Altro tipo di libro",

            ["Kawaii"] = "Kawaii",
            ["Cartoon"] = "Cartone animato",
            ["Chibi"] = "Chibi",
            ["Cute & Playful"] = "Tenero e giocoso",
            ["Whimsical"] = "Fantasioso",
            ["Storybook"] = "Libro illustrato",
            ["Fairy-Tale"] = "Fiaba",
            ["Cottagecore"] = "Cottagecore",
            ["Clean Line Art"] = "Linee pulite",
            ["Detailed Line Art"] = "Linee dettagliate",
            ["Minimal"] = "Minimalista",
            ["Simple Shapes"] = "Forme semplici",
            ["Realistic Simplified"] = "Realistico semplificato",
            ["Semi-Realistic"] = "Semi-realistico",
            ["Botanical"] = "Botanico",
            ["Nature Journal"] = "Taccuino naturalistico",
            ["Folk Art"] = "Arte popolare",
            ["Scandinavian"] = "Scandinavo",
            ["Boho"] = "Boho",
            ["Vintage"] = "Vintage",
            ["Retro"] = "Retrò",
            ["Mid-Century"] = "Metà Novecento",
            ["Art Nouveau"] = "Liberty / Art Nouveau",
            ["Art Deco"] = "Art Déco",
            ["Mandala"] = "Mandala",
            ["Zentangle"] = "Zentangle",
            ["Geometric"] = "Geometrico",
            ["Pattern"] = "Motivo ripetuto",
            ["Stained Glass"] = "Vetrata",
            ["Woodcut / Linocut"] = "Xilografia / linoleografia",
            ["Tattoo Flash"] = "Flash da tatuaggio",
            ["Fantasy"] = "Fantasy",
            ["Gothic"] = "Gotico",
            ["Steampunk"] = "Steampunk",
            ["Doodle"] = "Scarabocchio creativo",
            ["Comic"] = "Fumetto",
            ["Manga"] = "Manga",
            ["Anime-inspired"] = "Ispirato agli anime",
            ["Custom"] = "Personalizzato",

            ["Reference"] = "Riferimento",
            ["Molto spesso — Extra Bold"] = "Molto spesso",
            ["Spesso — Bold"] = "Spesso",
            ["Sottile — Fine"] = "Sottile",
            ["Molto sottile — Extra Fine"] = "Molto sottile",
            ["Bold & Easy — HARD indipendente"] = "Linee marcate e forme semplici — regola obbligatoria",
            ["Cozy — HARD indipendente"] = "Atmosfera accogliente — regola obbligatoria",
            ["Consistent — mantieni coerenti le immagini"] = "Mantieni coerenti le immagini tra loro",
            ["Regole Consistent"] = "Regole di coerenza",
            ["Memorizza profilo Coloring"] = "Salva impostazioni del libro da colorare",
            ["Profilo Coloring memorizzato."] = "Impostazioni del libro da colorare salvate.",
            ["Stile e leggibilità del Coloring"] = "Stile e leggibilità del libro da colorare",

            ["ChatGPT / OpenAI"] = "ChatGPT / OpenAI",
            ["Gemini"] = "Gemini",
            ["Altra / nuova AI"] = "Altra AI",
            ["Image"] = "Immagine",
            ["Text"] = "Testo",
            ["Data"] = "Dati",
            ["Ready"] = "Pronto",
            ["Reviewed"] = "Controllato",
            ["AcceptedException"] = "Eccezione accettata",
            ["Resolved"] = "Risolto",
            ["Open"] = "Aperto",
            ["Locale"] = "Sul computer",
            ["Google Drive / Docs / Sheets"] = "Google Drive / Documenti / Fogli",
            ["Locale + Google"] = "Sul computer + Google",

            ["Provider"] = "Servizio AI",
            ["Provider AI"] = "Servizio AI",
            ["Output"] = "Tipo di risultato",
            ["Human prompt"] = "Istruzioni per l’AI",
            ["Prompt provider-facing"] = "Istruzioni da inviare all’AI",
            ["Note exchange"] = "Note sullo scambio con l’AI",
            ["Crea job AI Ready"] = "Prepara attività AI",
            ["Crea job Ready"] = "Prepara attività AI",
            ["Job"] = "Attività AI",
            ["Response Review"] = "Controlla la risposta",
            ["Copia Prompt Pack"] = "Copia il pacchetto di istruzioni",
            ["Visual 3/4 · Prompt Pack / AI Exchange"] = "Immagini 3/4 · Pacchetto di istruzioni per l’AI",
            ["Visual 4/4 · Response Review / Vision"] = "Immagini 4/4 · Controllo finale",

            ["style_match — HARD"] = "Stile corretto — obbligatorio",
            ["bold_easy_match — HARD quando attivo"] = "Linee marcate e forme semplici — obbligatorio quando scelto",
            ["cozy_match — HARD quando attivo"] = "Atmosfera accogliente — obbligatoria quando scelta",
            ["line_weight_match — HARD"] = "Spessore delle linee corretto — obbligatorio",
            ["single_composition — HARD"] = "Una sola composizione — obbligatorio",
            ["scene_participants_match — HARD"] = "Soggetti corretti nella scena — obbligatorio",
            ["Vision HARD gates"] = "Controlli obbligatori delle immagini",
            ["Candidati / job AI"] = "Immagini e attività AI da controllare",

            ["Outline"] = "Scaletta",
            ["Content Graph / Bible"] = "Mappa dei contenuti / Guida del progetto",
            ["Note Graph / Bible"] = "Note sulla mappa e sulla guida del progetto",
            ["Consistency Review / Revision Candidate"] = "Controllo coerenza / Proposte di revisione",
            ["Review"] = "Controllo",
            ["Memorizza review Uno"] = "Salva il controllo",
            ["AI Production / Human Prompt / Exchange"] = "Produzione con AI",
            ["Configurazione AI"] = "Impostazioni AI",
            ["Brief comune"] = "Regole comuni del progetto",
            ["Export / Edizione / Handoff"] = "Esportazione, edizione e consegna",
            ["Handoff"] = "Consegna",
            ["Metadata edizione"] = "Dati dell’edizione",
            ["Crea snapshot / freeze dell’edizione prima dell’export"] = "Blocca una copia dell’edizione prima di esportare",
            ["Esporta project.json diagnostico"] = "Esporta dati tecnici del progetto",
            ["Libreria libri finalizzati"] = "Libri finalizzati",
            ["Apertura output"] = "Apri un file finale",
            ["Percorso / link"] = "File o collegamento",

            ["La Home Uno usa un albero visivo permanente: progetto, materiali e percorso libro restano nello stesso shell."] = "Qui trovi progetto, materiali e tipo di libro in un unico spazio.",
            ["Crea o apri un .diez. Il pacchetto viene letto e riscritto preservando le sezioni JSON sconosciute e gli allegati già incorporati."] = "Crea o apri un progetto Diez. I materiali già inseriti restano dentro al progetto.",
            ["Titolo e Tipo libro sono salvati nel progetto. La scelta instrada al workspace specializzato senza aprire nuove finestre."] = "Scegli il titolo e il tipo di libro. Diez ti porterà automaticamente nell’area di lavoro più adatta.",
            ["Numero esatto, soggetti, ambientazione, stile e Consistent sono editabili direttamente con controlli Uno."] = "Scegli quante immagini creare e descrivi soggetti, ambientazione, stile e regole di coerenza.",
            ["DEVE FARE, NON DEVE FARE e PROMPT sono TextBox Uno reali e modificabili."] = "Scrivi cosa deve fare l’AI, cosa deve evitare e modifica liberamente le istruzioni finali.",
            ["Seleziona il provider, conserva il prompt master e prepara una richiesta AI senza contaminare il prompt visuale con metadati interni."] = "Scegli l’AI e prepara le istruzioni da inviare. Diez gestisce automaticamente i dati tecnici separati.",
            ["La UI mantiene visibili i gate HARD: stile, Bold & Easy, Cozy, line weight, singola composizione e scene_participants_match."] = "Prima di approvare un’immagine, controlla stile, semplicità, atmosfera, spessore delle linee, composizione e soggetti presenti.",
            ["Le scene usano ID stabili: rinominare numero, nome o descrizione non cambia l'identità. Gli ID archiviati non vengono riciclati."] = "Puoi rinominare scene e soggetti senza perdere i collegamenti già creati.",
            ["Workspace unificato per struttura, contenuti, note, illustrazioni e handoff editoriale."] = "Organizza qui struttura, contenuti, note, illustrazioni e consegna del libro.",
            ["La nuova UI espone la struttura già presente nel .diez e mantiene separati Master modificabile e originali incorporati."] = "Qui modifichi il testo principale senza cambiare i materiali originali importati.",
            ["Gli originali importati rimangono nel pacchetto. In questa prima migrazione Uno l’editor manuale è conservato separatamente finché il servizio EditableMaster viene estratto dal layer Avalonia."] = "I materiali originali restano protetti nel progetto. Le modifiche vengono salvate separatamente nel testo principale.",
            ["Entità, relazioni e Bible già presenti nel progetto restano leggibili durante la migrazione."] = "Qui trovi personaggi, luoghi, relazioni e informazioni di riferimento del progetto.",
            ["La UI porta lo stato dei problemi e i comandi di revisione; gli engine di riconciliazione restano dati core da estrarre dal progetto Avalonia."] = "Controlla qui eventuali incongruenze e scegli come gestirle.",
            ["Provider, brief comune, MUST DO / MUST NOT, job e scambio risposte sono unificati."] = "Scegli l’AI, prepara le istruzioni e gestisci richieste e risposte nello stesso spazio.",
            ["Destinazione locale, Google o entrambe; metadata, freeze ed handoff sono raccolti nella stessa pagina."] = "Scegli dove salvare i file, i formati e i dati dell’edizione.",
            ["Archivio, rigenerazione e destinazioni locali/Google restano disponibili come superficie Uno."] = "Qui trovi le versioni finali già create e i relativi file.",
            ["La logica di apertura locale/Google e rigenerazione verrà collegata ai servizi FinalizedLibrary senza reintrodurre finestre Avalonia."] = "Da qui potrai aprire, copiare o rigenerare una versione finale già salvata."
        };

    private static SolidColorBrush Brush(string hex)
    {
        var value = hex.TrimStart('#');
        return new SolidColorBrush(Color.FromArgb(
            255,
            Convert.ToByte(value[0..2], 16),
            Convert.ToByte(value[2..4], 16),
            Convert.ToByte(value[4..6], 16)));
    }
}