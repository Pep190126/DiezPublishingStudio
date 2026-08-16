using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace DiezPublishingStudio.UnoSpike;

public sealed class MainShellPage : Page
{
    private static readonly string[] BookTypes =
    [
        "Coloring book", "Raccolta immagini", "Libro illustrato", "Saggio / manuale",
        "Word Search", "Cruciverba", "Quiz / trivia", "Romanzo / racconto",
        "Catalogo / raccolta dati", "Altro"
    ];

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

    private readonly ContentControl _contentHost = new();
    private readonly TextBlock _projectHeader = new() { Text = "Nessun progetto aperto", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Text = "Pronto.", TextWrapping = TextWrapping.Wrap };
    private DiezProjectDocument? _document;
    private string? _projectPath;

    public MainShellPage()
    {
        Content = BuildShell();
        ShowHome();
    }

    private UIElement BuildShell()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var navigation = new StackPanel { Spacing = 7, Margin = new Thickness(16) };
        navigation.Children.Add(new TextBlock { Text = "Diez Publishing Studio", FontSize = 22, TextWrapping = TextWrapping.Wrap });
        navigation.Children.Add(new TextBlock { Text = "Uno Platform · workspace stabile", FontSize = 13, TextWrapping = TextWrapping.Wrap });
        navigation.Children.Add(new Separator());
        navigation.Children.Add(_projectHeader);
        navigation.Children.Add(new Separator());

        AddNav(navigation, "Home / Progetto", ShowHome);
        AddNav(navigation, "Percorso libro", ShowBookRoute);
        AddNav(navigation, "Visual 1/4 · Quantità", ShowVisualQuantity);
        AddNav(navigation, "Visual 2/4 · Prompt", ShowVisualPrompt);
        AddNav(navigation, "Visual 3/4 · Prompt Pack", ShowPromptPack);
        AddNav(navigation, "Visual 4/4 · Vision", ShowVisionReview);
        AddNav(navigation, "Scene / Soggetti", ShowScenesAndSubjects);
        AddNav(navigation, "Word Search", ShowWordSearch);
        AddNav(navigation, "Cruciverba", ShowCrossword);
        AddNav(navigation, "Raccolta immagini", ShowImageCollection);
        AddNav(navigation, "Narrativa / Manuale", ShowNarrative);
        AddNav(navigation, "Editable Master", ShowEditableMaster);
        AddNav(navigation, "Content Graph / Bible", ShowContentGraph);
        AddNav(navigation, "Consistency Review", ShowConsistency);
        AddNav(navigation, "AI Production / Exchange", ShowAiCenter);
        AddNav(navigation, "Export / Finalizzazione", ShowExportAndFinalization);
        AddNav(navigation, "Libreria finalizzati", ShowFinalizedLibrary);

        navigation.Children.Add(new Separator());
        navigation.Children.Add(_status);

