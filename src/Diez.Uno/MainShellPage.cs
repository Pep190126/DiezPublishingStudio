using DiezPublishingStudio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace DiezPublishingStudio.UnoSpike;

public sealed class MainShellPage : Page
{
    private static readonly string[] BookTypes = BookTypeCatalog.All.ToArray();

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
        var type = Combo(BookTypes, string.IsNullOrWhiteSpace(_document.BookType) ? BookTypeCatalog.ColoringBook : BookTypeCatalog.Normalize(_document.BookType));
        var save = AsyncButton("Salva identità libro", async () =>
        {
            _document.EditionTitle = title.Text?.Trim() ?? string.Empty;
            _document.BookType = BookTypeCatalog.Normalize(type.SelectedItem?.ToString());
            await SaveIfPossibleAsync();
            RefreshHeader();
            Report("Titolo e Tipo libro salvati.");
        });
        var next = AsyncButton("Continua nel workspace", async () =>
        {
            _document.EditionTitle = title.Text?.Trim() ?? string.Empty;
            _document.BookType = BookTypeCatalog.Normalize(type.SelectedItem?.ToString());
            await SaveIfPossibleAsync();
            RefreshHeader();
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
        var type = BookTypeCatalog.Normalize(_document?.BookType);
        if (BookTypeCatalog.IsVisual(type)) ShowVisualQuantity();
        else if (type.Equals(BookTypeCatalog.WordSearch, StringComparison.OrdinalIgnoreCase)) ShowWordSearch();
        else if (type.Equals(BookTypeCatalog.Crossword, StringComparison.OrdinalIgnoreCase)) ShowCrossword();
        else if (BookTypeCatalog.IsLongForm(type)) ShowNarrative();
        else if (type.Equals(BookTypeCatalog.Quiz, StringComparison.OrdinalIgnoreCase) ||
                 type.Equals(BookTypeCatalog.DataCollection, StringComparison.OrdinalIgnoreCase) ||
                 type.Equals(BookTypeCatalog.Other, StringComparison.OrdinalIgnoreCase))
            ShowBookFamilyWorkspace(type);
        else ShowBookRoute();
    }

    private void ShowBookFamilyWorkspace(string bookType)
    {
        if (!RequireDocument()) return;
        var normalized = BookTypeCatalog.Normalize(bookType);
        var current = BookTypeCatalog.Normalize(_document!.BookType);
        if (!string.Equals(normalized, current, StringComparison.OrdinalIgnoreCase))
        {
            Report("Scegli prima questo tipo di libro dal percorso libro.");
            ShowBookRoute();
            return;
        }

        SetContent(BookFamilyWorkspace.Build(
            _document,
            normalized,
            SaveIfPossibleAsync,
            Report,
            ShowAiCenter,
            ShowEditableMaster,
            ShowExportAndFinalization));
    }

    private void ShowVisualQuantity() => ShowVisualWorkspace();
    private void ShowVisualPrompt() => ShowVisualWorkspace();
    private void ShowPromptPack() => ShowVisualWorkspace();

    private void ShowVisualWorkspace()
    {
        if (!RequireDocument()) return;
        var type = BookTypeCatalog.Normalize(_document!.BookType);
        if (!BookTypeCatalog.IsVisual(type))
        {
            Report("Il Tipo libro attuale non usa il percorso immagini. Apro la scelta Tipo libro.");
            ShowBookRoute();
            return;
        }

        SetContent(VisualBookWorkspace.Build(
            _document,
            SaveIfPossibleAsync,
            Report,
            ShowVisualWorkspace,
            ShowVisionReview,
            ShowAiCenter));
    }

    private void ShowVisionReview()
    {
        if (!RequireDocument()) return;
        SetContent(VisionWorkspace.Build(
            _document!,
            SaveIfPossibleAsync,
            Report,
            ShowVisionReview,
            ShowAiCenter));
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
        SetContent(WordSearchWorkspace.Build(
            _document!,
            SaveIfPossibleAsync,
            Report,
            ShowWordSearch,
            ExportTextAsync));
    }

    private void ShowCrossword()
    {
        if (!RequireDocument()) return;
        SetContent(CrosswordWorkspace.Build(
            _document!,
            SaveIfPossibleAsync,
            Report,
            ShowCrossword,
            ExportTextAsync));
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
        var type = BookTypeCatalog.Normalize(_document!.BookType);
        if (!BookTypeCatalog.IsLongForm(type))
        {
            Report("Il tipo di libro attuale non usa il workspace long-form. Apro la scelta Tipo libro.");
            ShowBookRoute();
            return;
        }

        var prefix = type.Equals(BookTypeCatalog.EssayManual, StringComparison.OrdinalIgnoreCase) ? "EssayManual" : "Novel";
        var title = type.Equals(BookTypeCatalog.EssayManual, StringComparison.OrdinalIgnoreCase) ? "Saggio / manuale" : "Romanzo / racconto";
        var root = PageRoot($"{title} · workspace", "Workspace unificato per struttura, contenuti, note, illustrazioni e handoff editoriale.");
        var outline = Editor(_document.GetUiString($"{prefix}.Outline", _document.GetUiString("Narrative.Outline")), "Scaletta / capitoli / sezioni.", 220);
        var notes = Editor(_document.GetUiString($"{prefix}.Notes", _document.GetUiString("Narrative.Notes")), "Note narrative, manualistiche o redazionali.", 160);
        var illustrationPlan = Editor(_document.GetUiString($"{prefix}.IllustrationPlan", _document.GetUiString("Narrative.IllustrationPlan")), "Piano illustrazioni: contenuto, posizione, didascalia.", 150);
        root.Children.Add(Card("Struttura", Labeled("Outline", outline)));
        root.Children.Add(Card("Note", Labeled("Note editoriali", notes)));
        root.Children.Add(Card("Illustrazioni", Labeled("Piano illustrazioni", illustrationPlan)));
        root.Children.Add(AsyncButton("Salva workspace", async () =>
        {
            _document.SetUiString($"{prefix}.Outline", outline.Text);
            _document.SetUiString($"{prefix}.Notes", notes.Text);
            _document.SetUiString($"{prefix}.IllustrationPlan", illustrationPlan.Text);
            await SaveIfPossibleAsync();
            Report($"Workspace {title} salvato.");
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
        SetContent(AiCenterWorkspace.Build(
            _document!,
            SaveIfPossibleAsync,
            Report,
            ShowAiCenter,
            ShowVisionReview,
            RouteCurrentBookType));
    }

    private void ShowExportAndFinalization()
    {
        if (!RequireDocument()) return;
        var root = PageRoot("Export / Edizione / Handoff",
            "Destinazione locale, Google o entrambe; metadata, freeze ed handoff sono raccolti nella stessa pagina.");
        var destination = Combo(["Locale", "Google Drive / Docs / Sheets", "Locale + Google"], _document!.GetUiString("Export.Destination", "Locale"));
        var formats = Editor(_document.GetUiString("Export.Formats", "DOCX\nPDF\nTXT/CSV quando applicabile"), "Un formato per riga.", 100);
        var metadata = Editor(_document.GetUiString("Export.Metadata", $"Titolo: {_document.EditionTitle}\nLingua: it\nEditore:\nISBN:"), "Metadata edizione", 160);
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

    private static bool IsVisualType(string type) => BookTypeCatalog.IsVisual(type);

    private static string VisualLabel(string type) => BookTypeCatalog.Normalize(type) switch
    {
        BookTypeCatalog.ImageCollection => "Raccolta immagini",
        BookTypeCatalog.IllustratedBook => "Libro illustrato · Illustrazioni",
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