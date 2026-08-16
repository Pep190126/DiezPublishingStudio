using DiezPublishingStudio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace DiezPublishingStudio.UnoSpike;

internal static class VisualBookWorkspace
{
    private static readonly string[] ColoringStyles =
    [
        "Kawaii", "Cartoon", "Chibi", "Cute & Playful", "Whimsical", "Storybook",
        "Fairy-Tale", "Cottagecore", "Clean Line Art", "Detailed Line Art", "Minimal",
        "Simple Shapes", "Realistic Simplified", "Semi-Realistic", "Botanical",
        "Nature Journal", "Folk Art", "Scandinavian", "Boho", "Vintage", "Retro",
        "Mid-Century", "Art Nouveau", "Art Deco", "Mandala", "Zentangle", "Geometric",
        "Pattern", "Stained Glass", "Woodcut / Linocut", "Tattoo Flash", "Fantasy",
        "Gothic", "Steampunk", "Doodle", "Comic", "Manga", "Anime-inspired", "Custom"
    ];

    public static UIElement Build(
        DiezProjectDocument document,
        Func<Task> save,
        Action<string> report,
        Action refresh,
        Action showVision,
        Action showAiCenter)
    {
        var type = BookTypeCatalog.Normalize(document.BookType);
        var root = PageRoot(
            $"{Label(type)} · produzione immagini",
            "Piano, Prompt, job AI, Vision e finalizzazione usano lo stesso stato canonico del Core. La grafica della pagina verrà rifinita dopo la prima installazione.");

        if (!BookTypeCatalog.IsVisual(type))
        {
            root.Children.Add(Card("Tipo libro non visuale", new TextBlock
            {
                Text = "Questo percorso è disponibile per Coloring Book, Raccolta immagini e Libro illustrato.",
                TextWrapping = TextWrapping.Wrap
            }));
            return root;
        }

        var setup = document.ReadVisualSetup();
        var legacyCount = document.GetUiInt("Visual.ImageCount", 0);
        var initialCount = legacyCount > 0 && setup.ImageCount == 1 ? legacyCount : setup.ImageCount;
        var count = Editor(Math.Clamp(initialCount, 1, 500).ToString(), "1–500", 42, false);
        var subject = Editor(FirstNonBlank(setup.Subject, document.GetUiString("Visual.Subject")), "Soggetto/i principali", 90);
        var environment = Editor(FirstNonBlank(setup.Environment, document.GetUiString("Visual.Environment")), "Ambientazione / scenario", 90);
        var consistent = Check("Consistent — mantieni coerenti le immagini", setup.Consistent || document.GetUiBool("Visual.Consistent"));
        var consistencyRules = Editor(
            FirstNonBlank(setup.ConsistencyRules, document.GetUiString("Visual.ConsistencyRules")),
            "Regole Consistent: personaggi, proporzioni, stile, palette, elementi ricorrenti…",
            90);

        var planPanel = Vertical(
            Labeled("Numero esatto di immagini", count),
            Labeled("Soggetto/i", subject),
            Labeled("Ambientazione", environment),
            consistent,
            Labeled("Regole Consistent", consistencyRules));
        root.Children.Add(Card("1 · Piano del libro", planPanel));

        Func<DiezColoringProfileDto>? coloringProfile = null;
        Func<DiezImageProfileDto>? imageProfile = null;

        if (string.Equals(type, BookTypeCatalog.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            var p = setup.Coloring ?? new DiezColoringProfileDto(
                "Clean Line Art", false, false, "Bambini 6–9 anni", "Facile", "Spesso — Bold",
                "Bassa", "Bassa", "Semplice / minimo", "Ampio", true, true, true, true, true, "");

            var style = Combo(ColoringStyles, FirstNonBlank(document.GetUiString("Coloring.Style"), p.Style));
            var audience = Combo(["Prescolare 3–5 anni", "Bambini 6–9 anni", "Ragazzi 10–13 anni", "Adolescenti", "Adulti", "Tutte le età"], p.TargetAudience);
            var difficulty = Combo(["Molto facile", "Facile", "Media", "Impegnativa"], p.Difficulty);
            var lineWeight = Combo(["Molto spesso — Extra Bold", "Spesso — Bold", "Medio", "Sottile — Fine", "Molto sottile — Extra Fine", "Variabile"], p.LineWeight);
            var complexity = Combo(["Molto bassa", "Bassa", "Media", "Alta"], p.Complexity);
            var density = Combo(["Molto bassa", "Bassa", "Media", "Alta"], p.ElementDensity);
            var background = Combo(["Nessuno / bianco", "Semplice / minimo", "Contestuale leggero", "Dettagliato"], p.Background);
            var whiteSpace = Combo(["Molto ampio", "Ampio", "Medio", "Compatto"], p.WhiteSpace);
            var boldEasy = Check("Bold & Easy", p.BoldEasy || document.GetUiBool("Coloring.BoldEasy"));
            var cozy = Check("Cozy", p.Cozy || document.GetUiBool("Coloring.Cozy"));
            var closed = Check("Aree chiuse e facili da colorare", p.ClosedAreas);
            var tiny = Check("Evita aree e dettagli minuscoli", p.AvoidTinyAreas);
            var contours = Check("Contorni puliti e continui", p.CleanContours);
            var noText = Check("Niente testo o numeri nell'immagine", p.NoTextInsideImage);
            var separated = Check("Soggetto ben separato dallo sfondo", p.SubjectClearlySeparated);
            var notes = Editor(p.Notes, "Note stile / eccezioni", 75);

            coloringProfile = () => new DiezColoringProfileDto(
                Selected(style, "Clean Line Art"),
                boldEasy.IsChecked == true,
                cozy.IsChecked == true,
                Selected(audience, "Bambini 6–9 anni"),
                Selected(difficulty, "Facile"),
                Selected(lineWeight, "Spesso — Bold"),
                Selected(complexity, "Bassa"),
                Selected(density, "Bassa"),
                Selected(background, "Semplice / minimo"),
                Selected(whiteSpace, "Ampio"),
                closed.IsChecked == true,
                tiny.IsChecked == true,
                contours.IsChecked == true,
                noText.IsChecked == true,
                separated.IsChecked == true,
                notes.Text ?? string.Empty);

            var profilePanel = Vertical(
                Labeled("Stile", style),
                Horizontal(Labeled("Pubblico", audience), Labeled("Difficoltà", difficulty)),
                Labeled("Spessore linee", lineWeight),
                Horizontal(Labeled("Complessità", complexity), Labeled("Densità", density)),
                Horizontal(Labeled("Sfondo", background), Labeled("Spazio bianco", whiteSpace)),
                boldEasy, cozy, closed, tiny, contours, noText, separated,
                Labeled("Note", notes),
                new TextBlock
                {
                    Text = "Coloring HARD: nero puro #000000 e bianco puro #FFFFFF; niente grigi, colori, ombre o sfumature.",
                    TextWrapping = TextWrapping.Wrap
                });
            root.Children.Add(Card("Profilo Coloring", profilePanel));
        }
        else
        {
            var p = setup.Image ?? new DiezImageProfileDto(
                "Illustrazione editoriale / saggio", "Colore pieno", "Medio", "Contorno medio",
                "Illustrativo chiaro", "Semplice / funzionale", "Tre quarti", true, true, true, true, "");

            var use = Combo([
                "Illustrazione editoriale / saggio", "Sequenza di esercizi / movimenti", "Illustrazione didattica",
                "Figura tecnica / manuale", "Schema anatomico semplificato", "Serie di riferimento coerente",
                "Raccolta artistica / concettuale", "Decorazione editoriale"], p.EditorialUse);
            var color = Combo([
                "Colore pieno", "Colore limitato / palette controllata", "Scala di grigi — con sfumature",
                "Bianco e nero puro — 2 colori", "Monocromatico — una tinta + bianco", "Automatico secondo il contenuto"], p.ColorMode);
            var detail = Combo(["Molto schematico", "Basso", "Medio", "Alto", "Molto alto"], p.DetailLevel);
            var line = Combo(["Senza contorno dominante", "Contorno molto sottile", "Contorno sottile", "Contorno medio", "Contorno spesso", "Contorno variabile"], p.LineTreatment);
            var rendering = Combo(["Illustrativo chiaro", "Line art editoriale", "Infografico / didattico", "Realistico semplificato", "Tecnico pulito", "Pittorico controllato", "Fotografico / realistico"], p.RenderingStyle);
            var background = Combo(["Nessuno / trasparente se supportato", "Bianco pulito", "Semplice / funzionale", "Contestuale leggero", "Ambientato / completo"], p.Background);
            var viewpoint = Combo(["Frontale", "Tre quarti", "Laterale", "Dall'alto", "Variabile secondo il soggetto", "Stesso punto di vista per tutta la serie"], p.Viewpoint);
            var readable = Check("Soggetto chiaramente leggibile", p.KeepSubjectReadable);
            var noText = Check("Evita testo/etichette salvo richiesta", p.AvoidTextInsideImage);
            var clarity = Check("Priorità alla chiarezza editoriale", p.EditorialClarity);
            var sameScale = Check("Scala/inquadratura comparabili nella serie", p.SameScaleWhenSeries);
            var notes = Editor(p.Notes, "Note aggiuntive sulla serie", 75);

            imageProfile = () => new DiezImageProfileDto(
                Selected(use, "Illustrazione editoriale / saggio"),
                Selected(color, "Colore pieno"),
                Selected(detail, "Medio"),
                Selected(line, "Contorno medio"),
                Selected(rendering, "Illustrativo chiaro"),
                Selected(background, "Semplice / funzionale"),
                Selected(viewpoint, "Tre quarti"),
                readable.IsChecked == true,
                noText.IsChecked == true,
                clarity.IsChecked == true,
                sameScale.IsChecked == true,
                notes.Text ?? string.Empty);

            var profilePanel = Vertical(
                Labeled("Uso editoriale", use),
                Labeled("Resa cromatica", color),
                Labeled("Dettaglio", detail),
                Labeled("Trattamento linee", line),
                Labeled("Stile resa", rendering),
                Labeled("Sfondo", background),
                Labeled("Punto di vista", viewpoint),
                readable, noText, clarity, sameScale,
                Labeled("Note", notes));
            root.Children.Add(Card("Profilo immagini", profilePanel));
        }

        var mustDo = Editor(document.GetUiString("Prompt.MustDo"), "Cosa deve esserci / cosa deve fare l'immagine", 90);
        var mustNot = Editor(document.GetUiString("Prompt.MustNotDo"), "Cosa deve essere escluso", 80);
        var provider = Combo(["ChatGPT / OpenAI", "Gemini", "Altra / nuova AI"], document.GetUiString("AI.Provider", "ChatGPT / OpenAI"));
        var advanced = Check("Usa il modello immagini più avanzato disponibile", document.GetUiBool("AI.PreferAdvanced", true));
        var promptPreview = Editor("", "Qui comparirà il Prompt atomico selezionato", 230);
        var promptItems = new ListView { Height = 165 };
        DiezVisualPromptPack? currentPack = null;

        async Task<bool> SaveSetupAsync()
        {
            if (!int.TryParse(count.Text, out var parsed) || parsed < 1 || parsed > 500)
            {
                report("Inserisci un numero di immagini da 1 a 500.");
                count.Focus(FocusState.Programmatic);
                return false;
            }

            if (coloringProfile is not null)
            {
                document.SaveColoringSetup(
                    parsed,
                    subject.Text,
                    environment.Text,
                    consistent.IsChecked == true,
                    consistent.IsChecked == true ? consistencyRules.Text : string.Empty,
                    coloringProfile());
            }
            else if (imageProfile is not null)
            {
                document.SaveImageBookSetup(
                    type,
                    parsed,
                    subject.Text,
                    environment.Text,
                    consistent.IsChecked == true,
                    consistent.IsChecked == true ? consistencyRules.Text : string.Empty,
                    imageProfile());
            }

            document.SetUiInt("Visual.ImageCount", parsed);
            document.SetUiString("Visual.Subject", subject.Text);
            document.SetUiString("Visual.Environment", environment.Text);
            document.SetUiBool("Visual.Consistent", consistent.IsChecked == true);
            document.SetUiString("Visual.ConsistencyRules", consistent.IsChecked == true ? consistencyRules.Text : string.Empty);
            document.SetUiString("Prompt.MustDo", mustDo.Text);
            document.SetUiString("Prompt.MustNotDo", mustNot.Text);
            document.SetUiString("AI.Provider", provider.SelectedItem?.ToString());
            document.SetUiBool("AI.PreferAdvanced", advanced.IsChecked == true);
            await save();
            return true;
        }

        async Task BuildPackAsync()
        {
            if (!await SaveSetupAsync()) return;
            currentPack = document.BuildVisualPromptPack(
                mustDo.Text,
                mustNot.Text,
                ProviderId(provider.SelectedItem?.ToString()),
                advanced.IsChecked == true);
            await save();
            promptItems.ItemsSource = currentPack.Items.Select(i => $"{i.Code} · {i.Title}").ToList();
            promptItems.SelectedIndex = currentPack.Items.Count > 0 ? 0 : -1;
            promptPreview.Text = currentPack.Items.FirstOrDefault()?.Prompt ?? string.Empty;
            report($"Prompt Pack preparato: {currentPack.Items.Count} Prompt atomici per {currentPack.Items.Count} immagini.");
        }

        promptItems.SelectionChanged += (_, _) =>
        {
            if (currentPack is null || promptItems.SelectedIndex < 0 || promptItems.SelectedIndex >= currentPack.Items.Count) return;
            promptPreview.Text = currentPack.Items[promptItems.SelectedIndex].Prompt;
        };

        var promptActions = Horizontal(
            AsyncButton("Salva piano", async () =>
            {
                if (!await SaveSetupAsync()) return;
                report("Piano immagini salvato nel Core.");
                refresh();
            }),
            AsyncButton("Prepara Prompt Pack", BuildPackAsync));

        root.Children.Add(Card("2 · Prompt", Vertical(
            Labeled("DEVE FARE", mustDo),
            Labeled("NON DEVE FARE", mustNot),
            Horizontal(Labeled("Provider AI", provider), advanced),
            promptActions)));

        var jobActions = Horizontal(
            ActionButton("Copia Prompt", () => Copy(promptPreview.Text ?? string.Empty)),
            AsyncButton("Crea / verifica job del piano", async () =>
            {
                if (!await SaveSetupAsync()) return;
                var result = document.EnsureVisualReadyJobs(
                    mustDo.Text,
                    mustNot.Text,
                    ProviderId(provider.SelectedItem?.ToString()),
                    advanced.IsChecked == true);
                await save();
                report(result.Message);
                if (result.Success) refresh();
            }),
            ActionButton("Vai a Vision", showVision),
            ActionButton("Apri Produzione con AI", showAiCenter));

        root.Children.Add(Card("3 · Prompt Pack atomico", Vertical(
            promptItems,
            Labeled("Prompt selezionato", promptPreview),
            jobActions)));

        var progress = document.VisualProgress();
        var publication = document.PublicationState();
        var problems = progress.Problems.Count == 0
            ? "Nessun problema di completezza visuale rilevato."
            : string.Join(Environment.NewLine, progress.Problems.Select(p => "• " + p));
        var failedChecks = publication.Checks
            .Where(c => !c.Passed || string.Equals(c.Severity, "Error", StringComparison.OrdinalIgnoreCase))
            .Select(c => $"{(c.Passed ? "PASS" : "FAIL")} · {c.Code} · {c.Message}")
            .ToList();
        var checksText = failedChecks.Count == 0 ? "Nessun blocco di pubblicazione rilevato." : string.Join(Environment.NewLine, failedChecks);

        root.Children.Add(Card("Stato del libro", Vertical(
            new TextBlock
            {
                Text = $"Piano: {progress.ExpectedImages} · job: {progress.ImageJobs} · applicate: {progress.AppliedImages} · asset finali distinti: {progress.DistinctAppliedMaterials}",
                TextWrapping = TextWrapping.Wrap
            },
            new TextBlock { Text = problems, TextWrapping = TextWrapping.Wrap },
            new TextBlock
            {
                Text = publication.PreflightReady ? "Preflight: READY" : "Preflight: non ancora READY",
                TextWrapping = TextWrapping.Wrap
            },
            new TextBlock { Text = checksText, TextWrapping = TextWrapping.Wrap })));

        var freezeActions = Horizontal(
            AsyncButton("Crea Edition Freeze", async () =>
            {
                await save();
                var result = document.CreateEditionFreeze("Freeze creato dal percorso visuale Uno Preview.");
                await save();
                report(result.Message);
                refresh();
            }),
            AsyncButton("Crea Publication Candidate", async () =>
            {
                await save();
                var result = document.CreatePublicationCandidate();
                await save();
                report(result.Message);
                refresh();
            }));

        var exportActions = Horizontal(
            AsyncButton("Esporta immagini finali ZIP", async () =>
            {
                if (string.IsNullOrWhiteSpace(document.SourcePath))
                {
                    report("Salva prima il progetto .diez.");
                    return;
                }

                try
                {
                    var picker = new FileSavePicker
                    {
                        SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                        SuggestedFileName = Path.GetFileNameWithoutExtension(
                            DiezPublicationFrontendBridge.SuggestedVisualImagesZipName(document.ExportProjectJson()))
                    };
                    picker.FileTypeChoices.Add("ZIP immagini finali", new List<string> { ".zip" });
                    var file = await picker.PickSaveFileAsync();
                    if (file is null) return;
                    await save();
                    var result = await document.ExportFinalVisualImagesAsync(document.SourcePath!, file.Path);
                    report(result.Message);
                }
                catch (Exception ex)
                {
                    report("Errore export immagini: " + ex.GetBaseException().Message);
                }
            }),
            AsyncButton("Esporta pacchetto pubblicazione", async () =>
            {
                try
                {
                    var picker = new FileSavePicker
                    {
                        SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                        SuggestedFileName = Path.GetFileNameWithoutExtension(
                            DiezPublicationFrontendBridge.SuggestedPublicationPackageName(document.ExportProjectJson()))
                    };
                    picker.FileTypeChoices.Add("Pacchetto pubblicazione", new List<string> { ".zip" });
                    var file = await picker.PickSaveFileAsync();
                    if (file is null) return;
                    await save();
                    var result = await document.ExportPublicationPackageAsync(file.Path);
                    report(result.Message);
                }
                catch (Exception ex)
                {
                    report("Errore export pubblicazione: " + ex.GetBaseException().Message);
                }
            }));

        root.Children.Add(Card("4 · Freeze, Publication Candidate ed export finale", Vertical(
            new TextBlock
            {
                Text = "Le azioni finali restano bloccate finché le immagini non sono importate, approvate in Vision, portate nel libro e incorporate nel .diez.",
                TextWrapping = TextWrapping.Wrap
            },
            freezeActions,
            exportActions)));

        return root;
    }

    private static StackPanel PageRoot(string title, string description)
    {
        var root = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(28),
            MaxWidth = 1050,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        root.Children.Add(new TextBlock { Text = title, FontSize = 28, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new Separator());
        return root;
    }

    private static Border Card(string title, UIElement content) => new()
    {
        Padding = new Thickness(16),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Child = Vertical(new TextBlock { Text = title, FontSize = 19, TextWrapping = TextWrapping.Wrap }, content)
    };

    private static StackPanel Vertical(params UIElement[] items)
    {
        var panel = new StackPanel { Spacing = 9 };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static StackPanel Horizontal(params UIElement[] items)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static StackPanel Labeled(string label, UIElement control) =>
        Vertical(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, control);

    private static TextBox Editor(string text, string placeholder, double minHeight, bool multiline = true) => new()
    {
        Text = text ?? string.Empty,
        PlaceholderText = placeholder,
        MinHeight = minHeight,
        AcceptsReturn = multiline,
        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static ComboBox Combo(IEnumerable<string> values, string selected)
    {
        var items = values.ToList();
        var combo = new ComboBox
        {
            ItemsSource = items,
            MinWidth = 230,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        combo.SelectedItem = items.FirstOrDefault(x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
        return combo;
    }

    private static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value };

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8) };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button AsyncButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static string Label(string type) => BookTypeCatalog.Normalize(type) switch
    {
        BookTypeCatalog.ImageCollection => "Raccolta immagini",
        BookTypeCatalog.IllustratedBook => "Libro illustrato",
        _ => "Coloring Book"
    };

    private static string ProviderId(string? value)
    {
        if ((value ?? string.Empty).Contains("Gemini", StringComparison.OrdinalIgnoreCase)) return "gemini";
        if ((value ?? string.Empty).Contains("OpenAI", StringComparison.OrdinalIgnoreCase) ||
            (value ?? string.Empty).Contains("ChatGPT", StringComparison.OrdinalIgnoreCase)) return "openai";
        return "generic";
    }

    private static string Selected(ComboBox combo, string fallback) => combo.SelectedItem?.ToString() ?? fallback;

    private static string FirstNonBlank(string? preferred, string? fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred! : fallback ?? string.Empty;

    private static void Copy(string text)
    {
        var package = new DataPackage();
        package.SetText(text ?? string.Empty);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}