        var navScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = navigation
        };
        Grid.SetColumn(navScroll, 0);
        root.Children.Add(navScroll);

        var contentScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _contentHost
        };
        Grid.SetColumn(contentScroll, 1);
        root.Children.Add(contentScroll);
        return root;
    }

    private static void AddNav(Panel panel, string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }

    private void ShowHome()
    {
        var root = PageRoot("Home / Progetto",
            "La Home Uno usa un albero visivo permanente: progetto, materiali e percorso libro restano nello stesso shell.");
        root.Children.Add(Horizontal(
            AsyncButton("Nuovo progetto", CreateProjectAsync),
            AsyncButton("Apri .diez", OpenProjectAsync),
            AsyncButton("Aggiungi materiali", ImportMaterialsAsync),
            AsyncButton("Salva", SaveProjectAsync)));

        if (_document is null)
        {
            root.Children.Add(Card("Nessun progetto aperto",
                new TextBlock
                {
                    Text = "Crea o apri un .diez. Il pacchetto viene letto e riscritto preservando le sezioni JSON sconosciute e gli allegati già incorporati.",
                    TextWrapping = TextWrapping.Wrap
                }));
            root.Children.Add(Card("Percorso di prova",
                ActionButton("Apri workspace demo", () =>
                {
                    _document = DiezProjectDocument.Create("Progetto Demo Uno");
                    _projectPath = null;
                    RefreshHeader();
                    ShowBookRoute();
                })));
            SetContent(root);
            return;
        }

        var summary = Vertical(
            new TextBlock { Text = _document.Name, FontSize = 21, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = $"Titolo: {_document.EditionTitle}", TextWrapping = TextWrapping.Wrap },
            new TextBlock
            {
                Text = $"Materiali: {_document.MaterialCount} · Contenuti: {_document.ContentCount} · Entità: {_document.EntityCount} · Problemi aperti: {_document.OpenIssueCount}",
                TextWrapping = TextWrapping.Wrap
            },
            new TextBlock { Text = $"File: {_projectPath ?? "(non ancora salvato)"}", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(Card("Progetto attivo", summary));

        var materials = new ListView { Height = 190, ItemsSource = _document.MaterialDisplayItems() };
        var remove = AsyncButton("Rimuovi materiale selezionato", async () =>
        {
            if (materials.SelectedIndex < 0 || !_document.RemoveMaterialAt(materials.SelectedIndex))
            {
                Report("Seleziona un materiale da rimuovere.");
                return;
            }
            await SaveIfPossibleAsync();
            ShowHome();
            Report("Materiale rimosso.");
        });
        root.Children.Add(Card("Materiali del progetto", Vertical(materials, remove)));
        root.Children.Add(Card("Continua",
            Horizontal(
                ActionButton("Percorso libro", ShowBookRoute),
                ActionButton("AI / Prompt", ShowAiCenter),
                ActionButton("Export", ShowExportAndFinalization))));
        SetContent(root);
    }

    private void ShowBookRoute()
    {
        if (!RequireDocument()) return;
        var root = PageRoot("Percorso libro · Tipo libro",
            "Titolo e Tipo libro sono salvati nel progetto. La scelta instrada al workspace specializzato senza aprire nuove finestre.");

        var title = Editor(_document!.EditionTitle, "Titolo del libro", 44, false);
        var type = Combo(BookTypes, string.IsNullOrWhiteSpace(_document.BookType) ? "Coloring book" : _document.BookType);
        var save = AsyncButton("Salva identità libro", async () =>
        {
            _document.EditionTitle = title.Text?.Trim() ?? string.Empty;
            _document.BookType = type.SelectedItem?.ToString() ?? "Altro";
            await SaveIfPossibleAsync();
            RefreshHeader();
            Report("Titolo e Tipo libro salvati.");
        });
        var next = AsyncButton("Continua nel workspace", async () =>
        {
            _document.EditionTitle = title.Text?.Trim() ?? string.Empty;
            _document.BookType = type.SelectedItem?.ToString() ?? "Altro";
            await SaveIfPossibleAsync();
            RouteCurrentBookType();
        });

        root.Children.Add(Card("Identità del libro",
            Vertical(
                Labeled("Titolo del libro", title),
                Labeled("Tipo libro", type),
                Horizontal(save, next))));
        root.Children.Add(Card("Tipi disponibili",
            new TextBlock { Text = string.Join(" · ", BookTypes), TextWrapping = TextWrapping.Wrap }));
        SetContent(root);
    }

    private void RouteCurrentBookType()
    {
        var type = _document?.BookType ?? string.Empty;
        if (IsVisualType(type)) ShowVisualQuantity();
        else if (type.Equals("Word Search", StringComparison.OrdinalIgnoreCase)) ShowWordSearch();
        else if (type.Equals("Cruciverba", StringComparison.OrdinalIgnoreCase)) ShowCrossword();
        else ShowNarrative();
    }

    private void ShowVisualQuantity()
    {
        if (!RequireDocument()) return;
        var type = _document!.BookType;
        if (!IsVisualType(type))
        {
            Report("Il Tipo libro attuale non usa il flusso immagini. Apro la scelta Tipo libro.");
            ShowBookRoute();
            return;
        }

        var root = PageRoot($"{VisualLabel(type)} · 1/4 Quantità e contenuto",
            "Numero esatto, soggetti, ambientazione, stile e Consistent sono editabili direttamente con controlli Uno.");
        var count = Editor(Math.Max(1, _document.GetUiInt("Visual.ImageCount", 1)).ToString(), "1–500", 42, false);
        var subject = Editor(_document.GetUiString("Visual.Subject"), "Personaggio/i o soggetto/i; sono ammesse eccezioni “Immagine N”.", 115);
        var environment = Editor(_document.GetUiString("Visual.Environment"), "Ambientazione / scenario; sono ammesse variazioni per immagine.", 115);
        var consistent = new CheckBox { Content = "Consistent — mantieni coerenti le immagini", IsChecked = _document.GetUiBool("Visual.Consistent") };
        var rules = Editor(_document.GetUiString("Visual.ConsistencyRules"), "Regole di coerenza: soggetti, stile, colori, proporzioni, elementi ricorrenti…", 100);

        root.Children.Add(Card("Quantità e contenuto",
            Vertical(
                Labeled("Quante immagini vuoi creare?", count),
                Labeled("Personaggio/i, soggetto/i e variazioni", subject),
                Labeled("Ambientazione / scenario", environment),
                consistent,
                Labeled("Regole Consistent", rules))));
        root.Children.Add(type.Equals("Coloring book", StringComparison.OrdinalIgnoreCase) ? BuildColoringProfile() : BuildImageProfile());

        root.Children.Add(Horizontal(
            ActionButton("Scene / Soggetti", ShowScenesAndSubjects),
            AsyncButton("Salva e vai a 2/4", async () =>
            {
                if (!int.TryParse(count.Text, out var parsed) || parsed < 1 || parsed > 500)
                {
                    Report("Inserisci un numero di immagini da 1 a 500.");
                    count.Focus(FocusState.Programmatic);
                    return;
                }
                _document.SetUiInt("Visual.ImageCount", parsed);
                _document.SetUiString("Visual.Subject", subject.Text);
                _document.SetUiString("Visual.Environment", environment.Text);
                _document.SetUiBool("Visual.Consistent", consistent.IsChecked == true);
                _document.SetUiString("Visual.ConsistencyRules", consistent.IsChecked == true ? rules.Text : "");
                await SaveIfPossibleAsync();
                ShowVisualPrompt();
            })));
        SetContent(root);
    }

    private UIElement BuildColoringProfile()
    {
        var style = Combo(ColoringStyles, _document!.GetUiString("Coloring.Style", "Clean Line Art"));
        var audience = Combo(
            ["Prescolare 3–5 anni", "Bambini 6–9 anni", "Ragazzi 10–13 anni", "Adolescenti", "Adulti", "Tutte le età"],
            _document.GetUiString("Coloring.Audience", "Bambini 6–9 anni"));
        var difficulty = Combo(["Molto facile", "Facile", "Media", "Impegnativa"], _document.GetUiString("Coloring.Difficulty", "Facile"));
        var lineWeight = Combo(
            ["Molto spesso — Extra Bold", "Spesso — Bold", "Medio", "Sottile — Fine", "Molto sottile — Extra Fine", "Variabile"],
            _document.GetUiString("Coloring.LineWeight", "Spesso — Bold"));
        var complexity = Combo(["Molto bassa", "Bassa", "Media", "Alta"], _document.GetUiString("Coloring.Complexity", "Bassa"));
        var density = Combo(["Molto bassa", "Bassa", "Media", "Alta"], _document.GetUiString("Coloring.Density", "Bassa"));
        var background = Combo(["Nessuno / bianco", "Semplice / minimo", "Contestuale leggero", "Dettagliato"], _document.GetUiString("Coloring.Background", "Semplice / minimo"));
        var whiteSpace = Combo(["Molto ampio", "Ampio", "Medio", "Compatto"], _document.GetUiString("Coloring.WhiteSpace", "Ampio"));
        var boldEasy = Check("Bold & Easy — HARD indipendente", _document.GetUiBool("Coloring.BoldEasy"));
        var cozy = Check("Cozy — HARD indipendente", _document.GetUiBool("Coloring.Cozy"));
        var closed = Check("Aree chiuse e facili da colorare", _document.GetUiBool("Coloring.ClosedAreas", true));
        var tiny = Check("Evita aree e dettagli minuscoli", _document.GetUiBool("Coloring.AvoidTinyAreas", true));
        var contours = Check("Contorni puliti e continui", _document.GetUiBool("Coloring.CleanContours", true));
        var noText = Check("Niente testo o numeri nell'immagine", _document.GetUiBool("Coloring.NoText", true));
        var separated = Check("Soggetto ben separato dallo sfondo", _document.GetUiBool("Coloring.Separated", true));
        var notes = Editor(_document.GetUiString("Coloring.Notes"), "Note stile Custom / eccezioni.", 85);

        var save = ActionButton("Memorizza profilo Coloring", () =>
        {
            _document.SetUiString("Coloring.Style", style.SelectedItem?.ToString());
            _document.SetUiString("Coloring.Audience", audience.SelectedItem?.ToString());
            _document.SetUiString("Coloring.Difficulty", difficulty.SelectedItem?.ToString());
            _document.SetUiString("Coloring.LineWeight", lineWeight.SelectedItem?.ToString());
            _document.SetUiString("Coloring.Complexity", complexity.SelectedItem?.ToString());
            _document.SetUiString("Coloring.Density", density.SelectedItem?.ToString());
            _document.SetUiString("Coloring.Background", background.SelectedItem?.ToString());
            _document.SetUiString("Coloring.WhiteSpace", whiteSpace.SelectedItem?.ToString());
            _document.SetUiBool("Coloring.BoldEasy", boldEasy.IsChecked == true);
            _document.SetUiBool("Coloring.Cozy", cozy.IsChecked == true);
            _document.SetUiBool("Coloring.ClosedAreas", closed.IsChecked == true);
            _document.SetUiBool("Coloring.AvoidTinyAreas", tiny.IsChecked == true);
            _document.SetUiBool("Coloring.CleanContours", contours.IsChecked == true);
            _document.SetUiBool("Coloring.NoText", noText.IsChecked == true);
            _document.SetUiBool("Coloring.Separated", separated.IsChecked == true);
            _document.SetUiString("Coloring.Notes", notes.Text);
            Report("Profilo Coloring memorizzato.");
        });

        return Card("Stile e leggibilità del Coloring",
            Vertical(
                new TextBlock { Text = "HARD: solo nero puro (#000000) e bianco puro (#FFFFFF). Nessun grigio, colore, ombra o sfumatura.", TextWrapping = TextWrapping.Wrap },
                Labeled("Stile", style),
                Horizontal(Labeled("Pubblico", audience), Labeled("Difficoltà", difficulty)),
                Labeled("Spessore linee", lineWeight),
                Horizontal(Labeled("Complessità", complexity), Labeled("Densità", density)),
                Horizontal(Labeled("Sfondo", background), Labeled("Spazio bianco", whiteSpace)),
                boldEasy, cozy, closed, tiny, contours, noText, separated,
                Labeled("Note stile", notes), save));
    }

    private UIElement BuildImageProfile()
    {
        var use = Combo(["Illustrazione editoriale", "Scheda / catalogo", "Reference", "Decorativa", "Sequenza narrativa"], _document!.GetUiString("ImageProfile.Use", "Illustrazione editoriale"));
        var color = Combo(["Colore", "Bianco e nero", "Monocromatico", "Palette controllata"], _document.GetUiString("ImageProfile.Color", "Colore"));
        var detail = Combo(["Basso", "Medio", "Alto"], _document.GetUiString("ImageProfile.Detail", "Medio"));
        var rendering = Combo(["Pulito editoriale", "Pittorico", "Vettoriale", "Fotografico", "Custom"], _document.GetUiString("ImageProfile.Rendering", "Pulito editoriale"));
        var sameScale = Check("Mantieni scala/inquadratura comparabili nelle serie", _document.GetUiBool("ImageProfile.SameScale", true));
        var readable = Check("Soggetto sempre chiaramente leggibile", _document.GetUiBool("ImageProfile.Readable", true));
        var noText = Check("Evita testo/etichette salvo richiesta", _document.GetUiBool("ImageProfile.NoText", true));
        var notes = Editor(_document.GetUiString("ImageProfile.Notes"), "Note aggiuntive sulla serie.", 85);

        var save = ActionButton("Memorizza profilo immagini", () =>
        {
            _document.SetUiString("ImageProfile.Use", use.SelectedItem?.ToString());
            _document.SetUiString("ImageProfile.Color", color.SelectedItem?.ToString());
            _document.SetUiString("ImageProfile.Detail", detail.SelectedItem?.ToString());
            _document.SetUiString("ImageProfile.Rendering", rendering.SelectedItem?.ToString());
            _document.SetUiBool("ImageProfile.SameScale", sameScale.IsChecked == true);
            _document.SetUiBool("ImageProfile.Readable", readable.IsChecked == true);
            _document.SetUiBool("ImageProfile.NoText", noText.IsChecked == true);
            _document.SetUiString("ImageProfile.Notes", notes.Text);
            Report("Profilo immagini memorizzato.");
        });
        return Card("Profilo Raccolta immagini / Libro illustrato",
            Vertical(
                Labeled("Uso editoriale", use), Labeled("Resa cromatica", color), Labeled("Dettaglio", detail),
                Labeled("Stile resa", rendering), sameScale, readable, noText, Labeled("Note", notes), save));
    }

    private void ShowVisualPrompt()
    {
        if (!RequireDocument()) return;
        var root = PageRoot($"{VisualLabel(_document!.BookType)} · 2/4 Istruzioni",
            "DEVE FARE, NON DEVE FARE e PROMPT sono TextBox Uno reali e modificabili.");
        var mustDo = Editor(_document.GetUiString("Prompt.MustDo"), "Cosa devono rappresentare e come devono essere i risultati.", 130);
        var mustNot = Editor(_document.GetUiString("Prompt.MustNot"), "Cosa deve essere evitato.", 110);
        var prompt = Editor(_document.GetUiString("Prompt.Master"), "Prepara il prompt, poi modificalo liberamente.", 280);

        void PreparePrompt()
        {
            _document.SetUiString("Prompt.MustDo", mustDo.Text);
            _document.SetUiString("Prompt.MustNot", mustNot.Text);
            var built = BuildMasterPrompt(mustDo.Text, mustNot.Text);
            _document.SetUiString("Prompt.Master", built);
            prompt.Text = built;
            Report("Prompt master preparato.");
        }

        root.Children.Add(Labeled("DEVE FARE", mustDo));
        root.Children.Add(Labeled("NON DEVE FARE", mustNot));
        root.Children.Add(Labeled("PROMPT — modificabile", prompt));
        root.Children.Add(Horizontal(
            ActionButton("Prepara prompt", PreparePrompt),
            ActionButton("Copia prompt", () => CopyText(prompt.Text ?? string.Empty)),
            AsyncButton("Salva e vai a 3/4", async () =>
            {
                _document.SetUiString("Prompt.MustDo", mustDo.Text);
                _document.SetUiString("Prompt.MustNot", mustNot.Text);
                _document.SetUiString("Prompt.Master", prompt.Text);
                await SaveIfPossibleAsync();
                ShowPromptPack();
            })));
        SetContent(root);
    }

    private string BuildMasterPrompt(string? mustDo, string? mustNot)
    {
        var type = _document!.BookType;
        var count = Math.Max(1, _document.GetUiInt("Visual.ImageCount", 1));
        var lines = new List<string>
        {
            $"Crea {count} {(count == 1 ? "immagine" : "immagini")} per {VisualLabel(type)}.", "",
            "DEVE FARE:", (mustDo ?? string.Empty).Trim(), "",
            "NON DEVE FARE:", (mustNot ?? string.Empty).Trim(), "",
            "SOGGETTO/I:", _document.GetUiString("Visual.Subject"), "",
            "AMBIENTAZIONE:", _document.GetUiString("Visual.Environment")
        };
        if (_document.GetUiBool("Visual.Consistent"))
        {
            lines.Add(""); lines.Add("CONSISTENT — HARD:"); lines.Add(_document.GetUiString("Visual.ConsistencyRules"));
        }
        if (type.Equals("Coloring book", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(""); lines.Add("PROFILO COLORING:");
            lines.Add($"- Stile: {_document.GetUiString("Coloring.Style", "Clean Line Art")}");
            lines.Add($"- Pubblico: {_document.GetUiString("Coloring.Audience", "Bambini 6–9 anni")}");
            lines.Add($"- Difficoltà: {_document.GetUiString("Coloring.Difficulty", "Facile")}");
            lines.Add($"- Linee: {_document.GetUiString("Coloring.LineWeight", "Spesso — Bold")}");
            lines.Add($"- Bold & Easy HARD: {(_document.GetUiBool("Coloring.BoldEasy") ? "ON" : "OFF")}");
            lines.Add($"- Cozy HARD: {(_document.GetUiBool("Coloring.Cozy") ? "ON" : "OFF")}");
            lines.Add("- SOLO #000000 e #FFFFFF; vietati grigi, colori, ombre e sfumature.");
        }
        lines.Add("");
        lines.Add("Ogni immagine deve essere una singola composizione VISUAL_ONLY e non deve mostrare ID interni, routing, retry, numeri di sessione o nomi file.");
        return string.Join(Environment.NewLine, lines).Trim();
    }

    private void ShowPromptPack()
    {
        if (!RequireDocument()) return;
        var root = PageRoot($"{VisualLabel(_document!.BookType)} · 3/4 Prompt Pack / AI Exchange",
            "Seleziona il provider, conserva il prompt master e prepara una richiesta AI senza contaminare il prompt visuale con metadati interni.");
        var provider = Combo(["ChatGPT / OpenAI", "Gemini", "Altra / nuova AI"], _document.GetUiString("AI.Provider", "ChatGPT / OpenAI"));
        var preferAdvanced = Check("Usa il modello immagini più avanzato disponibile", _document.GetUiBool("AI.PreferAdvanced", true));
        var prompt = Editor(_document.GetUiString("Prompt.Master"), "Prompt master", 300);
        var exchangeNotes = Editor(_document.GetUiString("AI.ExchangeNotes"), "Note di scambio, correzioni o handoff.", 110);

        root.Children.Add(Card("Provider e richiesta",
            Vertical(Labeled("Provider AI", provider), preferAdvanced, Labeled("Prompt provider-facing", prompt), Labeled("Note exchange", exchangeNotes))));
        root.Children.Add(Horizontal(
            ActionButton("Copia Prompt Pack", () => CopyText($"PROVIDER: {provider.SelectedItem}\n\n{prompt.Text}\n\nNOTE:\n{exchangeNotes.Text}")),
            AsyncButton("Crea job AI Ready", async () =>
            {
                _document.SetUiString("AI.Provider", provider.SelectedItem?.ToString());
                _document.SetUiBool("AI.PreferAdvanced", preferAdvanced.IsChecked == true);
                _document.SetUiString("AI.ExchangeNotes", exchangeNotes.Text);
                _document.SetUiString("Prompt.Master", prompt.Text);
                _document.AddAiJob($"{VisualLabel(_document.BookType)} · Prompt Pack", "Image", prompt.Text ?? "");
                await SaveIfPossibleAsync();
                Report("Job AI aggiunto con stato Ready.");
            }),
            ActionButton("Vai a 4/4 Vision", ShowVisionReview)));
        SetContent(root);
    }

    private void ShowVisionReview()
    {
        if (!RequireDocument()) return;
        var root = PageRoot($"{VisualLabel(_document!.BookType)} · 4/4 Response Review / Vision",
            "La UI mantiene visibili i gate HARD: stile, Bold & Easy, Cozy, line weight, singola composizione e scene_participants_match.");
        var jobs = new ListView { Height = 210, ItemsSource = _document.AiJobDisplayItems() };
        var hardChecklist = Vertical(
            Check("style_match — HARD", true),
            Check("bold_easy_match — HARD quando attivo", true),
            Check("cozy_match — HARD quando attivo", true),
            Check("line_weight_match — HARD", true),
            Check("single_composition — HARD", true),
            Check("scene_participants_match — HARD", true));
        var reviewNotes = Editor(_document.GetUiString("Vision.ReviewNotes"), "Esito Vision, correzioni richieste, motivi di rifiuto.", 150);
        root.Children.Add(Card("Candidati / job AI", jobs));
        root.Children.Add(Card("Vision HARD gates", hardChecklist));
        root.Children.Add(Card("Revisione", Vertical(reviewNotes,
            AsyncButton("Salva revisione", async () =>
            {
                _document.SetUiString("Vision.ReviewNotes", reviewNotes.Text);
                await SaveIfPossibleAsync();
                Report("Revisione Vision salvata.");
            }))));
        SetContent(root);
    }

    private void ShowScenesAndSubjects()
    {
        if (!RequireDocument()) return;
        var root = PageRoot("Scene strutturate / Soggetti",
            "Le scene usano ID stabili: rinominare numero, nome o descrizione non cambia l'identità. Gli ID archiviati non vengono riciclati.");
        var mode = Combo(["Ambientazione generica", "Definisci scene"], _document!.GetUiString("Scenes.Mode", "Ambientazione generica"));
        var generic = Editor(_document.GetUiString("Scenes.GenericEnvironment"), "Ambientazione generica del progetto.", 120);
        var sceneList = new ListView { Height = 180 };
        var name = Editor("", "Nome scena", 42, false);
        var description = Editor("", "Descrizione scena", 120);
        var active = Check("Scena attiva", true);

        void RefreshScenes()
        {
            sceneList.ItemsSource = _document.Scenes()
                .Select(s => $"{(s.IsActive ? "●" : "○")} {s.Name} · {s.SceneId[..Math.Min(8, s.SceneId.Length)]}")
                .ToList();
        }
        sceneList.SelectionChanged += (_, _) =>
        {
            var scenes = _document.Scenes();
            if (sceneList.SelectedIndex < 0 || sceneList.SelectedIndex >= scenes.Count) return;
            var scene = scenes[sceneList.SelectedIndex];
            name.Text = scene.Name;
            description.Text = scene.Description;
            active.IsChecked = scene.IsActive;
        };
        var add = ActionButton("+ Nuova scena", () =>
        {
            var scene = _document.AddScene();
            RefreshScenes();
            sceneList.SelectedIndex = Math.Max(0, _document.Scenes().Count - 1);
            name.Text = scene.Name;
            description.Text = scene.Description;
            active.IsChecked = true;
            Report($"Nuova scena creata con ID stabile {scene.SceneId}.");
        });
        var save = AsyncButton("Salva scena corrente", async () =>
        {
            var scenes = _document.Scenes();
            if (sceneList.SelectedIndex >= 0 && sceneList.SelectedIndex < scenes.Count)
            {
                var current = scenes[sceneList.SelectedIndex];
                _document.UpdateScene(current with
                {
                    Name = string.IsNullOrWhiteSpace(name.Text) ? current.Name : name.Text.Trim(),
                    Description = description.Text ?? "",
                    IsActive = active.IsChecked == true
                });
            }
            _document.SetUiString("Scenes.Mode", mode.SelectedItem?.ToString());
            _document.SetUiString("Scenes.GenericEnvironment", generic.Text);
            await SaveIfPossibleAsync();
            RefreshScenes();
            Report("Scene e ambiente salvati.");
        });
        RefreshScenes();
        root.Children.Add(Card("Modalità ambiente", Vertical(Labeled("Modalità", mode), Labeled("Ambientazione generica", generic))));
        root.Children.Add(Card("Scene", Vertical(sceneList, Labeled("Nome", name), Labeled("Descrizione", description), active, Horizontal(add, save))));
        root.Children.Add(Card("Partecipazione soggetti",
            new TextBlock { Text = "Contratto preservato: la partecipazione è keyed da SubjectId + SceneId, non dai nomi visibili. La UI Uno non ricicla SceneId.", TextWrapping = TextWrapping.Wrap }));
        SetContent(root);
    }

    private void ShowWordSearch()
    {
        if (!RequireDocument()) return;
        var root = PageRoot("Word Search · workspace",
            "Database, colonne-puzzle, lessico, sostituzione contestuale, validazione e destinazioni di export sono raccolti in un’unica superficie.");
        var database = Editor(_document!.GetUiString("WordSearch.Database"), "Incolla o importa righe/colonne del database Word Search. Ogni colonna può rappresentare un puzzle.", 210);
        var lexicon = Editor(_document.GetUiString("WordSearch.Lexicon"), "Lessico di riserva / parole non ancora usate.", 140);
        var expected = Editor(_document.GetUiString("WordSearch.ExpectedCount", "10"), "Numero atteso di parole per puzzle", 42, false);
        var replacement = Editor(_document.GetUiString("WordSearch.Replacement"), "Parola da sostituire → nuova parola contestuale dal lessico.", 80);
        var exportMode = Combo(["Locale", "Google Sheets", "Locale + Google Sheets"], _document.GetUiString("WordSearch.ExportMode", "Locale"));
        var validation = new TextBlock { Text = BuildWordSearchValidation(database.Text, expected.Text), TextWrapping = TextWrapping.Wrap };
        database.TextChanged += (_, _) => validation.Text = BuildWordSearchValidation(database.Text, expected.Text);
        expected.TextChanged += (_, _) => validation.Text = BuildWordSearchValidation(database.Text, expected.Text);
        root.Children.Add(Card("Database / colonne puzzle", Vertical(Labeled("Database", database), Labeled("Parole attese per puzzle", expected), validation)));
        root.Children.Add(Card("Lessico e sostituzione chirurgica", Vertical(Labeled("Lessico disponibile", lexicon), Labeled("Sostituzione contestuale", replacement))));
        root.Children.Add(Card("Export", Vertical(Labeled("Destinazione", exportMode), Horizontal(
            ActionButton("Copia database", () => CopyText(database.Text ?? "")),
            AsyncButton("Esporta TXT/CSV", async () => await ExportTextAsync("word-search-database.csv", database.Text ?? ""))))));
        root.Children.Add(AsyncButton("Salva workspace Word Search", async () =>
        {
            _document.SetUiString("WordSearch.Database", database.Text);
            _document.SetUiString("WordSearch.Lexicon", lexicon.Text);
            _document.SetUiString("WordSearch.ExpectedCount", expected.Text);
            _document.SetUiString("WordSearch.Replacement", replacement.Text);
            _document.SetUiString("WordSearch.ExportMode", exportMode.SelectedItem?.ToString());
            await SaveIfPossibleAsync();
            Report("Workspace Word Search salvato.");
        }));
        SetContent(root);
    }

    private static string BuildWordSearchValidation(string? text, string? expectedText)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var expected = int.TryParse(expectedText, out var n) && n > 0 ? n : 0;
        return $"Righe non vuote: {lines.Length}" + (expected > 0 ? $" · target dichiarato: {expected} parole per puzzle. Usa l’export completo per reimportare il DB senza perdita." : "");
    }

    private void ShowCrossword()
    {
        if (!RequireDocument()) return;
        var root = PageRoot("Cruciverba · workspace", "Tema, parole, definizioni editabili, ruoli tematici e handoff Qxw restano nello stesso workspace.");
        var theme = Editor(_document!.GetUiString("Crossword.Theme"), "Tema del cruciverba", 70);
        var words = Editor(_document.GetUiString("Crossword.Words"), "Una voce per riga: PAROLA | definizione | ruolo/tema", 250);
        var qxw = Editor(_document.GetUiString("Crossword.Qxw"), "Lista pronta per Qxw / tool esterno.", 150);
        var adaptive = Check("Tipo adattivo: adatta definizioni e difficoltà al pubblico", _document.GetUiBool("Crossword.Adaptive", true));
        root.Children.Add(Card("Tema e definizioni", Vertical(Labeled("Tema", theme), adaptive, Labeled("Griglia parole/definizioni", words))));
        root.Children.Add(Card("Qxw / handoff", Vertical(Labeled("Lista Qxw", qxw), Horizontal(
            ActionButton("Copia Qxw", () => CopyText(qxw.Text ?? "")),
            AsyncButton("Esporta lista", async () => await ExportTextAsync("crossword-qxw.txt", qxw.Text ?? ""))))));
        root.Children.Add(AsyncButton("Salva workspace Cruciverba", async () =>
        {
            _document.SetUiString("Crossword.Theme", theme.Text);
            _document.SetUiString("Crossword.Words", words.Text);
            _document.SetUiString("Crossword.Qxw", qxw.Text);
            _document.SetUiBool("Crossword.Adaptive", adaptive.IsChecked == true);
            await SaveIfPossibleAsync();
            Report("Workspace Cruciverba salvato.");
        }));
        SetContent(root);
    }

    private void ShowImageCollection()
    {
        if (!RequireDocument()) return;
        var root = PageRoot("Raccolta immagini · descrizioni e layout",
            "Descrizioni, scelta layout, coerenza di serie ed export interno/esterno/combinato sono portati nella nuova UI.");
        var descriptions = Editor(_document!.GetUiString("ImageCollection.Descriptions"), "Descrizione per ogni immagine / posizione editoriale.", 240);
        var layout = Combo(["Una immagine per pagina", "Griglia", "Immagine + descrizione", "Sequenza narrativa", "Custom"], _document.GetUiString("ImageCollection.Layout", "Immagine + descrizione"));
        var mode = Combo(["Interno", "Esterno", "Combinato"], _document.GetUiString("ImageCollection.ExportMode", "Combinato"));
        var rules = Editor(_document.GetUiString("ImageCollection.LayoutRules"), "Regole layout, margini, didascalie, ordine.", 110);
        root.Children.Add(Card("Descrizioni", Labeled("Descrizioni immagini", descriptions)));
        root.Children.Add(Card("Layout", Vertical(Labeled("Modalità layout", layout), Labeled("Regole", rules))));
        root.Children.Add(Card("Export raccolta", Vertical(Labeled("Output", mode), Horizontal(
            AsyncButton("Esporta descrizioni TXT", async () => await ExportTextAsync("image-descriptions.txt", descriptions.Text ?? "")),
            ActionButton("Vai a Finalizzazione", ShowExportAndFinalization)))));
        root.Children.Add(AsyncButton("Salva workspace Raccolta immagini", async () =>
        {
            _document.SetUiString("ImageCollection.Descriptions", descriptions.Text);
            _document.SetUiString("ImageCollection.Layout", layout.SelectedItem?.ToString());
            _document.SetUiString("ImageCollection.ExportMode", mode.SelectedItem?.ToString());
            _document.SetUiString("ImageCollection.LayoutRules", rules.Text);
            await SaveIfPossibleAsync();
            Report("Workspace Raccolta immagini salvato.");
        }));
        SetContent(root);
    }

    private void ShowNarrative()
    {
        if (!RequireDocument()) return;
        var root = PageRoot("Narrativa / Manuale · workspace", "Workspace unificato per struttura, contenuti, note, illustrazioni e handoff editoriale.");
        var outline = Editor(_document!.GetUiString("Narrative.Outline"), "Scaletta / capitoli / sezioni.", 220);
        var notes = Editor(_document.GetUiString("Narrative.Notes"), "Note narrative, manualistiche o redazionali.", 160);
        var illustrationPlan = Editor(_document.GetUiString("Narrative.IllustrationPlan"), "Piano illustrazioni: contenuto, posizione, didascalia.", 150);
        root.Children.Add(Card("Struttura", Labeled("Outline", outline)));
        root.Children.Add(Card("Note", Labeled("Note editoriali", notes)));
        root.Children.Add(Card("Illustrazioni", Labeled("Piano illustrazioni", illustrationPlan)));
        root.Children.Add(AsyncButton("Salva workspace", async () =>
        {
            _document.SetUiString("Narrative.Outline", outline.Text);
            _document.SetUiString("Narrative.Notes", notes.Text);
            _document.SetUiString("Narrative.IllustrationPlan", illustrationPlan.Text);
            await SaveIfPossibleAsync();
            Report("Workspace narrativo salvato.");
        }));
        SetContent(root);
    }

    private void ShowEditableMaster()
    {
        if (!RequireDocument()) return;
        var root = PageRoot("Editable Master / Struttura editoriale",
            "La nuova UI espone la struttura già presente nel .diez e mantiene separati Master modificabile e originali incorporati.");
        var list = new ListView { Height = 300, ItemsSource = _document!.ContentDisplayItems() };
        var editor = Editor(_document.GetUiString("Master.ManualDraft"), "Bozza / modifica manuale del contenuto selezionato.", 230);
        root.Children.Add(Card("Struttura editoriale", list));
        root.Children.Add(Card("Modifica Master", Vertical(editor,
            new TextBlock
            {
                Text = "Gli originali importati rimangono nel pacchetto. In questa prima migrazione Uno l’editor manuale è conservato separatamente finché il servizio EditableMaster viene estratto dal layer Avalonia.",
                TextWrapping = TextWrapping.Wrap
            },
            AsyncButton("Salva bozza Master", async () =>
            {
                _document.SetUiString("Master.ManualDraft", editor.Text);
                await SaveIfPossibleAsync();
                Report("Bozza Master salvata senza modificare gli originali.");
            }))));
        SetContent(root);
    }

    private void ShowContentGraph()
    {
        if (!RequireDocument()) return;
        var root = PageRoot("Content Graph / Bible", "Entità, relazioni e Bible già presenti nel progetto restano leggibili durante la migrazione.");
        var entities = new ListView { Height = 320, ItemsSource = _document!.EntityDisplayItems() };
        var notes = Editor(_document.GetUiString("Graph.Notes"), "Note su entità, relazione, autorità o Bible.", 150);
        root.Children.Add(Card("Entità", entities));
        root.Children.Add(Card("Note Graph / Bible", Vertical(notes, AsyncButton("Salva note", async () =>
        {
            _document.SetUiString("Graph.Notes", notes.Text);
            await SaveIfPossibleAsync();
            Report("Note Graph/Bible salvate.");
        }))));
        SetContent(root);
    }

    private void ShowConsistency()
    {
        if (!RequireDocument()) return;
        var root = PageRoot("Consistency Review / Revision Candidate",
            "La UI porta lo stato dei problemi e i comandi di revisione; gli engine di riconciliazione restano dati core da estrarre dal progetto Avalonia.");
        var issues = new ListView { Height = 300, ItemsSource = _document!.IssueDisplayItems() };
        var note = Editor(_document.GetUiString("Consistency.Note"), "Nota di review / eccezione / risoluzione.", 130);
        var action = Combo(["Reviewed", "AcceptedException", "Resolved", "Open"], _document.GetUiString("Consistency.Action", "Reviewed"));
        root.Children.Add(Card("Problemi di coerenza", issues));
        root.Children.Add(Card("Review", Vertical(Labeled("Stato", action), Labeled("Nota", note),
            AsyncButton("Memorizza review Uno", async () =>
            {
                _document.SetUiString("Consistency.Action", action.SelectedItem?.ToString());
                _document.SetUiString("Consistency.Note", note.Text);
                await SaveIfPossibleAsync();
                Report("Review memorizzata. Il mutatore core delle issue verrà collegato senza cambiare questo layout.");
            }))));
        SetContent(root);
    }

    private void ShowAiCenter()
    {
        if (!RequireDocument()) return;
        var root = PageRoot("AI Production / Human Prompt / Exchange",
            "Provider, brief comune, MUST DO / MUST NOT, job e scambio risposte sono unificati.");
        var provider = Combo(["ChatGPT / OpenAI", "Gemini", "Altra / nuova AI"], _document!.GetUiString("AI.Provider", "ChatGPT / OpenAI"));
        var brief = Editor(_document.GetUiString("AI.ProjectBrief"), "Regole comuni del progetto.", 140);
        var humanPrompt = Editor(_document.GetUiString("AI.HumanPrompt"), "Istruzione umana modificabile per testo, immagini o dati.", 180);
        var outputType = Combo(["Image", "Text", "Data"], _document.GetUiString("AI.OutputType", "Image"));
        var jobs = new ListView { Height = 220, ItemsSource = _document.AiJobDisplayItems() };
        root.Children.Add(Card("Configurazione AI", Vertical(
            Labeled("Provider", provider), Labeled("Output", outputType), Labeled("Brief comune", brief), Labeled("Human prompt", humanPrompt))));
        root.Children.Add(Card("Job", jobs));
        root.Children.Add(Horizontal(
            AsyncButton("Crea job Ready", async () =>
            {
                _document.SetUiString("AI.Provider", provider.SelectedItem?.ToString());
                _document.SetUiString("AI.OutputType", outputType.SelectedItem?.ToString());
                _document.SetUiString("AI.ProjectBrief", brief.Text);
                _document.SetUiString("AI.HumanPrompt", humanPrompt.Text);
                _document.AddAiJob("Human prompt", outputType.SelectedItem?.ToString() ?? "Image", humanPrompt.Text ?? "");
                await SaveIfPossibleAsync();
                ShowAiCenter();
                Report("Job AI Ready creato.");
            }),
            ActionButton("Copia richiesta", () => CopyText(humanPrompt.Text ?? "")),
            ActionButton("Response Review", ShowVisionReview)));
        SetContent(root);
    }

    private void ShowExportAndFinalization()
    {
        if (!RequireDocument()) return;
        var root = PageRoot("Export / Edizione / Handoff",
            "Destinazione locale, Google o entrambe; metadata, freeze ed handoff sono raccolti nella stessa pagina.");
        var destination = Combo(["Locale", "Google Drive / Docs / Sheets", "Locale + Google"], _document!.GetUiString("Export.Destination", "Locale"));
        var formats = Editor(_document.GetUiString("Export.Formats", "DOCX\nPDF\nTXT/CSV quando applicabile"), "Un formato per riga.", 100);
        var metadata = Editor(_document.GetUiString("Export.Metadata"), $"Titolo: {_document.EditionTitle}\nLingua: it\nEditore:\nISBN:", 160);
        var freeze = Check("Crea snapshot / freeze dell’edizione prima dell’export", _document.GetUiBool("Export.Freeze", true));
        var handoff = Editor(_document.GetUiString("Export.Handoff"), "Note handoff, link Google, cartella locale, destinatario.", 130);
        root.Children.Add(Card("Destinazione e formati", Vertical(Labeled("Destinazione", destination), Labeled("Formati", formats), freeze)));
        root.Children.Add(Card("Metadata edizione", metadata));
        root.Children.Add(Card("Handoff", handoff));
        root.Children.Add(Horizontal(
            AsyncButton("Salva configurazione", async () =>
            {
                _document.SetUiString("Export.Destination", destination.SelectedItem?.ToString());
                _document.SetUiString("Export.Formats", formats.Text);
                _document.SetUiString("Export.Metadata", metadata.Text);
                _document.SetUiBool("Export.Freeze", freeze.IsChecked == true);
                _document.SetUiString("Export.Handoff", handoff.Text);
                await SaveIfPossibleAsync();
                Report("Configurazione export/finalizzazione salvata.");
            }),
            AsyncButton("Esporta project.json diagnostico", async () => await ExportTextAsync("project-export.json", _document.ExportProjectJson())),
            ActionButton("Apri libreria finalizzati", ShowFinalizedLibrary)));
        SetContent(root);
    }

    private void ShowFinalizedLibrary()
    {
        if (!RequireDocument()) return;
        var root = PageRoot("Libreria libri finalizzati",
            "Archivio, rigenerazione e destinazioni locali/Google restano disponibili come superficie Uno.");
        var archive = Editor(_document!.GetUiString("Finalized.Archive"), "Una voce per riga: titolo | edizione | data | percorso/link | stato.", 260);
        var selectedLink = Editor(_document.GetUiString("Finalized.LastGoogleLink"), "Link Google ricordato / percorso locale.", 70);
        root.Children.Add(Card("Archivio", archive));
        root.Children.Add(Card("Apertura output", Vertical(Labeled("Percorso / link", selectedLink),
            new TextBlock
            {
                Text = "La logica di apertura locale/Google e rigenerazione verrà collegata ai servizi FinalizedLibrary senza reintrodurre finestre Avalonia.",
                TextWrapping = TextWrapping.Wrap
            })));
        root.Children.Add(AsyncButton("Salva libreria", async () =>
        {
            _document.SetUiString("Finalized.Archive", archive.Text);
            _document.SetUiString("Finalized.LastGoogleLink", selectedLink.Text);
            await SaveIfPossibleAsync();
            Report("Libreria finalizzati salvata.");
        }));
        SetContent(root);
    }

    private async Task CreateProjectAsync()
    {
        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = "NuovoProgetto" };
            picker.FileTypeChoices.Add("Progetto Diez", new List<string> { ".diez" });
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            _document = DiezProjectDocument.Create(Path.GetFileNameWithoutExtension(file.Path));
            _projectPath = file.Path;
            await _document.SaveAsync(_projectPath);
            RefreshHeader();
            ShowHome();
            Report($"Creato: {_projectPath}");
        }
        catch (Exception ex) { Report("Errore creazione: " + ex.GetBaseException().Message); }
    }

    private async Task OpenProjectAsync()
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".diez");
            picker.FileTypeFilter.Add(".json");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            _document = await DiezProjectDocument.LoadAsync(file.Path);
            _projectPath = file.Path;
            RefreshHeader();
            ShowHome();
            Report($"Aperto: {_document.Name}");
        }
        catch (Exception ex) { Report("Errore apertura: " + ex.GetBaseException().Message); }
    }

    private async Task ImportMaterialsAsync()
    {
        if (!RequireDocument()) return;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            var files = await picker.PickMultipleFilesAsync();
            if (files.Count == 0) return;
            var imported = 0;
            var duplicates = 0;
            foreach (var file in files)
            {
                var result = await _document!.ImportMaterialAsync(file.Path);
                if (result.StartsWith("Importato", StringComparison.OrdinalIgnoreCase)) imported++;
                else if (result.StartsWith("Duplicato", StringComparison.OrdinalIgnoreCase)) duplicates++;
            }
            await SaveIfPossibleAsync();
            ShowHome();
            Report($"Materiali: {imported} importati · {duplicates} duplicati ignorati.");
        }
        catch (Exception ex) { Report("Errore importazione: " + ex.GetBaseException().Message); }
    }

    private async Task SaveProjectAsync()
    {
        if (!RequireDocument()) return;
        try
        {
            if (string.IsNullOrWhiteSpace(_projectPath))
            {
                var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = SafeFileName(_document!.Name) };
                picker.FileTypeChoices.Add("Progetto Diez", new List<string> { ".diez" });
                var file = await picker.PickSaveFileAsync();
                if (file is null) return;
                _projectPath = file.Path;
            }
            await _document!.SaveAsync(_projectPath);
            RefreshHeader();
            Report("Progetto salvato.");
        }
        catch (Exception ex) { Report("Errore salvataggio: " + ex.GetBaseException().Message); }
    }

    private async Task SaveIfPossibleAsync()
    {
        if (_document is null || string.IsNullOrWhiteSpace(_projectPath)) return;
        await _document.SaveAsync(_projectPath);
    }

    private async Task ExportTextAsync(string suggestedName, string content)
    {
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName)
            };
            var extension = Path.GetExtension(suggestedName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".txt";
            picker.FileTypeChoices.Add("File", new List<string> { extension });
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await File.WriteAllTextAsync(file.Path, content);
            Report($"Esportato: {file.Path}");
        }
        catch (Exception ex) { Report("Errore export: " + ex.GetBaseException().Message); }
    }

    private bool RequireDocument()
    {
        if (_document is not null) return true;
        Report("Prima crea o apri un progetto .diez.");
        ShowHome();
        return false;
    }

    private void RefreshHeader() => _projectHeader.Text = _document is null
        ? "Nessun progetto aperto"
        : $"{_document.Name}\n{(_document.BookType.Length == 0 ? "Tipo libro non scelto" : _document.BookType)}";

    private void Report(string message) => _status.Text = message;
    private void SetContent(UIElement content) => _contentHost.Content = content;

    private static StackPanel PageRoot(string title, string description)
    {
        var root = new StackPanel { Spacing = 16, Margin = new Thickness(28), MaxWidth = 1050, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(new TextBlock { Text = title, FontSize = 28, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new Separator());
        return root;
    }

    private static Border Card(string title, UIElement content) => new()
    {
        Padding = new Thickness(16), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
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

    private static StackPanel Labeled(string label, UIElement control) => Vertical(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, control);

    private static TextBox Editor(string text, string placeholder, double minHeight, bool multiline = true) => new()
    {
        Text = text ?? string.Empty, PlaceholderText = placeholder, MinHeight = minHeight, AcceptsReturn = multiline,
        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static ComboBox Combo(IEnumerable<string> values, string selected)
    {
        var items = values.ToList();
        var combo = new ComboBox { ItemsSource = items, MinWidth = 230, HorizontalAlignment = HorizontalAlignment.Left };
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

    private static Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static Button AsyncButton(string text, Func<Task> action) => ActionButton(text, action);

    private static bool IsVisualType(string type) =>
        type.Equals("Coloring book", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("Raccolta immagini", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("Libro illustrato", StringComparison.OrdinalIgnoreCase);

    private static string VisualLabel(string type) => type switch
    {
        "Raccolta immagini" => "Raccolta immagini",
        "Libro illustrato" => "Libro illustrato · Illustrazioni",
        _ => "Coloring Book"
    };

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim();
        return result.Length == 0 ? "NuovoProgetto" : result;
    }

    private static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}
