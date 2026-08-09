using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

internal static class CrosswordWorkspaceUi
{
    private const int DefinitionPageSize = 100;

    public static IReadOnlyList<object?> Build(MainWindow window, PreviewProject project) =>
    [
        BuildDatabase(window, project),
        BuildBookType(window, project),
        BuildChecks(project),
        BuildAi(window, project),
        BuildExport(window, project)
    ];

    private static Control BuildDatabase(MainWindow window, PreviewProject project)
    {
        var words = new ListBox { Margin = new Thickness(8) };
        var wordStatus = new TextBlock { Margin = new Thickness(8, 4), TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        void RefreshWords()
        {
            var data = CrosswordService.Words(project);
            words.ItemsSource = data.Select(w => w.Name).ToList();
            wordStatus.Text = $"{data.Count:N0} parole nel vocabolario del progetto. Qxw riceverà un TXT ordinato e senza duplicati.";
        }

        var import = new Button { Content = "Importa liste TXT / DIC…", MinWidth = 180 };
        import.Click += async (_, _) =>
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importa parole per cruciverba",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Liste parole") { Patterns = ["*.txt", "*.dic"] }]
            });
            if (files.Count == 0) return;
            var added = 0; var existing = 0; var ignored = 0;
            foreach (var file in files)
            {
                var result = await CrosswordService.ImportWordListAsync(project, file.Path.LocalPath);
                added += result.Added; existing += result.Existing; ignored += result.Ignored;
            }
            await SaveCurrentAsync(window, project);
            RefreshWords();
            wordStatus.Text += $" Importate: {added:N0} nuove · {existing:N0} già presenti · {ignored:N0} righe ignorate.";
        };

        var remove = new Button { Content = "Escludi parola", MinWidth = 130 };
        remove.Click += async (_, _) =>
        {
            if (words.SelectedItem is not string selected) return;
            var entity = CrosswordService.FindWord(project, selected);
            if (entity is null) return;
            project.Entities.Remove(entity);
            project.BibleEntries.RemoveAll(b => b.SubjectEntityId == entity.EntityId);
            await SaveCurrentAsync(window, project);
            RefreshWords();
        };
        RefreshWords();

        var wordPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 6,
            Margin = new Thickness(6),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { import, remove } },
                wordStatus.WithGridRow(1),
                words.WithGridRow(2)
            }
        };

        var definitions = BuildDefinitionsGrid(window, project);
        return SubTabs
        ([
            new TabItem { Header = "Parole", Content = wordPanel },
            new TabItem { Header = "Definizioni", Content = definitions },
            new TabItem { Header = "Fonti", Content = MessagePanel("Fonti", "Le liste importate possono provenire da più lingue. Diez le riunisce nel vocabolario del progetto senza obbligare Qxw a conoscere la provenienza.") },
            new TabItem { Header = "Liste speciali", Content = MessagePanel("Liste speciali", "Qui vivranno parole obbligatorie/preferite del tema, nomi propri, sigle, termini tecnici e parole di soccorso.") },
            new TabItem { Header = "Esclusioni", Content = MessagePanel("Esclusioni", "Le parole escluse dal progetto verranno tenute fuori dal TXT usato per riempire la griglia.") }
        ]);
    }

    private static Control BuildDefinitionsGrid(MainWindow window, PreviewProject project)
    {
        var body = new StackPanel { Spacing = 4 };
        var pageLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var search = new TextBox { Width = 220, Watermark = "Cerca parola" };
        var page = 0;

        var header = DefinitionRowGrid();
        var headers = new[] { "PAROLA", "DEFINIZIONE 1", "DEFINIZIONE 2", "DEFINIZIONE 3", "DEFINIZIONE 4", "APPROVATA", "NOTE" };
        for (var i = 0; i < headers.Length; i++)
        {
            var text = new TextBlock { Text = headers[i], FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Thickness(5) };
            Grid.SetColumn(text, i);
            header.Children.Add(text);
        }

        async Task SaveCell(Guid id, int column, string? value)
        {
            if (column is >= 1 and <= 4) CrosswordService.SetDefinitionCell(project, id, column, value);
            else if (column == 5) CrosswordService.SetApproved(project, id, value);
            else if (column == 6) CrosswordService.SetNotes(project, id, value);
            await SaveCurrentAsync(window, project);
        }

        void Refresh()
        {
            body.Children.Clear();
            var filter = (search.Text ?? string.Empty).Trim();
            var rows = CrosswordService.DefinitionRows(project)
                .Where(r => filter.Length == 0 || r.Word.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var pages = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)DefinitionPageSize));
            page = Math.Clamp(page, 0, pages - 1);
            var visible = rows.Skip(page * DefinitionPageSize).Take(DefinitionPageSize).ToList();
            foreach (var row in visible)
            {
                var entity = CrosswordService.FindWord(project, row.Word);
                if (entity is null) continue;
                var grid = DefinitionRowGrid();
                var values = new[] { row.Word, row.Definition1, row.Definition2, row.Definition3, row.Definition4, row.Approved, row.Notes };
                for (var column = 0; column < values.Length; column++)
                {
                    var box = new TextBox
                    {
                        Text = values[column],
                        IsReadOnly = column == 0,
                        MinHeight = 34,
                        Margin = new Thickness(2),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    };
                    Grid.SetColumn(box, column);
                    if (column > 0)
                    {
                        var captured = column;
                        box.LostFocus += async (_, _) => await SaveCell(entity.EntityId, captured, box.Text);
                    }
                    grid.Children.Add(box);
                }
                body.Children.Add(grid);
            }
            pageLabel.Text = $"Pagina {page + 1}/{pages} · {rows.Count:N0} parole · {CrosswordService.MissingDefinitions(project):N0} senza definizioni";
            status.Text = "Le celle sono modificabili e copiabili. La colonna APPROVATA è tua: le alternative restano conservate.";
        }

        var previous = new Button { Content = "←", Width = 44 };
        previous.Click += (_, _) => { if (page > 0) { page--; Refresh(); } };
        var next = new Button { Content = "→", Width = 44 };
        next.Click += (_, _) => { page++; Refresh(); };
        var find = new Button { Content = "Cerca", Width = 80 };
        find.Click += (_, _) => { page = 0; Refresh(); };
        var refresh = new Button { Content = "Aggiorna", Width = 90 };
        refresh.Click += (_, _) => Refresh();
        var importXlsx = new Button { Content = "Importa XLSX definizioni…", MinWidth = 175 };
        importXlsx.Click += async (_, _) =>
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importa definizioni restituite dall'AI",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Foglio XLSX") { Patterns = ["*.xlsx"] }]
            });
            var file = files.FirstOrDefault();
            if (file is null) return;
            try
            {
                var result = await CrosswordService.ImportDefinitionsXlsxAsync(project, file.Path.LocalPath);
                await SaveCurrentAsync(window, project);
                page = 0;
                Refresh();
                status.Text = $"Importate {result.DefinitionsImported:N0} definizioni da {result.Rows:N0} righe. Nuove parole aggiunte: {result.WordsCreated:N0}.";
            }
            catch (Exception ex) { status.Text = "Importazione non riuscita: " + ex.Message; }
        };

        Refresh();
        var sheet = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 6,
            Margin = new Thickness(6),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { search, find, previous, next, pageLabel, refresh, importXlsx } },
                status.WithGridRow(1),
                header.WithGridRow(2),
                new ScrollViewer
                {
                    Content = body,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                }.WithGridRow(3)
            }
        };
        return sheet;
    }

    private static Control BuildBookType(MainWindow window, PreviewProject project)
    {
        var gridKnown = string.Equals(CrosswordService.GetSetting(project, "GridKnown"), "yes", StringComparison.OrdinalIgnoreCase);
        var structure = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
        structure.Children.Add(new TextBlock { Text = "Conosci già dimensioni e struttura della griglia?", FontSize = 19, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var yes = new RadioButton { Content = "Sì", GroupName = "crossword-grid-known", IsChecked = gridKnown };
        var no = new RadioButton { Content = "No, proponile in base al progetto", GroupName = "crossword-grid-known", IsChecked = !gridKnown };
        var numbers = new StackPanel { Spacing = 6, IsVisible = gridKnown };
        var rows = SettingBox(project, "GridRows", "Righe", "15", window);
        var columns = SettingBox(project, "GridColumns", "Colonne", "15", window);
        numbers.Children.Add(rows); numbers.Children.Add(columns);
        var autoText = new TextBlock
        {
            Text = "Diez userà tema, vocabolario e parole obbligatorie/preferite per proporti dimensioni e struttura, senza chiederti numeri prima del tempo.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            IsVisible = !gridKnown
        };
        void ApplyChoice(bool known)
        {
            CrosswordService.SetSetting(project, "GridKnown", known ? "yes" : "no");
            numbers.IsVisible = known; autoText.IsVisible = !known;
            _ = SaveCurrentAsync(window, project);
        }
        yes.IsCheckedChanged += (_, _) => { if (yes.IsChecked == true) ApplyChoice(true); };
        no.IsCheckedChanged += (_, _) => { if (no.IsChecked == true) ApplyChoice(false); };
        structure.Children.Add(yes); structure.Children.Add(no); structure.Children.Add(numbers); structure.Children.Add(autoText);

        var language = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
        language.Children.Add(SettingBox(project, "PrimaryLanguage", "Lingua principale", "Italiano", window));
        var openness = new ComboBox
        {
            ItemsSource = new[] { "Solo lingua principale", "Abbastanza aperto", "Molto aperto" },
            SelectedItem = CrosswordService.GetSetting(project, "InternationalOpenness", "Abbastanza aperto"),
            MinWidth = 220
        };
        if (openness.SelectedIndex < 0) openness.SelectedIndex = 1;
        openness.SelectionChanged += async (_, _) =>
        {
            CrosswordService.SetSetting(project, "InternationalOpenness", openness.SelectedItem?.ToString());
            await SaveCurrentAsync(window, project);
        };
        language.Children.Add(Field("Quanto può essere internazionale il vocabolario?", openness));
        language.Children.Add(new TextBlock
        {
            Text = "Le parole straniere possono restare nel listone: Diez le userà come riserva quando aiutano a chiudere gli incroci.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var theme = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
        var themeMode = new ComboBox { ItemsSource = new[] { "Generico", "Tematico" }, MinWidth = 180 };
        themeMode.SelectedItem = CrosswordService.GetSetting(project, "ThemeMode", "Generico");
        if (themeMode.SelectedIndex < 0) themeMode.SelectedIndex = 0;
        var themeBox = new TextBox { Text = CrosswordService.GetSetting(project, "Theme"), MinWidth = 360, Watermark = "Es. Cinema italiano, Astronomia, Anni '80" };
        var themeField = Field("Tema", themeBox);
        themeField.IsVisible = string.Equals(themeMode.SelectedItem?.ToString(), "Tematico", StringComparison.Ordinal);
        themeMode.SelectionChanged += async (_, _) =>
        {
            var mode = themeMode.SelectedItem?.ToString() ?? "Generico";
            CrosswordService.SetSetting(project, "ThemeMode", mode);
            themeField.IsVisible = mode == "Tematico";
            if (mode != "Tematico") CrosswordService.SetSetting(project, "Theme", "Generico");
            await SaveCurrentAsync(window, project);
        };
        themeBox.LostFocus += async (_, _) =>
        {
            CrosswordService.SetSetting(project, "Theme", themeBox.Text);
            await SaveCurrentAsync(window, project);
        };
        theme.Children.Add(Field("Tipo", themeMode)); theme.Children.Add(themeField);
        theme.Children.Add(new TextBlock { Text = "Nel prossimo passaggio le parole del tema potranno essere marcate obbligatorie o preferite; il vocabolario generale resterà disponibile per chiudere la griglia.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });

        return SubTabs
        ([
            new TabItem { Header = "Griglia", Content = new ScrollViewer { Content = structure } },
            new TabItem { Header = "Lingua e regole", Content = new ScrollViewer { Content = language } },
            new TabItem { Header = "Tema", Content = new ScrollViewer { Content = theme } },
            new TabItem { Header = "Qualità parole", Content = MessagePanel("Qualità parole", "Qui Diez distinguerà parole preferite, normali, di soccorso ed escluse senza eliminarle dal database lessicale.") },
            new TabItem { Header = "Definizioni", Content = MessagePanel("Definizioni", "Le definizioni vengono proposte dall'AI tramite XLSX o API, poi restano modificabili e approvabili nel Database.") }
        ]);
    }

    private static Control BuildChecks(PreviewProject project)
    {
        var total = CrosswordService.Words(project).Count;
        var missing = CrosswordService.MissingDefinitions(project);
        return SubTabs
        ([
            new TabItem { Header = "Incroci", Content = MessagePanel("Incroci", "Quando importeremo o costruiremo la griglia, qui Diez controllerà gli incroci e cercherà sostituzioni con il minimo numero di voci coinvolte.") },
            new TabItem { Header = "Parole deboli", Content = MessagePanel("Parole deboli", "Qui verranno evidenziate sigle, termini rari e parole di soccorso usate nella griglia, senza considerare le parole straniere un errore.") },
            new TabItem { Header = "Duplicati", Content = MessagePanel("Duplicati", $"Vocabolario corrente: {total:N0} forme uniche per la griglia. I duplicati di grafia vengono unificati all'importazione.") },
            new TabItem { Header = "Definizioni mancanti", Content = MessagePanel("Definizioni mancanti", $"{missing:N0} parole su {total:N0} non hanno ancora una definizione proposta.") },
            new TabItem { Header = "Lingua", Content = MessagePanel("Lingua", "La lingua principale guida definizioni e qualità editoriale; le altre lingue possono restare disponibili come riserva per gli incroci.") }
        ]);
    }

    private static Control BuildAi(MainWindow window, PreviewProject project)
    {
        var prompt = new TextBox
        {
            Text = CrosswordService.BuildDefinitionPrompt(project),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 250
        };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var createXlsx = new Button { Content = "Crea XLSX per l'AI…", MinWidth = 170 };
        createXlsx.Click += async (_, _) =>
        {
            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Crea elenco parole per le definizioni AI",
                SuggestedFileName = "definizioni_cruciverba.xlsx",
                DefaultExtension = "xlsx",
                FileTypeChoices = [new FilePickerFileType("Foglio XLSX") { Patterns = ["*.xlsx"] }]
            });
            if (file is null) return;
            await CrosswordService.WriteDefinitionTemplateXlsxAsync(project, file.Path.LocalPath);
            prompt.Text = CrosswordService.BuildDefinitionPrompt(project);
            status.Text = $"XLSX creato con {CrosswordService.Words(project).Count:N0} parole. Copia il prompt qui sopra, allega il file all'AI e chiedile di restituirti lo stesso XLSX compilato.";
        };
        var importXlsx = new Button { Content = "Importa XLSX restituito…", MinWidth = 175 };
        importXlsx.Click += async (_, _) =>
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importa XLSX restituito dall'AI",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Foglio XLSX") { Patterns = ["*.xlsx"] }]
            });
            var file = files.FirstOrDefault();
            if (file is null) return;
            try
            {
                var result = await CrosswordService.ImportDefinitionsXlsxAsync(project, file.Path.LocalPath);
                await SaveCurrentAsync(window, project);
                status.Text = $"Importate {result.DefinitionsImported:N0} definizioni per {result.Rows:N0} parole. Le trovi in Database → Definizioni.";
            }
            catch (Exception ex) { status.Text = "Importazione non riuscita: " + ex.Message; }
        };
        var create = new StackPanel
        {
            Margin = new Thickness(10), Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Prompt Pack definizioni", FontSize = 19 },
                new TextBlock { Text = "Il prompt è copiabile. L'XLSX contiene le parole; l'AI deve restituire lo stesso foglio con le definizioni possibili.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                prompt,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { createXlsx, importXlsx } },
                status
            }
        };
        return SubTabs
        ([
            new TabItem { Header = "Crea definizioni", Content = new ScrollViewer { Content = create } },
            new TabItem { Header = "Migliora", Content = MessagePanel("Migliora", "Qui potrai chiedere nuove alternative solo per le definizioni selezionate, senza perdere quelle già approvate.") },
            new TabItem { Header = "Classifica parole", Content = MessagePanel("Classifica parole", "Qui l'AI potrà aiutare a classificare parole comuni, forestierismi, termini tecnici e parole di soccorso; la decisione resta nel database Diez.") },
            new TabItem { Header = "Provider e modalità", Content = MessagePanel("Provider e modalità", "Prompt pack, API o Chiedi ogni volta: il flusso delle definizioni resta identico qualunque sia il provider.") }
        ]);
    }

    private static Control BuildExport(MainWindow window, PreviewProject project)
    {
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var qxw = new Button { Content = "Esporta TXT per Qxw…", MinWidth = 180 };
        qxw.Click += async (_, _) =>
        {
            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Esporta lista parole per Qxw",
                SuggestedFileName = "dizionario_qxw.txt",
                DefaultExtension = "txt",
                FileTypeChoices = [new FilePickerFileType("Testo UTF-8") { Patterns = ["*.txt"] }]
            });
            if (file is null) return;
            await CrosswordService.ExportQxwTextAsync(project, file.Path.LocalPath);
            status.Text = $"Creato TXT UTF-8: {CrosswordService.Words(project).Count:N0} parole, una per riga, ordinate e senza duplicati.";
        };
        var databaseXlsx = new Button { Content = "Esporta definizioni XLSX…", MinWidth = 190 };
        databaseXlsx.Click += async (_, _) =>
        {
            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Esporta definizioni del cruciverba",
                SuggestedFileName = "definizioni_cruciverba.xlsx",
                DefaultExtension = "xlsx",
                FileTypeChoices = [new FilePickerFileType("Foglio XLSX") { Patterns = ["*.xlsx"] }]
            });
            if (file is null) return;
            await CrosswordService.WriteDefinitionWorkbookAsync(file.Path.LocalPath, CrosswordService.DefinitionRows(project));
            status.Text = "XLSX definizioni esportato.";
        };
        return SubTabs
        ([
            new TabItem { Header = "Qxw", Content = new StackPanel { Margin = new Thickness(12), Spacing = 8, Children = { qxw, status } } },
            new TabItem { Header = "Database", Content = new StackPanel { Margin = new Thickness(12), Spacing = 8, Children = { databaseXlsx, new TextBlock { Text = "Il database Diez conserva anche la definizione approvata; il foglio resta un formato modificabile di scambio.", TextWrapping = Avalonia.Media.TextWrapping.Wrap } } } },
            new TabItem { Header = "Output", Content = MessagePanel("Output", "Quando la griglia e le definizioni saranno approvate, qui verranno preparati gli output editoriali del cruciverba.") }
        ]);
    }

    private static Grid DefinitionRowGrid() => new()
    {
        ColumnDefinitions = new ColumnDefinitions("150,260,260,260,260,260,260"),
        ColumnSpacing = 2,
        MinWidth = 1720
    };

    private static StackPanel SettingBox(PreviewProject project, string key, string label, string defaultValue, MainWindow window)
    {
        var box = new TextBox { Text = CrosswordService.GetSetting(project, key, defaultValue), MinWidth = 220 };
        box.LostFocus += async (_, _) =>
        {
            CrosswordService.SetSetting(project, key, box.Text);
            await SaveCurrentAsync(window, project);
        };
        return Field(label, box);
    }

    private static StackPanel Field(string label, Control control) => new()
    {
        Spacing = 3,
        Children = { new TextBlock { Text = label }, control }
    };

    private static TabControl SubTabs(IReadOnlyList<TabItem> items) => new()
    {
        ItemsSource = items,
        SelectedIndex = 0,
        Margin = new Thickness(4)
    };

    private static StackPanel MessagePanel(string title, string text) => new()
    {
        Margin = new Thickness(12), Spacing = 8,
        Children =
        {
            new TextBlock { Text = title, FontSize = 19 },
            new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
        }
    };

    private static string? CurrentPath(MainWindow window) =>
        typeof(MainWindow).GetField("_currentProjectPath", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as string;

    private static async Task SaveCurrentAsync(MainWindow window, PreviewProject project)
    {
        var path = CurrentPath(window);
        if (string.IsNullOrWhiteSpace(path)) return;
        await ProjectFileStore.SaveAsync(path, project);
    }
}
