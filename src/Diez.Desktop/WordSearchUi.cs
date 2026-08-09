using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

internal static class WordSearchUi
{
    public static void Attach(MainWindow window)
    {
        if (window.Content is not Border border || border.Child is not StackPanel root) return;
        var projectButtons = root.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.Ordinal)));
        if (projectButtons is null) return;
        if (projectButtons.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Word Search", StringComparison.Ordinal))) return;

        var button = new Button
        {
            Content = "Word Search",
            Width = 135,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ToolTip.SetTip(button, "Apre l'intero database dei puzzle come elenco: ricerca, filtri, parole, correzioni e XLSX completo reimportabile.");
        button.Click += async (_, _) =>
        {
            if (!TryGetSession(window, out var project, out var path))
            {
                SetStatus(window, "Prima crea o apri un progetto Diez.");
                return;
            }
            var dialog = new WordSearchWorkspaceWindow(project, path, message => SetStatus(window, message));
            await dialog.ShowDialog(window);
        };
        projectButtons.Children.Add(button);
    }

    private static bool TryGetSession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private static void SetStatus(MainWindow window, string message)
    {
        var status = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as TextBlock;
        if (status is not null) status.Text = message;
    }
}

internal sealed class WordSearchWorkspaceWindow : Window
{
    private readonly PreviewProject _project;
    private readonly string _projectPath;
    private readonly Action<string> _mainStatus;
    private readonly TextBox _search;
    private readonly ComboBox _filter;
    private readonly ListBox _list;
    private readonly TextBlock _summary;
    private readonly TextBlock _selectedInfo;
    private readonly TextBox _order;
    private readonly TextBox _id;
    private readonly TextBox _title;
    private readonly TextBox _theme;
    private readonly TextBox _words;
    private readonly ComboBox _statusChoice;
    private readonly TextBox _notes;
    private readonly TextBlock _problems;
    private readonly TextBlock _status;
    private List<WordSearchRecord> _displayed = [];
    private WordSearchRecord? _selected;
    private bool _loading;

