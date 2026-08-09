using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace DiezPublishingStudio;

internal static class BookWorkspaceTabsUi
{
    public static void Attach(MainWindow window)
    {
        if (window.Content is not Border border || border.Child is not Grid desktop || desktop.RowDefinitions.Count < 4)
            return;

        var header = desktop.Children.FirstOrDefault(c => Grid.GetRow(c) == 0);
        var genericWorkspace = desktop.Children.FirstOrDefault(c => Grid.GetRow(c) == 1);
        var detail = desktop.Children.FirstOrDefault(c => Grid.GetRow(c) == 2);
        var help = desktop.Children.FirstOrDefault(c => Grid.GetRow(c) == 3);
        if (header is null || genericWorkspace is null || detail is null || help is null) return;

        desktop.Children.Remove(genericWorkspace);
        desktop.Children.Remove(detail);
        desktop.Children.Remove(help);

        var projectTabContent = new Grid
        {
            RowDefinitions = new RowDefinitions("*,178"),
            RowSpacing = 8,
            Children =
            {
                genericWorkspace,
                detail.WithGridRow(1)
            }
        };

        var tabs = new TabControl();
        var projectTab = new TabItem { Header = "Progetto", Content = projectTabContent };
        var wordSearch = new WordSearchTabWorkspace(window);
        var specializedTabs = wordSearch.CreateTabs().ToList();
        var items = new List<TabItem> { projectTab };
        items.AddRange(specializedTabs);
        tabs.ItemsSource = items;
        tabs.SelectedIndex = 0;

        desktop.RowDefinitions = new RowDefinitions("Auto,*,Auto");
        desktop.RowSpacing = 8;
        desktop.Children.Add(tabs.WithGridRow(1));
        desktop.Children.Add(help.WithGridRow(2));

        async Task RefreshAsync()
        {
            var project = TryGetProject(window);
            var isWordSearch = project is not null && BookTypeRecognition.IsWordSearch(project);
            foreach (var tab in specializedTabs) tab.IsVisible = isWordSearch;
            if (!isWordSearch && tabs.SelectedItem is TabItem selected && specializedTabs.Contains(selected))
                tabs.SelectedIndex = 0;
            if (isWordSearch) await wordSearch.RefreshAsync(collectIfNeeded: true);
        }

        window.Opened += async (_, _) => await RefreshAsync();
        window.Activated += async (_, _) => await RefreshAsync();
        tabs.AttachedToVisualTree += async (_, _) => await RefreshAsync();
        tabs.SelectionChanged += async (_, _) =>
        {
            if (specializedTabs.Any(t => ReferenceEquals(tabs.SelectedItem, t)))
                await wordSearch.RefreshAsync(collectIfNeeded: true);
        };

        _ = RefreshAsync();
    }

