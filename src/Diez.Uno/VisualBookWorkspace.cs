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
        var phase = Math.Clamp(document.GetUiInt("Visual.ActivePhase", 1), 1, 4);
        var root = PageRoot(
            $"{Label(type)} · percorso immagini",
            "Percorso guidato in quattro fasi. Piano, Prompt, produzione AI, Vision e finalizzazione usano lo stesso stato canonico del Core.");

        if (!BookTypeCatalog.IsVisual(type))
        {
            root.Children.Add(Card("Tipo libro non visuale", new TextBlock
            {
                Text = "Questo percorso è disponibile per Coloring Book, Raccolta immagini e Libro illustrato.",
                TextWrapping = TextWrapping.Wrap
            }));
            return root;
        }

        root.Children.Add(PhaseStrip(phase));

        async Task GoToPhaseAsync(int target)
        {
            document.SetUiInt("Visual.ActivePhase", Math.Clamp(target, 1, 4));
            await save();
            refresh();
        }

        switch (phase)
        {
            case 1:
                BuildPhaseOne(root, document, type, save, report, refresh, GoToPhaseAsync);
                break;
            case 2:
                BuildPhaseTwo(root, document, save, report, refresh, GoToPhaseAsync);
                break;
            case 3:
                BuildPhaseThree(root, document, save, report, refresh, showVision, showAiCenter, GoToPhaseAsync);
                break;
            default:
                BuildPhaseFour(root, document, save, report, refresh, showVision, showAiCenter, GoToPhaseAsync);
                break;
        }

        return root;
    }

    private static void BuildPhaseOne(
        StackPanel root,
        DiezProjectDocument document,
        string type,
        Func<Task> save,
        Action<string> report,
        Action refresh,
        Func<int, Task> goToPhase)
    {
        var setup = document.ReadVisualSetup();
        var legacyCount = document.GetUiInt("Visual.ImageCount", 0);
        var initialCount = legacyCount > 0 && setup.ImageCount == 1 ? legacyCount : setup.ImageCount;
        var count = NumberInput(Math.Clamp(initialCount, 1, 500), 1, 500, 1, 190);
        var subject = Editor(FirstNonBlank(setup.Subject, document.GetUiString("Visual.Subject")), "Soggetto/i principali", 100);
        var environment = Editor(FirstNonBlank(setup.Environment, document.GetUiString("Visual.Environment")), "Ambientazione generica / scenario", 100);
        var consistent = Check("Consistent — mantieni coerenti soggetti, stile e regole fra le immagini", setup.Consistent || document.GetUiBool("Visual.Consistent"));
        var consistencyRules = Editor(
            FirstNonBlank(setup.ConsistencyRules, document.GetUiString("Visual.ConsistencyRules")),
            "Regole generali Consistent: proporzioni, stile, palette, elementi ricorrenti…",
            90);

        var structuredConsistency = BuildStructuredConsistencyEditor(document, save, report, refresh);
        structuredConsistency.Visibility = consistent.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        consistent.Checked += (_, _) => structuredConsistency.Visibility = Visibility.Visible;
        consistent.Unchecked += (_, _) => structuredConsistency.Visibility = Visibility.Collapsed;

        var planPanel = Vertical(
            Labeled("Numero esatto di immagini", count),
            new TextBlock
            {
                Text = "Usa le frecce del contatore oppure digita direttamente un valore da 1 a 500.",
                TextWrapping = TextWrapping.Wrap
            },
            Labeled("Soggetto/i", subject),
            Labeled("Ambientazione generica", environment),
            new TextBlock
            {
                Text = "Se attivi Scene strutturate, l'ambientazione della singola scena prevale su questa ambientazione generica per le immagini assegnate a quella scena.",
                TextWrapping = TextWrapping.Wrap
            },
            consistent,
            Labeled("Regole Consistent generali", consistencyRules),
            structuredConsistency);
        root.Children.Add(Card("1/4 · Definizione del libro e Consistent", planPanel));

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
            var boldEasy = Check("Bold & Easy — parametro HARD indipendente", p.BoldEasy || document.GetUiBool("Coloring.BoldEasy"));
            var cozy = Check("Cozy — parametro HARD indipendente", p.Cozy || document.GetUiBool("Coloring.Cozy"));
            var closed = Check("Aree chiuse e facili da colorare", p.ClosedAreas);
            var tiny = Check("Evita aree e dettagli minuscoli", p.AvoidTinyAreas);
            var contours = Check("Contorni puliti e continui", p.CleanContours);
            var noText = Check("Niente testo o numeri nell'immagine", p.NoTextInsideImage);
            var separated = Check("Soggetto ben separato dallo sfondo", p.SubjectClearlySeparated);
            var notes = Editor(p.Notes, "Note stile / eccezioni", 80);

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

            root.Children.Add(Card("Profilo Coloring", Vertical(
                Labeled("Stile", style),
                WrapRow(Labeled("Pubblico", audience), Labeled("Difficoltà", difficulty)),
                Labeled("Spessore linee", lineWeight),
                WrapRow(Labeled("Complessità", complexity), Labeled("Densità", density)),
                WrapRow(Labeled("Sfondo", background), Labeled("Spazio bianco", whiteSpace)),
                boldEasy, cozy, closed, tiny, contours, noText, separated,
                Labeled("Note", notes),
                new TextBlock
                {
                    Text = "Coloring HARD: nero puro #000000 e bianco puro #FFFFFF; niente grigi, colori, ombre o sfumature.",
                    TextWrapping = TextWrapping.Wrap
                })));
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
            var notes = Editor(p.Notes, "Note aggiuntive sulla serie", 80);

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

            root.Children.Add(Card("Profilo immagini", Vertical(
                Labeled("Uso editoriale", use),
                Labeled("Resa cromatica", color),
                WrapRow(Labeled("Dettaglio", detail), Labeled("Trattamento linee", line)),
                WrapRow(Labeled("Stile resa", rendering), Labeled("Sfondo", background)),
                Labeled("Punto di vista", viewpoint),
                readable, noText, clarity, sameScale,
                Labeled("Note", notes))));
        }

        async Task<bool> SaveSetupAsync()
        {
            var parsed = ReadInteger(count, 1);
            if (parsed < 1 || parsed > 500)
            {
                report("Inserisci un numero di immagini da 1 a 500.");
                count.Focus(FocusState.Programmatic);
                return false;
            }

            if (coloringProfile is not null)
            {
                document.SaveColoringSetup(
                    parsed, subject.Text, environment.Text,
                    consistent.IsChecked == true,
                    consistent.IsChecked == true ? consistencyRules.Text : string.Empty,
                    coloringProfile());
            }
            else if (imageProfile is not null)
            {
                document.SaveImageBookSetup(
                    type, parsed, subject.Text, environment.Text,
                    consistent.IsChecked == true,
                    consistent.IsChecked == true ? consistencyRules.Text : string.Empty,
                    imageProfile());
            }

            document.SetUiInt("Visual.ImageCount", parsed);
            document.SetUiString("Visual.Subject", subject.Text);
            document.SetUiString("Visual.Environment", environment.Text);
            document.SetUiBool("Visual.Consistent", consistent.IsChecked == true);
            document.SetUiString("Visual.ConsistencyRules", consistent.IsChecked == true ? consistencyRules.Text : string.Empty);
            await save();
            return true;
        }

        root.Children.Add(NavigationRow(
            null,
            AsyncButton("Salva e continua → Prompt", async () =>
            {
                if (!await SaveSetupAsync()) return;
                report("Fase 1 salvata nel Core.");
                await goToPhase(2);
            })));
    }

    private static void BuildPhaseTwo(
        StackPanel root,
        DiezProjectDocument document,
        Func<Task> save,
        Action<string> report,
        Action refresh,
        Func<int, Task> goToPhase)
    {
        var mustDo = Editor(document.GetUiString("Prompt.MustDo"), "Cosa deve esserci / cosa deve fare ogni immagine", 140);
        var mustNot = Editor(document.GetUiString("Prompt.MustNotDo"), "Cosa deve essere escluso", 120);
        var provider = Combo(["ChatGPT / OpenAI", "Gemini", "Altra / nuova AI"], document.GetUiString("AI.Provider", "ChatGPT / OpenAI"));
        var advanced = Check("Usa il modello immagini più avanzato disponibile", document.GetUiBool("AI.PreferAdvanced", true));

        root.Children.Add(Card("2/4 · Istruzioni e Prompt", Vertical(
            new TextBlock
            {
                Text = "Diez combina queste istruzioni con Tipo libro, profilo visuale, Consistent, soggetti, Scene e HARD locks. Il Prompt finale resta modificabile/trasportabile nella fase successiva.",
                TextWrapping = TextWrapping.Wrap
            },
            Labeled("DEVE FARE", mustDo),
            Labeled("NON DEVE FARE", mustNot),
            WrapRow(Labeled("Provider AI", provider), advanced))));

        async Task SavePromptInputsAsync()
        {
            document.SetUiString("Prompt.MustDo", mustDo.Text);
            document.SetUiString("Prompt.MustNotDo", mustNot.Text);
            document.SetUiString("AI.Provider", provider.SelectedItem?.ToString());
            document.SetUiBool("AI.PreferAdvanced", advanced.IsChecked == true);
            await save();
        }

        root.Children.Add(NavigationRow(
            AsyncButton("← Indietro", async () => await goToPhase(1)),
            AsyncButton("Prepara Prompt Pack →", async () =>
            {
                await SavePromptInputsAsync();
                var pack = document.BuildVisualPromptPack(
                    mustDo.Text,
                    mustNot.Text,
                    ProviderId(provider.SelectedItem?.ToString()),
                    advanced.IsChecked == true);
                await save();
                report($"Prompt Pack preparato: {pack.Items.Count} Prompt atomici.");
                await goToPhase(3);
            })));
    }

    private static void BuildPhaseThree(
        StackPanel root,
        DiezProjectDocument document,
        Func<Task> save,
        Action<string> report,
        Action refresh,
        Action showVision,
        Action showAiCenter,
        Func<int, Task> goToPhase)
    {
        DiezVisualPromptPack pack;
        try
        {
            pack = document.BuildVisualPromptPack(
                document.GetUiString("Prompt.MustDo"),
                document.GetUiString("Prompt.MustNotDo"),
                ProviderId(document.GetUiString("AI.Provider", "ChatGPT / OpenAI")),
                document.GetUiBool("AI.PreferAdvanced", true));
        }
        catch (Exception ex)
        {
            root.Children.Add(Card("3/4 · Prompt Pack e produzione", new TextBlock
            {
                Text = "Non riesco a preparare il Prompt Pack: " + ex.GetBaseException().Message,
                TextWrapping = TextWrapping.Wrap
            }));
            root.Children.Add(NavigationRow(AsyncButton("← Indietro", async () => await goToPhase(2)), null));
            return;
        }

        var promptItems = new ListView
        {
            Height = 200,
            ItemsSource = pack.Items.Select(i => $"{i.Code} · {i.Title}").ToList()
        };
        var promptPreview = Editor(pack.Items.FirstOrDefault()?.Prompt ?? string.Empty, "Prompt atomico selezionato", 300);
        promptItems.SelectedIndex = pack.Items.Count > 0 ? 0 : -1;
        promptItems.SelectionChanged += (_, _) =>
        {
            if (promptItems.SelectedIndex < 0 || promptItems.SelectedIndex >= pack.Items.Count) return;
            promptPreview.Text = pack.Items[promptItems.SelectedIndex].Prompt;
        };

        root.Children.Add(Card("3/4 · Prompt Pack e produzione", Vertical(
            new TextBlock
            {
                Text = "Un Prompt atomico per ogni immagine. Le Scene e i partecipanti selezionati sono già risolti nel Prompt della relativa posizione; gli ID tecnici restano interni.",
                TextWrapping = TextWrapping.Wrap
            },
            promptItems,
            Labeled("Prompt selezionato", promptPreview),
            WrapRow(
                ActionButton("Copia Prompt", () => Copy(promptPreview.Text ?? string.Empty)),
                AsyncButton("Crea / verifica job del piano", async () =>
                {
                    var result = document.EnsureVisualReadyJobs(
                        document.GetUiString("Prompt.MustDo"),
                        document.GetUiString("Prompt.MustNotDo"),
                        ProviderId(document.GetUiString("AI.Provider", "ChatGPT / OpenAI")),
                        document.GetUiBool("AI.PreferAdvanced", true));
                    await save();
                    report(result.Message);
                }),
                ActionButton("Produzione con AI", showAiCenter),
                ActionButton("Vision", showVision)))));

        var imageAssets = DiezImagePreviewCatalog.Read(document).ToList();
        if (imageAssets.Count == 0)
        {
            root.Children.Add(Card("Materiali e immagini · Anteprima", Vertical(
                new TextBlock
                {
                    Text = "Non ci sono ancora materiali immagine. Aggiungi un'immagine ai Materiali del progetto oppure importa una Candidate da Vision: apparirà qui nella stessa gallery.",
                    TextWrapping = TextWrapping.Wrap
                },
                WrapRow(ActionButton("Produzione con AI", showAiCenter), ActionButton("Vision", showVision)))));
        }
        else
        {
            var assetList = new ListView
            {
                Height = 420,
                ItemsSource = imageAssets.Select(x => x.Label).ToList()
            };
            var assetPreview = new VisualImagePreviewSurface(420);
            assetList.SelectionChanged += async (_, _) =>
            {
                if (assetList.SelectedIndex < 0 || assetList.SelectedIndex >= imageAssets.Count)
                {
                    assetPreview.Clear();
                    return;
                }
                await assetPreview.ShowAssetAsync(document, imageAssets[assetList.SelectedIndex]);
            };

            var gallery = new Grid
            {
                ColumnSpacing = 16,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            gallery.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            gallery.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(assetList, 0);
            Grid.SetColumn(assetPreview.View, 1);
            gallery.Children.Add(assetList);
            gallery.Children.Add(assetPreview.View);

            root.Children.Add(Card("Materiali e immagini · Anteprima", Vertical(
                new TextBlock
                {
                    Text = "La stessa preview mostra materiale aggiunto, Candidate AI, versione approvata o immagine già portata nel libro. Cambiare selezione non cambia approvazione, Vision o placement.",
                    TextWrapping = TextWrapping.Wrap
                },
                gallery)));
            assetList.SelectedIndex = 0;
        }

        root.Children.Add(NavigationRow(
            AsyncButton("← Indietro", async () => await goToPhase(2)),
            AsyncButton("Continua → Revisione", async () =>
            {
                await save();
                await goToPhase(4);
            })));
    }

    private static void BuildPhaseFour(
        StackPanel root,
        DiezProjectDocument document,
        Func<Task> save,
        Action<string> report,
        Action refresh,
        Action showVision,
        Action showAiCenter,
        Func<int, Task> goToPhase)
    {
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

        root.Children.Add(Card("4/4 · Revisione, Vision e finalizzazione", Vertical(
            new TextBlock
            {
                Text = "Seleziona Vision per controllare le Candidate reali. Un'immagine entra nel libro solo dopo approvazione e il successivo comando Porta nel libro.",
                TextWrapping = TextWrapping.Wrap
            },
            WrapRow(ActionButton("Apri Vision", showVision), ActionButton("Apri Produzione con AI", showAiCenter)),
            new Separator(),
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

        root.Children.Add(Card("Freeze ed export finale", Vertical(
            new TextBlock
            {
                Text = "Freeze e Publication Candidate restano bloccati finché quantità, approvazioni Vision, applicazione al libro e package .diez non sono coerenti.",
                TextWrapping = TextWrapping.Wrap
            },
            WrapRow(
                AsyncButton("Crea Edition Freeze", async () =>
                {
                    await save();
                    var result = document.CreateEditionFreeze("Freeze creato dal percorso visuale Uno.");
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
                })),
            WrapRow(
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
                })))));

        root.Children.Add(NavigationRow(AsyncButton("← Indietro", async () => await goToPhase(3)), null));
    }

    private static FrameworkElement BuildStructuredConsistencyEditor(
        DiezProjectDocument document,
        Func<Task> save,
        Action<string> report,
        Action refresh)
    {
        DiezVisualSceneStateDto state;
        try { state = document.ReadVisualSceneState(); }
        catch
        {
            return new TextBlock
            {
                Text = "Soggetti/Scene strutturate non disponibili.",
                TextWrapping = TextWrapping.Wrap
            };
        }

        var panel = Vertical(
            new Separator(),
            new TextBlock { Text = "Soggetti, Consistent e Scene", FontSize = 19, TextWrapping = TextWrapping.Wrap },
            new TextBlock
            {
                Text = "Qui le Scene vengono realmente attaccate ai personaggi: Diez salva la partecipazione con SubjectId + SceneId stabili, non con il nome visibile.",
                TextWrapping = TextWrapping.Wrap
            });

        var multi = Check("Soggetti/personaggi strutturati", state.MultiSubjectEnabled);
        var subjectCount = NumberInput(Math.Max(1, state.SubjectCount), 1, 12, 1, 160);
        panel.Children.Add(WrapRow(
            multi,
            Labeled("N° soggetti", subjectCount),
            AsyncButton("Applica soggetti", async () =>
            {
                var result = document.ConfigureVisualSubjects(multi.IsChecked == true, ReadInteger(subjectCount, 1));
                await save();
                report(result.Message);
                refresh();
            })));

        if (state.MultiSubjectEnabled && state.Subjects.Count > 0)
        {
            var subjectSelector = new ComboBox
            {
                ItemsSource = state.Subjects,
                DisplayMemberPath = nameof(DiezVisualSubjectDto.Name),
                MinWidth = 240,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            subjectSelector.SelectedItem = state.Subjects.FirstOrDefault(x =>
                string.Equals(x.SubjectId, state.ActiveSubjectId, StringComparison.OrdinalIgnoreCase)) ?? state.Subjects[0];

            var subjectEditor = Vertical();
            panel.Children.Add(Labeled("Personaggio / soggetto", subjectSelector));
            panel.Children.Add(subjectEditor);

            void RenderSubject(DiezVisualSubjectDto selected)
            {
                subjectEditor.Children.Clear();
                var name = Editor(selected.Name, "Nome soggetto/personaggio", 42, false);
                var description = Editor(selected.Description, "Aspetto, segni distintivi, proporzioni e caratteristiche da mantenere", 95);
                subjectEditor.Children.Add(Labeled("Nome", name));
                subjectEditor.Children.Add(Labeled("Descrizione", description));
                subjectEditor.Children.Add(AsyncButton("Salva soggetto", async () =>
                {
                    var result = document.SaveVisualSubject(selected.SubjectId, name.Text, description.Text);
                    await save();
                    report(result.Message);
                    if (result.Status == "SAVED") refresh();
                }));
                subjectEditor.Children.Add(new TextBlock
                {
                    Text = "Consistent del soggetto/personaggio",
                    FontSize = 17,
                    Margin = new Thickness(0, 8, 0, 0)
                });

                foreach (var rule in selected.Consistency)
                {
                    if (rule.Key == "identity")
                    {
                        subjectEditor.Children.Add(new TextBlock
                        {
                            Text = $"{rule.Label} — LOCKED / HARD",
                            TextWrapping = TextWrapping.Wrap
                        });
                        continue;
                    }

                    var level = Combo(["LOCKED", "PREFERRED", "FREE"], rule.Level);
                    var strategy = Combo(["USER", "AI", "MIXED"], rule.Strategy);
                    var variation = Editor(rule.Variation, "Indicazioni di variazione, se consentite", 62);
                    subjectEditor.Children.Add(new Border
                    {
                        Padding = new Thickness(10),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Child = Vertical(
                            new TextBlock { Text = rule.Label, FontSize = 15, TextWrapping = TextWrapping.Wrap },
                            WrapRow(Labeled("Livello", level), Labeled("Decisione", strategy)),
                            Labeled("Variazione / guida", variation),
                            AsyncButton("Salva regola", async () =>
                            {
                                var result = document.SaveVisualConsistencyRule(
                                    selected.SubjectId, rule.Key,
                                    level.SelectedItem?.ToString(),
                                    strategy.SelectedItem?.ToString(),
                                    variation.Text);
                                await save();
                                report(result.Message);
                            }))
                    });
                }
            }

            RenderSubject((DiezVisualSubjectDto)subjectSelector.SelectedItem!);
            subjectSelector.SelectionChanged += (_, _) =>
            {
                if (subjectSelector.SelectedItem is DiezVisualSubjectDto selected) RenderSubject(selected);
            };
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Attiva Soggetti/personaggi strutturati per dare a ogni personaggio un'identità stabile e collegarlo alle Scene.",
                TextWrapping = TextWrapping.Wrap
            });
        }

        panel.Children.Add(new Separator());
        panel.Children.Add(new TextBlock { Text = "Scene e partecipazione", FontSize = 17, TextWrapping = TextWrapping.Wrap });
        var scenesEnabled = Check("Definisci Scene strutturate", state.ScenesEnabled);
        var sceneCount = NumberInput(Math.Max(1, state.SceneCount), 1, 120, 1, 160);
        panel.Children.Add(WrapRow(
            scenesEnabled,
            Labeled("N° Scene", sceneCount),
            AsyncButton("Applica Scene", async () =>
            {
                var result = document.ConfigureVisualScenes(scenesEnabled.IsChecked == true, ReadInteger(sceneCount, 1));
                await save();
                report(result.Message);
                refresh();
            })));

        if (state.ScenesEnabled && state.Scenes.Count > 0)
        {
            var sceneSelector = new ComboBox
            {
                ItemsSource = state.Scenes,
                DisplayMemberPath = nameof(DiezVisualSceneDto.Name),
                MinWidth = 240,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            sceneSelector.SelectedItem = state.Scenes.FirstOrDefault(x =>
                string.Equals(x.SceneId, state.ActiveSceneId, StringComparison.OrdinalIgnoreCase)) ?? state.Scenes[0];
            var sceneEditor = Vertical();
            panel.Children.Add(Labeled("Scena", sceneSelector));
            panel.Children.Add(sceneEditor);

            void RenderScene(DiezVisualSceneDto selected)
            {
                sceneEditor.Children.Clear();
                var name = Editor(selected.Name, "Nome scena", 42, false);
                var description = Editor(selected.Description, "Ambientazione e azione specifica della scena", 95);
                sceneEditor.Children.Add(Labeled("Nome scena", name));
                sceneEditor.Children.Add(Labeled("Descrizione / ambientazione locale", description));
                sceneEditor.Children.Add(AsyncButton("Salva scena", async () =>
                {
                    var result = document.SaveVisualScene(selected.SceneId, name.Text, description.Text);
                    await save();
                    report(result.Message);
                    if (result.Status == "SAVED") refresh();
                }));

                sceneEditor.Children.Add(new TextBlock
                {
                    Text = "Personaggi presenti in questa scena",
                    FontSize = 16,
                    Margin = new Thickness(0, 8, 0, 0)
                });
                if (!state.MultiSubjectEnabled || state.Subjects.Count == 0)
                {
                    sceneEditor.Children.Add(new TextBlock
                    {
                        Text = "Attiva prima Soggetti/personaggi strutturati per collegare la scena ai personaggi.",
                        TextWrapping = TextWrapping.Wrap
                    });
                    return;
                }

                foreach (var subject in state.Subjects)
                {
                    var participates = selected.ParticipantSubjectIds.Contains(subject.SubjectId, StringComparer.OrdinalIgnoreCase);
                    var check = Check(subject.Name, participates);
                    check.Checked += async (_, _) =>
                    {
                        var result = document.SetVisualSceneParticipation(selected.SceneId, subject.SubjectId, true);
                        await save();
                        report(result.Message);
                    };
                    check.Unchecked += async (_, _) =>
                    {
                        var result = document.SetVisualSceneParticipation(selected.SceneId, subject.SubjectId, false);
                        await save();
                        report(result.Message);
                    };
                    sceneEditor.Children.Add(check);
                }
                sceneEditor.Children.Add(new TextBlock
                {
                    Text = "Il Prompt Compiler userà questi partecipanti per la Work Unit assegnata alla scena; Vision verifica scene_participants_match come gate HARD quando applicabile.",
                    TextWrapping = TextWrapping.Wrap
                });
            }

            RenderScene((DiezVisualSceneDto)sceneSelector.SelectedItem!);
            sceneSelector.SelectionChanged += (_, _) =>
            {
                if (sceneSelector.SelectedItem is DiezVisualSceneDto selected) RenderScene(selected);
            };
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Con Scene disattivate, tutte le immagini usano l'ambientazione generica. Attivandole, ogni scena conserva un SceneId stabile e può avere personaggi partecipanti specifici.",
                TextWrapping = TextWrapping.Wrap
            });
        }

        return panel;
    }

    private static StackPanel PageRoot(string title, string description)
    {
        var root = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(28),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        root.Children.Add(new TextBlock { Text = title, FontSize = 28, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new Separator());
        return root;
    }

    private static UIElement PhaseStrip(int active)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var names = new[] { "1 · Definizione", "2 · Prompt", "3 · Produzione", "4 · Revisione" };
        for (var i = 0; i < names.Length; i++)
        {
            panel.Children.Add(new Border
            {
                Padding = new Thickness(12, 7),
                BorderThickness = new Thickness(i + 1 == active ? 2 : 1),
                CornerRadius = new CornerRadius(16),
                Child = new TextBlock
                {
                    Text = (i + 1 == active ? "● " : "○ ") + names[i],
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }
        return panel;
    }

    private static Border Card(string title, UIElement content) => new()
    {
        Padding = new Thickness(16),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Child = Vertical(new TextBlock { Text = title, FontSize = 19, TextWrapping = TextWrapping.Wrap }, content)
    };

    private static StackPanel Vertical(params UIElement[] items)
    {
        var panel = new StackPanel { Spacing = 9, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static StackPanel WrapRow(params UIElement[] items)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static StackPanel NavigationRow(UIElement? back, UIElement? next)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 18)
        };
        if (back is not null) row.Children.Add(back);
        if (next is not null) row.Children.Add(next);
        return row;
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

    private static NumberBox NumberInput(double value, double min, double max, double step, double width) => new()
    {
        Value = Math.Clamp(value, min, max),
        Minimum = min,
        Maximum = max,
        SmallChange = step,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        Width = width,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private static int ReadInteger(NumberBox box, int fallback)
    {
        var value = box.Value;
        if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
        return (int)Math.Round(value);
    }

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