    public WordSearchWorkspaceWindow(PreviewProject project, string projectPath, Action<string> mainStatus)
    {
        _project = project;
        _projectPath = projectPath;
        _mainStatus = mainStatus;
        Title = $"Word Search — Diez {ProductInfo.DisplayVersion}";
        Width = 1280;
        Height = 800;
        MinWidth = 1020;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _search = new TextBox { Watermark = "Cerca ID, titolo, tema o parola...", Width = 330 };
        _filter = new ComboBox
        {
            ItemsSource = new[] { "Tutti", "Da controllare", "Approvati", "Da rifare", "Con problemi" },
            SelectedIndex = 0,
            Width = 160
        };
        _summary = new TextBlock { FontSize = 16, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _list = new ListBox { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        _selectedInfo = new TextBlock { Text = "Seleziona un puzzle", FontSize = 19, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _order = new TextBox { Width = 80 };
        _id = new TextBox { Width = 125 };
        _title = new TextBox();
        _theme = new TextBox();
        _words = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Height = 245,
            Watermark = "Una parola o espressione per riga"
        };
        _statusChoice = new ComboBox
        {
            ItemsSource = new[] { WordSearchWorkspaceService.StatusToReview, WordSearchWorkspaceService.StatusApproved, WordSearchWorkspaceService.StatusNeedsRevision },
            SelectedIndex = 0,
            Width = 160
        };
        _notes = new TextBox { AcceptsReturn = true, Height = 72, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _problems = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _status = new TextBlock
        {
            Text = "Qui vedi il prodotto reale: un puzzle per riga, non la struttura interna di Diez.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        _search.TextChanged += (_, _) => RefreshList(_selected?.ContentId);
        _filter.SelectionChanged += (_, _) => RefreshList(_selected?.ContentId);
        _list.SelectionChanged += (_, _) => LoadSelected();

        var import = Button("Importa / aggiorna XLSX", 190);
        var collect = Button("Raccogli dati del progetto", 190);
        var add = Button("Nuovo puzzle", 130);
        var export = Button("Esporta database XLSX", 190);
        var save = Button("Salva modifiche", 145);
        var normalize = Button("Normalizza parole", 145);
        var dedupe = Button("Togli doppioni", 130);
        var approve = Button("Approva", 105);
        var redo = Button("Da rifare", 105);
        var aiFix = Button("Prepara correzione AI", 175);

        import.Click += async (_, _) => await ImportXlsxAsync();
        collect.Click += async (_, _) => await CollectAsync();
        add.Click += async (_, _) => await AddAsync();
        export.Click += async (_, _) => await ExportAsync();
        save.Click += async (_, _) => await SaveSelectedAsync();
        normalize.Click += async (_, _) => await NormalizeAsync(removeDuplicates: false);
        dedupe.Click += async (_, _) => await NormalizeAsync(removeDuplicates: true);
        approve.Click += async (_, _) => await SetStatusAsync(WordSearchWorkspaceService.StatusApproved);
        redo.Click += async (_, _) => await SetStatusAsync(WordSearchWorkspaceService.StatusNeedsRevision);
        aiFix.Click += async (_, _) => await PrepareAiCorrectionAsync();

        Help(import, "Importa un database Word Search XLSX. Se trova un ID già presente, sostituisce solo quel puzzle: gli altri restano al loro posto.");
        Help(collect, "Cerca nei materiali XLSX già incorporati e nei dati AI approvati. Aggiunge ciò che manca senza sovrascrivere conflitti.");
        Help(export, "Esporta l'intero database consolidato in un XLSX leggibile in Excel e reimportabile in Diez.");
        Help(normalize, "Pulisce spazi e porta le parole in maiuscolo senza cambiare l'ID del puzzle.");
        Help(dedupe, "Rimuove le parole duplicate soltanto dal puzzle selezionato.");
        Help(aiFix, "Prepara una richiesta AI riferita esattamente a questo ID. Il resto del database non viene toccato.");

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children = { import, collect, add, export }
        };

        var searchBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _search, _filter }
        };

        var left = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 8,
            Margin = new Thickness(0, 0, 12, 0),
            Children =
            {
                new TextBlock { Text = "Tutto il database", FontSize = 21 },
                _summary.WithGridRow(1),
                searchBar.WithGridRow(2),
                _list.WithGridRow(3)
            }
        };

        var orderAndId = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                Field("Ordine", _order, 80),
                Field("ID stabile", _id, 125),
                Field("Stato", _statusChoice, 160)
            }
        };

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children = { save, normalize, dedupe, approve, redo, aiFix }
        };

        var right = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    _selectedInfo,
                    orderAndId,
                    Field("Titolo del puzzle", _title),
                    Field("Tema", _theme),
                    new TextBlock { Text = "Parole" },
                    _words,
                    Field("Note", _notes),
                    new TextBlock { Text = "Controlli su questo puzzle", FontSize = 17 },
                    _problems,
                    actionRow,
                    _status
                }
            }
        };

        Content = new Border
        {
            Padding = new Thickness(16),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                ColumnDefinitions = new ColumnDefinitions("5*,6*"),
                RowSpacing = 10,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock { Text = "Gestore Word Search", FontSize = 25 },
                            toolbar
                        }
                    },
                    left.WithGridRow(1),
                    right.WithGridRow(1).WithGridColumn(1)
                }
            }
        };

        Opened += async (_, _) =>
        {
            await CollectAsync(silentWhenEmpty: true);
            RefreshList();
        };
        RefreshList();
    }

    private async Task ImportXlsxAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importa o aggiorna database Word Search",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Database Excel XLSX") { Patterns = ["*.xlsx"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;
        var path = file.Path.LocalPath;
        try
        {
            var material = await MaterialImporter.ImportAsync(path);
            var existingMaterial = _project.Materials.FirstOrDefault(m => string.Equals(m.Sha256, material.Sha256, StringComparison.OrdinalIgnoreCase));
            if (existingMaterial is null)
            {
                _project.Materials.Add(material);
                existingMaterial = material;
            }
            var result = await WordSearchWorkspaceService.ImportXlsxFileAsync(_project, path, existingMaterial.MaterialId, replaceExisting: true);
            if (result.Recognized) await ProjectFileStore.SaveAsync(_projectPath, _project);
            RefreshList(_selected?.ContentId);
            Report(result.Message);
        }
        catch (Exception ex) { Report("Non riesco a importare il database: " + ex.Message); }
    }

    private async Task CollectAsync(bool silentWhenEmpty = false)
    {
        try
        {
            var result = await WordSearchWorkspaceService.CollectFromProjectAsync(_project, _projectPath);
            if (result.Recognized) await ProjectFileStore.SaveAsync(_projectPath, _project);
            RefreshList(_selected?.ContentId);
            if (result.Recognized || !silentWhenEmpty) Report(result.Message);
        }
        catch (Exception ex) { if (!silentWhenEmpty) Report("Non riesco a raccogliere i dati: " + ex.Message); }
    }

    private async Task AddAsync()
    {
        var record = WordSearchWorkspaceService.AddNew(_project);
        await ProjectFileStore.SaveAsync(_projectPath, _project);
        RefreshList(record.ContentId);
        Report($"Creato {record.Id}. Puoi compilarlo senza spostare gli altri puzzle.");
    }

    private async Task ExportAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta database Word Search completo",
            SuggestedFileName = WordSearchWorkspaceService.SuggestedFileName(_project),
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("Database Excel XLSX") { Patterns = ["*.xlsx"] }]
        });
        if (file is null) return;
        try
        {
            var result = await WordSearchWorkspaceService.ExportXlsxAsync(_project, file.Path.LocalPath);
            Report(result.Message);
        }
        catch (Exception ex) { Report("Errore esportazione: " + ex.Message); }
    }

    private async Task SaveSelectedAsync()
    {
        if (_selected is null) { Report("Seleziona prima un puzzle."); return; }
        _ = int.TryParse((_order.Text ?? string.Empty).Trim(), out var order);
        _selected.Order = order;
        _selected.Id = _id.Text ?? _selected.Id;
        _selected.Title = _title.Text ?? string.Empty;
        _selected.Theme = _theme.Text ?? string.Empty;
        _selected.Words = (_words.Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _selected.Status = _statusChoice.SelectedItem?.ToString() ?? WordSearchWorkspaceService.StatusToReview;
        _selected.Notes = _notes.Text ?? string.Empty;
        if (!_selected.Origin.Contains("modificat", StringComparison.OrdinalIgnoreCase))
            _selected.Origin = string.IsNullOrWhiteSpace(_selected.Origin) ? "Modificato in Diez" : _selected.Origin + " · modificato";
        WordSearchWorkspaceService.SaveRecord(_project, _selected);
        await ProjectFileStore.SaveAsync(_projectPath, _project);
        RefreshList(_selected.ContentId);
        Report($"{_selected.Id} salvato. Gli altri puzzle non sono stati modificati.");
    }

    private async Task NormalizeAsync(bool removeDuplicates)
    {
        if (_selected is null) { Report("Seleziona prima un puzzle."); return; }
        _selected.Words = (_words.Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        WordSearchWorkspaceService.NormalizeSelectedWords(_project, _selected, removeDuplicates);
        await ProjectFileStore.SaveAsync(_projectPath, _project);
        RefreshList(_selected.ContentId);
        Report(removeDuplicates ? $"Doppioni rimossi solo da {_selected.Id}." : $"Parole normalizzate solo in {_selected.Id}.");
    }

    private async Task SetStatusAsync(string value)
    {
        if (_selected is null) { Report("Seleziona prima un puzzle."); return; }
        _selected.Status = value;
        WordSearchWorkspaceService.SaveRecord(_project, _selected);
        await ProjectFileStore.SaveAsync(_projectPath, _project);
        RefreshList(_selected.ContentId);
        Report($"{_selected.Id}: {value}.");
    }

    private async Task PrepareAiCorrectionAsync()
    {
        if (_selected is null) { Report("Seleziona prima un puzzle."); return; }
        var issue = WordSearchWorkspaceService.Analyze(_project, _selected);
        var request = $"Correggi esclusivamente il puzzle {_selected.Id}. Mantieni esattamente questo ID e non modificare altri puzzle.\n" +
                      $"Titolo: {_selected.Title}\nTema: {_selected.Theme}\nParole: {string.Join(" | ", _selected.Words)}\n" +
                      $"Problemi da considerare: {string.Join(" ", issue.Messages)}\n\n" +
                      "Restituisci una sola riga tabellare con colonne: ID;Titolo;Tema;Parola01;Parola02;...;Stato. Mantieni l'ID invariato.";
        var job = AiProductionService.CreateJob(_project, AiProductionService.TypeData, $"Correzione {_selected.Id}", request, _selected.ContentId);
        await ProjectFileStore.SaveAsync(_projectPath, _project);
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(job.Prompt);
        Report($"Istruzioni per correggere {_selected.Id} preparate" + (clipboard is null ? "." : " e copiate negli appunti.") + " Il resto del database resta invariato.");
    }

    private void RefreshList(Guid? selectContentId = null)
    {
        var all = WordSearchWorkspaceService.GetRecords(_project);
        var query = (_search.Text ?? string.Empty).Trim();
        var filter = _filter.SelectedItem?.ToString() ?? "Tutti";
        IEnumerable<WordSearchRecord> selected = all;
        if (!string.IsNullOrWhiteSpace(query))
        {
            selected = selected.Where(r =>
                r.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Theme.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Words.Any(w => w.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }
        selected = filter switch
        {
            "Da controllare" => selected.Where(r => r.Status == WordSearchWorkspaceService.StatusToReview),
            "Approvati" => selected.Where(r => r.Status == WordSearchWorkspaceService.StatusApproved),
            "Da rifare" => selected.Where(r => r.Status == WordSearchWorkspaceService.StatusNeedsRevision),
            "Con problemi" => selected.Where(r => WordSearchWorkspaceService.Analyze(_project, r).HasProblems),
            _ => selected
        };
        _displayed = selected.ToList();
        _loading = true;
        _list.ItemsSource = _displayed.Select(DisplayRow).ToList();
        _summary.Text = $"{all.Count} puzzle totali · {_displayed.Count} visualizzati · {all.Count(r => r.Status == WordSearchWorkspaceService.StatusApproved)} approvati · {all.Count(r => WordSearchWorkspaceService.Analyze(_project, r).HasProblems)} con problemi";
        var index = selectContentId.HasValue ? _displayed.FindIndex(r => r.ContentId == selectContentId.Value) : -1;
        _list.SelectedIndex = index >= 0 ? index : (_displayed.Count > 0 ? 0 : -1);
        _loading = false;
        LoadSelected();
    }

    private void LoadSelected()
    {
        if (_loading) return;
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _displayed.Count)
        {
            _selected = null;
            _selectedInfo.Text = "Nessun puzzle selezionato";
            _problems.Text = string.Empty;
            return;
        }
        _selected = _displayed[_list.SelectedIndex];
        _loading = true;
        _order.Text = _selected.Order.ToString();
        _id.Text = _selected.Id;
        _title.Text = _selected.Title;
        _theme.Text = _selected.Theme;
        _words.Text = string.Join(Environment.NewLine, _selected.Words);
        _statusChoice.SelectedItem = _selected.Status;
        _notes.Text = _selected.Notes;
        _selectedInfo.Text = $"{_selected.Id} — {_selected.Title} · {_selected.Words.Count} parole · origine: {_selected.Origin}";
        var issue = WordSearchWorkspaceService.Analyze(_project, _selected);
        _problems.Text = string.Join(Environment.NewLine, issue.Messages.Select(m => "• " + m));
        _loading = false;
    }

    private string DisplayRow(WordSearchRecord record)
    {
        var problem = WordSearchWorkspaceService.Analyze(_project, record).HasProblems ? "⚠" : "✓";
        return $"{record.Order:D3}   {record.Id}   {problem}   {record.Title}   |   {record.Theme}   |   {record.Words.Count} parole   |   {record.Status}";
    }

    private void Report(string message)
    {
        _status.Text = message;
        _mainStatus(message);
    }

    private void Help(Control control, string text)
    {
        ToolTip.SetTip(control, text);
        control.GotFocus += (_, _) => _status.Text = text;
        control.PointerEntered += (_, _) => _status.Text = text;
    }

    private static Button Button(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static Control Field(string label, Control control, double? width = null)
    {
        if (width.HasValue) control.Width = width.Value;
        return new StackPanel
        {
            Spacing = 3,
            Children = { new TextBlock { Text = label }, control }
        };
    }
}