    private static PreviewProject? TryGetProject(MainWindow window) =>
        typeof(MainWindow).GetField("_project", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as PreviewProject;
}

internal static class BookTypeRecognition
{
    public static bool IsWordSearch(PreviewProject project)
    {
        if (WordSearchWorkspaceService.HasWordSearchDatabase(project)) return true;
        var combined = (project.Name + " " + project.EditionMetadata?.Title).ToLowerInvariant();
        if (combined.Contains("word search") || combined.Contains("wordsearch") || combined.Contains("cerca parole")) return true;

        if (project.Materials.Any(m =>
                m.FileName.Contains("wordsearch", StringComparison.OrdinalIgnoreCase) ||
                m.FileName.Contains("word_search", StringComparison.OrdinalIgnoreCase) ||
                m.Columns.Any(c => c.Contains("puzzle", StringComparison.OrdinalIgnoreCase) || c.Contains("parola", StringComparison.OrdinalIgnoreCase))))
            return true;

        return project.AiProductionJobs.Any(j =>
            string.Equals(j.OutputType, AiProductionService.TypeData, StringComparison.OrdinalIgnoreCase) &&
            (j.ResultText.Contains("Puzzle", StringComparison.OrdinalIgnoreCase) || j.Request.Contains("word search", StringComparison.OrdinalIgnoreCase)));
    }
}

internal sealed class WordSearchTabWorkspace
{
    private readonly MainWindow _window;
    private readonly ListBox _databaseList = new();
    private readonly TextBox _search = new() { Watermark = "Cerca ID, titolo, tema o parola...", Width = 330 };
    private readonly ComboBox _filter = new()
    {
        ItemsSource = new[] { "Tutti", "Da controllare", "Approvati", "Da rifare", "Con problemi" },
        SelectedIndex = 0,
        Width = 160
    };
    private readonly TextBlock _databaseSummary = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _selectedInfo = new() { FontSize = 18, Text = "Seleziona un puzzle nel tab Database." };
    private readonly TextBox _order = new() { Width = 75 };
    private readonly TextBox _id = new() { Width = 120 };
    private readonly TextBox _expected = new() { Width = 90 };
    private readonly TextBox _title = new();
    private readonly TextBox _theme = new();
    private readonly TextBox _words = new()
    {
        AcceptsReturn = true,
        Height = 260,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
        Watermark = "Una parola o espressione per riga"
    };
    private readonly ComboBox _statusChoice = new()
    {
        ItemsSource = new[] { WordSearchWorkspaceService.StatusToReview, WordSearchWorkspaceService.StatusApproved, WordSearchWorkspaceService.StatusNeedsRevision },
        SelectedIndex = 0,
        Width = 150
    };
    private readonly TextBox _notes = new() { AcceptsReturn = true, Height = 72, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _checks = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ListBox _allChecks = new();
    private readonly TextBlock _aiInfo = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _exportInfo = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private List<WordSearchRecord> _displayed = [];
    private WordSearchRecord? _selected;
    private bool _loading;
    private bool _collecting;

    public WordSearchTabWorkspace(MainWindow window)
    {
        _window = window;
        _databaseList.SelectionChanged += (_, _) => LoadSelected();
        _search.TextChanged += (_, _) => RefreshLists();
        _filter.SelectionChanged += (_, _) => RefreshLists();
    }

    public IEnumerable<TabItem> CreateTabs()
    {
        yield return new TabItem { Header = "Database", Content = BuildDatabaseTab() };
        yield return new TabItem { Header = "Puzzle", Content = BuildPuzzleTab() };
        yield return new TabItem { Header = "Controlli", Content = BuildChecksTab() };
        yield return new TabItem { Header = "AI", Content = BuildAiTab() };
        yield return new TabItem { Header = "Esporta", Content = BuildExportTab() };
    }

    public async Task RefreshAsync(bool collectIfNeeded)
    {
        if (!TrySession(out var project, out var path))
        {
            _displayed = [];
            _databaseList.ItemsSource = null;
            _databaseSummary.Text = "Apri prima un progetto Word Search.";
            return;
        }

        if (collectIfNeeded && !_collecting && WordSearchWorkspaceService.GetRecords(project).Count == 0)
        {
            _collecting = true;
            try
            {
                var result = await WordSearchWorkspaceService.CollectFromProjectAsync(project, path);
                if (result.Recognized) await ProjectFileStore.SaveAsync(path, project);
            }
            catch { }
            finally { _collecting = false; }
        }
        RefreshLists(_selected?.ContentId);
    }

    private Control BuildDatabaseTab()
    {
        var import = Button("Importa / aggiorna XLSX", 185);
        var collect = Button("Raccogli tutto", 135);
        var add = Button("Nuovo puzzle", 130);
        import.Click += async (_, _) => await ImportAsync();
        collect.Click += async (_, _) => await CollectAsync();
        add.Click += async (_, _) => await AddAsync();

        var top = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { import, collect, add, _search, _filter }
        };

        return new Grid
        {
            Margin = new Thickness(8),
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 8,
            Children =
            {
                top,
                _databaseSummary.WithGridRow(1),
                _databaseList.WithGridRow(2)
            }
        };
    }

    private Control BuildPuzzleTab()
    {
        var save = Button("Salva modifiche", 140);
        var normalize = Button("Normalizza parole", 145);
        var dedupe = Button("Togli doppioni", 125);
        var approve = Button("Approva", 100);
        var redo = Button("Da rifare", 100);
        save.Click += async (_, _) => await SaveSelectedAsync();
        normalize.Click += async (_, _) => await NormalizeAsync(false);
        dedupe.Click += async (_, _) => await NormalizeAsync(true);
        approve.Click += async (_, _) => await SetStatusAsync(WordSearchWorkspaceService.StatusApproved);
        redo.Click += async (_, _) => await SetStatusAsync(WordSearchWorkspaceService.StatusNeedsRevision);

        var identity = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                Field("Ordine", _order), Field("ID", _id), Field("Parole previste", _expected), Field("Stato", _statusChoice)
            }
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children = { save, normalize, dedupe, approve, redo }
        };

        return new ScrollViewer
        {
            Margin = new Thickness(8),
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    _selectedInfo,
                    identity,
                    Field("Titolo", _title),
                    Field("Tema", _theme),
                    new TextBlock { Text = "Parole" },
                    _words,
                    Field("Note", _notes),
                    new TextBlock { Text = "Controllo del puzzle", FontSize = 17 },
                    _checks,
                    actions
                }
            }
        };
    }

    private Control BuildChecksTab()
    {
        var refresh = Button("Aggiorna controlli", 155);
        refresh.Click += (_, _) => RefreshLists(_selected?.ContentId);
        return new Grid
        {
            Margin = new Thickness(8),
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 8,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Problemi e incompletezze del Word Search", FontSize = 20, VerticalAlignment = VerticalAlignment.Center },
                        refresh
                    }
                },
                _allChecks.WithGridRow(1)
            }
        };
    }

    private Control BuildAiTab()
    {
        var correction = Button("Prepara correzione del puzzle", 220);
        correction.Click += async (_, _) => await PrepareAiCorrectionAsync();
        return new StackPanel
        {
            Margin = new Thickness(8),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "AI per questo Word Search", FontSize = 20 },
                new TextBlock
                {
                    Text = "L'AI può proporre temi, liste e correzioni. La griglia del puzzle resta un compito deterministico di Diez. Una proposta AI non modifica mai da sola il puzzle.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                correction,
                _aiInfo
            }
        };
    }

    private Control BuildExportTab()
    {
        var database = Button("Esporta database", 170);
        var xlsx = Button("Esporta XLSX", 150);
        var csv = Button("Esporta CSV", 150);
        database.Click += async (_, _) => await ExportDatabaseAsync();
        xlsx.Click += async (_, _) => await ExportColumnsXlsxAsync();
        csv.Click += async (_, _) => await ExportColumnsCsvAsync();

        return new StackPanel
        {
            Margin = new Thickness(8),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Esporta il Word Search", FontSize = 20 },
                new TextBlock
                {
                    Text = "Esporta database crea la copia completa e reimportabile. XLSX e CSV creano invece l'output pulito: Puzzle 1, Puzzle 2 ... Puzzle N in colonne e sotto soltanto le parole previste.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { database, xlsx, csv } },
                _exportInfo
            }
        };
    }

    private async Task ImportAsync()
    {
        if (!TrySession(out var project, out var projectPath)) return;
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importa o aggiorna Word Search",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("File Excel XLSX") { Patterns = ["*.xlsx"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;
        try
        {
            var path = file.Path.LocalPath;
            var material = await MaterialImporter.ImportAsync(path);
            var existing = project.Materials.FirstOrDefault(m => string.Equals(m.Sha256, material.Sha256, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                project.Materials.Add(material);
                existing = material;
            }

            var rich = await WordSearchDatabaseService.ImportDatabaseAsync(project, path, existing.MaterialId, replaceExisting: true);
            var result = rich.Recognized
                ? rich
                : await WordSearchWorkspaceService.ImportXlsxFileAsync(project, path, existing.MaterialId, replaceExisting: true);
            if (result.Recognized) await ProjectFileStore.SaveAsync(projectPath, project);
            SetStatus(result.Message);
            RefreshLists(_selected?.ContentId);
        }
        catch (Exception ex) { SetStatus("Non riesco a importare il Word Search: " + ex.Message); }
    }

    private async Task CollectAsync()
    {
        if (!TrySession(out var project, out var path)) return;
        try
        {
            var result = await WordSearchWorkspaceService.CollectFromProjectAsync(project, path);
            if (result.Recognized) await ProjectFileStore.SaveAsync(path, project);
            SetStatus(result.Message);
            RefreshLists(_selected?.ContentId);
        }
        catch (Exception ex) { SetStatus("Non riesco a raccogliere i dati: " + ex.Message); }
    }

    private async Task AddAsync()
    {
        if (!TrySession(out var project, out var path)) return;
        var existing = WordSearchWorkspaceService.GetRecords(project);
        var defaultExpected = existing.Count == 0
            ? 20
            : existing.Select(r => WordSearchDatabaseService.ExpectedWordCount(project, r))
                .GroupBy(n => n).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        var record = WordSearchWorkspaceService.AddNew(project);
        WordSearchDatabaseService.SetExpectedWordCount(project, record.Id, defaultExpected);
        await ProjectFileStore.SaveAsync(path, project);
        RefreshLists(record.ContentId);
        SetStatus($"Creato {record.Id}: {defaultExpected} parole previste. Gli altri puzzle non sono stati modificati.");
    }

    private async Task SaveSelectedAsync()
    {
        if (_selected is null || !TrySession(out var project, out var path)) return;
        _ = int.TryParse((_order.Text ?? string.Empty).Trim(), out var order);
        _ = int.TryParse((_expected.Text ?? string.Empty).Trim(), out var expected);
        if (expected <= 0) expected = Math.Max(1, _selected.Words.Count);
        _selected.Order = order;
        _selected.Id = _id.Text ?? _selected.Id;
        _selected.Title = _title.Text ?? string.Empty;
        _selected.Theme = _theme.Text ?? string.Empty;
        _selected.Words = Lines(_words.Text);
        _selected.Status = _statusChoice.SelectedItem?.ToString() ?? WordSearchWorkspaceService.StatusToReview;
        _selected.Notes = _notes.Text ?? string.Empty;
        if (!_selected.Origin.Contains("modificat", StringComparison.OrdinalIgnoreCase))
            _selected.Origin = string.IsNullOrWhiteSpace(_selected.Origin) ? "Modificato in Diez" : _selected.Origin + " · modificato";
        WordSearchWorkspaceService.SaveRecord(project, _selected);
        WordSearchDatabaseService.SetExpectedWordCount(project, _selected.Id, expected);
        await ProjectFileStore.SaveAsync(path, project);
        RefreshLists(_selected.ContentId);
        SetStatus($"{_selected.Id} salvato: {_selected.Words.Count}/{expected} parole. Nessun altro puzzle è stato modificato.");
    }

    private async Task NormalizeAsync(bool removeDuplicates)
    {
        if (_selected is null || !TrySession(out var project, out var path)) return;
        _selected.Words = Lines(_words.Text);
        WordSearchWorkspaceService.NormalizeSelectedWords(project, _selected, removeDuplicates);
        await ProjectFileStore.SaveAsync(path, project);
        RefreshLists(_selected.ContentId);
        SetStatus(removeDuplicates ? "Doppioni rimossi soltanto dal puzzle selezionato." : "Parole normalizzate soltanto nel puzzle selezionato.");
    }

    private async Task SetStatusAsync(string value)
    {
        if (_selected is null || !TrySession(out var project, out var path)) return;
        _selected.Status = value;
        WordSearchWorkspaceService.SaveRecord(project, _selected);
        await ProjectFileStore.SaveAsync(path, project);
        RefreshLists(_selected.ContentId);
        SetStatus($"{_selected.Id}: {value}.");
    }

    private async Task PrepareAiCorrectionAsync()
    {
        if (_selected is null || !TrySession(out var project, out var path))
        {
            _aiInfo.Text = "Seleziona prima un puzzle nel tab Database.";
            return;
        }
        var expected = WordSearchDatabaseService.ExpectedWordCount(project, _selected);
        var issue = WordSearchWorkspaceChecks.Analyze(project, _selected);
        var request = $"Correggi esclusivamente il puzzle {_selected.Id}. Mantieni esattamente questo ID e restituisci {expected} parole. Non modificare altri puzzle.\n" +
                      $"Titolo: {_selected.Title}\nTema: {_selected.Theme}\nParole attuali: {string.Join(" | ", _selected.Words)}\n" +
                      $"Problemi: {string.Join(" ", issue.Messages)}\n\n" +
                      $"Restituisci ID, titolo, tema e precisamente {expected} parole. Mantieni l'ID {_selected.Id}.";
        var task = AiProductionService.CreateJob(project, AiProductionService.TypeData, $"Correzione {_selected.Id}", request, _selected.ContentId);
        await ProjectFileStore.SaveAsync(path, project);
        var clipboard = TopLevel.GetTopLevel(_window)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(task.Prompt);
        _aiInfo.Text = $"Correzione preparata per {_selected.Id}" + (clipboard is null ? "." : " e copiata negli appunti.") + " Il puzzle non è stato modificato.";
    }

    private async Task ExportDatabaseAsync()
    {
        if (!TrySession(out var project, out _)) return;
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta database Word Search",
            SuggestedFileName = WordSearchExportService.SuggestedDatabaseName(project),
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("Database Word Search XLSX") { Patterns = ["*.xlsx"] }]
        });
        if (file is null) return;
        var result = await WordSearchExportService.ExportDatabaseAsync(project, file.Path.LocalPath);
        _exportInfo.Text = result.Message;
        SetStatus(result.Message);
    }

    private async Task ExportColumnsXlsxAsync()
    {
        if (!TrySession(out var project, out _)) return;
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta puzzle in XLSX",
            SuggestedFileName = WordSearchColumnExportService.SuggestedXlsxName(project),
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("Excel XLSX") { Patterns = ["*.xlsx"] }]
        });
        if (file is null) return;
        var result = await WordSearchColumnExportService.ExportXlsxAsync(project, file.Path.LocalPath);
        _exportInfo.Text = result.Message;
        SetStatus(result.Message);
    }

    private async Task ExportColumnsCsvAsync()
    {
        if (!TrySession(out var project, out _)) return;
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta puzzle in CSV",
            SuggestedFileName = WordSearchColumnExportService.SuggestedCsvName(project),
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        });
        if (file is null) return;
        var result = await WordSearchColumnExportService.ExportCsvAsync(project, file.Path.LocalPath);
        _exportInfo.Text = result.Message;
        SetStatus(result.Message);
    }

    private void RefreshLists(Guid? selectContentId = null)
    {
        if (!TrySession(out var project, out _)) return;
        var all = WordSearchWorkspaceService.GetRecords(project);
        var query = (_search.Text ?? string.Empty).Trim();
        var filter = _filter.SelectedItem?.ToString() ?? "Tutti";
        IEnumerable<WordSearchRecord> selected = all;
        if (query.Length > 0)
            selected = selected.Where(r => r.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                           r.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                           r.Theme.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                           r.Words.Any(w => w.Contains(query, StringComparison.OrdinalIgnoreCase)));

        selected = filter switch
        {
            "Da controllare" => selected.Where(r => r.Status == WordSearchWorkspaceService.StatusToReview),
            "Approvati" => selected.Where(r => r.Status == WordSearchWorkspaceService.StatusApproved),
            "Da rifare" => selected.Where(r => r.Status == WordSearchWorkspaceService.StatusNeedsRevision),
            "Con problemi" => selected.Where(r => HasProblems(project, r)),
            _ => selected
        };

        _displayed = selected.ToList();
        _loading = true;
        _databaseList.ItemsSource = _displayed.Select(r => DisplayRow(project, r)).ToList();
        _databaseSummary.Text = $"{all.Count} puzzle totali · {_displayed.Count} visualizzati · {all.Count(r => r.Status == WordSearchWorkspaceService.StatusApproved)} approvati · {all.Count(r => HasProblems(project, r))} con problemi";
        var target = selectContentId ?? _selected?.ContentId;
        var index = target.HasValue ? _displayed.FindIndex(r => r.ContentId == target.Value) : -1;
        _databaseList.SelectedIndex = index >= 0 ? index : (_displayed.Count > 0 ? 0 : -1);
        _loading = false;
        LoadSelected();

        _allChecks.ItemsSource = all
            .Select(r =>
            {
                var expected = WordSearchDatabaseService.ExpectedWordCount(project, r);
                var issues = WordSearchWorkspaceChecks.Analyze(project, r);
                var prefix = HasProblems(project, r) ? "⚠" : "✓";
                return $"{prefix} {r.Id} · {r.Words.Count}/{expected} parole · {string.Join(" ", issues.Messages)}";
            })
            .ToList();
    }

    private void LoadSelected()
    {
        if (_loading || !TrySession(out var project, out _)) return;
        if (_databaseList.SelectedIndex < 0 || _databaseList.SelectedIndex >= _displayed.Count)
        {
            _selected = null;
            _selectedInfo.Text = "Nessun puzzle selezionato.";
            _checks.Text = string.Empty;
            return;
        }
        _selected = _displayed[_databaseList.SelectedIndex];
        var expected = WordSearchDatabaseService.ExpectedWordCount(project, _selected);
        _loading = true;
        _order.Text = _selected.Order.ToString();
        _id.Text = _selected.Id;
        _expected.Text = expected.ToString();
        _title.Text = _selected.Title;
        _theme.Text = _selected.Theme;
        _words.Text = string.Join(Environment.NewLine, _selected.Words);
        _statusChoice.SelectedItem = _selected.Status;
        _notes.Text = _selected.Notes;
        _selectedInfo.Text = $"{_selected.Id} — {_selected.Title} · {_selected.Words.Count}/{expected} parole · origine: {_selected.Origin}";
        _checks.Text = string.Join(Environment.NewLine, WordSearchWorkspaceChecks.Analyze(project, _selected).Messages.Select(m => "• " + m));
        _aiInfo.Text = $"Puzzle selezionato: {_selected.Id}. Puoi preparare una correzione limitata a questo puzzle.";
        _loading = false;
    }

    private static string DisplayRow(PreviewProject project, WordSearchRecord record)
    {
        var expected = WordSearchDatabaseService.ExpectedWordCount(project, record);
        var problem = HasProblems(project, record) ? "⚠" : "✓";
        return $"{record.Order:D3}   {record.Id}   {problem}   {record.Title}   |   {record.Theme}   |   {record.Words.Count}/{expected} parole   |   {record.Status}";
    }

    private static bool HasProblems(PreviewProject project, WordSearchRecord record)
    {
        var expected = WordSearchDatabaseService.ExpectedWordCount(project, record);
        var summary = WordSearchWorkspaceChecks.Analyze(project, record);
        return record.Words.Count != expected || summary.DuplicateWordsInside > 0 || summary.MissingTitle || summary.MissingTheme;
    }

    private bool TrySession(out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(_window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(_window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private void SetStatus(string message)
    {
        var status = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_window) as TextBlock;
        if (status is not null) status.Text = message;
    }

    private static List<string> Lines(string? text) => (text ?? string.Empty)
        .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static StackPanel Field(string label, Control control) => new()
    {
        Spacing = 3,
        Children = { new TextBlock { Text = label }, control }
    };

    private static Button Button(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };
}
