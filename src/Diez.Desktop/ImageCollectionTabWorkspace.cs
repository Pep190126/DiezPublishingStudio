using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

internal sealed class ImageCollectionTabWorkspace
{
    private readonly MainWindow _window;
    private readonly ListBox _list = new();
    private readonly TextBox _search = new() { Watermark = "Cerca numero, titolo o descrizione...", Width = 330 };
    private readonly ComboBox _filter = new()
    {
        ItemsSource = new[] { "Tutte", "Da controllare", "Approvate", "Da rifare", "Mancanti" },
        SelectedIndex = 0,
        Width = 155
    };
    private readonly TextBlock _summary = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Image _preview = new() { Stretch = Avalonia.Media.Stretch.Uniform, Height = 370 };
    private readonly TextBlock _selectedInfo = new() { FontSize = 18, Text = "Seleziona un'immagine nel tab Database.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _title = new();
    private readonly TextBox _request = new() { AcceptsReturn = true, Height = 100, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _description = new() { AcceptsReturn = true, Height = 250, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _rules = new() { AcceptsReturn = true, Height = 180, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ListBox _checks = new();
    private readonly TextBlock _checkSummary = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _aiInstructions = new() { AcceptsReturn = true, Height = 250, IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ComboBox _provider = new()
    {
        ItemsSource = new[] { AiImageBatchService.ProviderOpenAi, AiImageBatchService.ProviderGemini, AiImageBatchService.ProviderOther },
        SelectedIndex = 0,
        Width = 190
    };
    private readonly CheckBox _advancedModel = new() { Content = "Preferisci il modello più avanzato", IsChecked = false };
    private readonly ComboBox _layoutMode = new()
    {
        ItemsSource = new[] { ImageCollectionLayoutExportService.External, ImageCollectionLayoutExportService.Internal, ImageCollectionLayoutExportService.Both },
        SelectedIndex = 0,
        Width = 250
    };
    private readonly CheckBox _includeDescriptions = new() { Content = "Allega anche le descrizioni", IsChecked = false };
    private readonly ComboBox _descriptionFormat = new()
    {
        ItemsSource = new[] { ImageCollectionDescriptionService.DescriptionTxt, ImageCollectionDescriptionService.DescriptionDocx },
        SelectedIndex = 0,
        Width = 120
    };
    private readonly TextBlock _exportInfo = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _newCount = new() { Text = "50", Width = 70 };
    private readonly TextBox _newTheme = new() { Watermark = "Tema/stile comune della raccolta", Width = 420 };

    private List<AiProductionJob> _jobs = [];
    private List<AiProductionJob> _displayed = [];
    private AiProductionJob? _selected;
    private List<ImageCollectionCheck> _lastChecks = [];
    private Bitmap? _bitmap;
    private bool _loading;

    public ImageCollectionTabWorkspace(MainWindow window)
    {
        _window = window;
        Contents = [BuildDatabase(), BuildTypeBook(), BuildChecks(), BuildAi(), BuildExport()];
        _list.SelectionChanged += async (_, _) => await LoadSelectedAsync();
        _search.TextChanged += (_, _) => RefreshList();
        _filter.SelectionChanged += (_, _) => RefreshList();
        _layoutMode.SelectionChanged += (_, _) => UpdateExportInfo();
        _includeDescriptions.IsCheckedChanged += (_, _) => UpdateExportInfo();
        _descriptionFormat.SelectionChanged += (_, _) => UpdateExportInfo();
        UpdateExportInfo();
    }

    public IReadOnlyList<Control> Contents { get; }

    public async Task RefreshAsync()
    {
        if (!TrySession(out var project, out _)) return;
        var keep = _selected?.Code;
        _jobs = ImageCollectionWorkspaceService.Jobs(project);
        RefreshList(keep);
        if (!_rules.IsFocused) _rules.Text = ImageCollectionWorkspaceService.GetConsistencyRules(project);
        if (_selected is not null) await LoadSelectedAsync(force: false);
    }

    private Control BuildDatabase()
    {
        var create = Button("Crea serie", 115);
        create.Click += async (_, _) => await CreateSeriesAsync();
        return new Grid
        {
            Margin = new Thickness(8),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 8,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _search, _filter } },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { Field("Quante immagini", _newCount), Field("Tema / stile comune", _newTheme), create }
                }.WithGridRow(1),
                _summary.WithGridRow(2),
                _list.WithGridRow(3)
            }
        };
    }

    private Control BuildTypeBook()
    {
        var save = Button("Salva modifiche", 145);
        var copy = Button("Copia descrizione", 155);
        var replace = Button("Sostituisci immagine", 165);
        var approve = Button("Approva", 100);
        var redo = Button("Da rifare", 100);
        save.Click += async (_, _) => await SaveSelectedAsync();
        copy.Click += async (_, _) => await CopyDescriptionAsync();
        replace.Click += async (_, _) => await ReplaceImageAsync();
        approve.Click += async (_, _) => await ChangeStatusAsync(approve: true);
        redo.Click += async (_, _) => await ChangeStatusAsync(approve: false);

        var left = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _selectedInfo,
                new Border { Padding = new Thickness(6), Child = _preview },
                new TextBlock
                {
                    Text = "Sostituisci cambia soltanto questa posizione IMG-###. Le altre immagini non vengono spostate o rigenerate.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            }
        };
        var right = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Field("Titolo", _title),
                    Field("Cosa deve rappresentare", _request),
                    new TextBlock { Text = "Descrizione completa" },
                    _description,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { save, copy, replace, approve, redo } },
                    _status
                }
            }
        };
        return new Grid
        {
            Margin = new Thickness(8),
            ColumnDefinitions = new ColumnDefinitions("380,*"),
            ColumnSpacing = 12,
            Children = { left, right.WithGridColumn(1) }
        };
    }

    private Control BuildChecks()
    {
        var saveRules = Button("Salva regole", 130);
        var run = Button("Controlla raccolta", 155);
        var prepare = Button("Prepara le segnalate", 175);
        saveRules.Click += async (_, _) => await SaveRulesAsync();
        run.Click += async (_, _) => await RunChecksAsync();
        prepare.Click += async (_, _) => await PrepareFlaggedAsync();
        return new Grid
        {
            Margin = new Thickness(8),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            RowSpacing = 8,
            Children =
            {
                new TextBlock { Text = "Coerenza dell'intera raccolta", FontSize = 20 },
                new StackPanel
                {
                    Spacing = 5,
                    Children =
                    {
                        new TextBlock { Text = "Regole visive da mantenere", FontSize = 16 },
                        _rules,
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { saveRules, run, prepare } }
                    }
                }.WithGridRow(1),
                _checkSummary.WithGridRow(2),
                _checks.WithGridRow(3),
                new TextBlock
                {
                    Text = "I controlli indicano sempre IMG-### reali. Preparare le segnalate non modifica alcuna immagine: le rende soltanto disponibili per la correzione in blocco.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }.WithGridRow(4)
            }
        };
    }

    private Control BuildAi()
    {
        var prepareOne = Button("Prepara correzione selezionata", 230);
        var copy = Button("Copia per l'AI", 145);
        var pack = Button("XLSX immagini da rifare", 195);
        var import = Button("Importa ZIP risultati", 180);
        prepareOne.Click += (_, _) => PrepareSelectedInstructions();
        copy.Click += async (_, _) => await CopyAiInstructionsAsync();
        pack.Click += async (_, _) => await ExportCorrectionPackAsync();
        import.Click += async (_, _) => await ImportResultZipAsync();
        return new ScrollViewer
        {
            Margin = new Thickness(8),
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 9,
                Children =
                {
                    new TextBlock { Text = "Correggi o rigenera senza perdere l'ordine", FontSize = 20 },
                    new TextBlock
                    {
                        Text = "La nuova versione resta legata allo stesso IMG-###. Sei tu a controllarla e approvarla.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { prepareOne, copy } },
                    _aiInstructions,
                    new Separator(),
                    new TextBlock { Text = "Correzione in blocco", FontSize = 17 },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _provider, _advancedModel, pack, import } },
                    _status
                }
            }
        };
    }

    private Control BuildExport()
    {
        var export = Button("Esporta", 140);
        export.Click += async (_, _) => await ExportAsync();
        return new StackPanel
        {
            Margin = new Thickness(8),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Consegna della raccolta", FontSize = 20 },
                Field("Come vuoi impaginare?", _layoutMode),
                _exportInfo,
                _includeDescriptions,
                Field("Formato descrizione", _descriptionFormat),
                export,
                _status,
                new TextBlock
                {
                    Text = "Documento modificabile (DOCX) è un formato di scambio, non una destinazione legata a Word. Questo lascia aperta anche una futura destinazione diretta verso Google Documenti.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            }
        };
    }

    private async Task CreateSeriesAsync()
    {
        if (!TrySession(out var project, out var path)) return;
        if (!int.TryParse((_newCount.Text ?? string.Empty).Trim(), out var count) || count < 1 || count > 500)
        {
            SetStatus("Scegli un numero di immagini tra 1 e 500.");
            return;
        }
        AiImageBatchService.CreateImageSeries(project, count, (_newTheme.Text ?? string.Empty).Trim(), "Tavola");
        if (string.IsNullOrWhiteSpace(BookTypeProfileService.Get(project)))
            BookTypeProfileService.Set(project, BookTypeProfileService.ImageCollection);
        await ProjectFileStore.SaveAsync(path, project);
        SetStatus($"Create {count} posizioni IMG-###. Puoi lavorarle una alla volta o in blocco.");
        await RefreshAsync();
    }

    private void RefreshList(string? keepCode = null)
    {
        if (_loading) return;
        var query = (_search.Text ?? string.Empty).Trim();
        var filter = _filter.SelectedItem?.ToString() ?? "Tutte";
        _displayed = _jobs.Where(j =>
        {
            if (query.Length > 0 && !j.Code.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !j.Title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !ImageCollectionDescriptionService.GetDescription(j).Contains(query, StringComparison.OrdinalIgnoreCase)) return false;
            return filter switch
            {
                "Da controllare" => string.Equals(j.Status, AiProductionService.StatusToReview, StringComparison.Ordinal),
                "Approvate" => string.Equals(j.Status, AiProductionService.StatusApproved, StringComparison.Ordinal),
                "Da rifare" => string.Equals(j.Status, AiProductionService.StatusNeedsRevision, StringComparison.Ordinal),
                "Mancanti" => !j.ResultMaterialId.HasValue,
                _ => true
            };
        }).ToList();

        _loading = true;
        try
        {
            _list.ItemsSource = _displayed.Select(j => $"{j.Code} — {j.Title} · {AiProductionService.DisplayStatus(j.Status)}").ToList();
            var index = string.IsNullOrWhiteSpace(keepCode) ? (_displayed.Count > 0 ? 0 : -1) : _displayed.FindIndex(j => string.Equals(j.Code, keepCode, StringComparison.OrdinalIgnoreCase));
            _list.SelectedIndex = index >= 0 ? index : (_displayed.Count > 0 ? 0 : -1);
            _selected = _list.SelectedIndex >= 0 && _list.SelectedIndex < _displayed.Count ? _displayed[_list.SelectedIndex] : null;
        }
        finally { _loading = false; }

        var approved = _jobs.Count(j => string.Equals(j.Status, AiProductionService.StatusApproved, StringComparison.Ordinal));
        var missing = _jobs.Count(j => !j.ResultMaterialId.HasValue);
        var redo = _jobs.Count(j => string.Equals(j.Status, AiProductionService.StatusNeedsRevision, StringComparison.Ordinal));
        _summary.Text = $"{_jobs.Count} immagini · {approved} approvate · {missing} mancanti · {redo} da rifare";
    }

    private async Task LoadSelectedAsync(bool force = true)
    {
        if (_loading) return;
        if (_list.SelectedIndex >= 0 && _list.SelectedIndex < _displayed.Count) _selected = _displayed[_list.SelectedIndex];
        if (_selected is null || !TrySession(out var project, out var path)) return;

        if (force || !string.Equals(_selectedInfo.Tag?.ToString(), _selected.Code, StringComparison.OrdinalIgnoreCase))
        {
            _title.Text = _selected.Title;
            _request.Text = _selected.Request;
            _description.Text = ImageCollectionDescriptionService.GetDescription(_selected);
        }
        _selectedInfo.Tag = _selected.Code;
        _selectedInfo.Text = $"{_selected.Code} — {(_selected.Title.Length == 0 ? "Senza titolo" : _selected.Title)} · {AiProductionService.DisplayStatus(_selected.Status)}";
        _preview.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        if (!_selected.ResultMaterialId.HasValue) return;
        var material = project.Materials.FirstOrDefault(m => m.MaterialId == _selected.ResultMaterialId.Value);
        if (material is null) return;
        var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(path, material);
        if (bytes is null || bytes.Length == 0) return;
        try
        {
            using var memory = new MemoryStream(bytes);
            _bitmap = new Bitmap(memory);
            _preview.Source = _bitmap;
        }
        catch { _preview.Source = null; }
    }

    private async Task SaveSelectedAsync()
    {
        if (_selected is null || !TrySession(out var project, out var path)) return;
        _selected.Title = (_title.Text ?? string.Empty).Trim();
        _selected.Request = (_request.Text ?? string.Empty).Trim();
        ImageCollectionDescriptionService.SetDescription(_selected, _description.Text);
        AiProductionService.RebuildPrompt(project, _selected);
        await ProjectFileStore.SaveAsync(path, project);
        SetStatus($"{_selected.Code} salvata. Nessun'altra immagine è stata modificata.");
        RefreshList(_selected.Code);
    }

    private async Task CopyDescriptionAsync()
    {
        var clipboard = TopLevel.GetTopLevel(_window)?.Clipboard;
        if (clipboard is null) { SetStatus("Non riesco ad accedere agli appunti di Windows."); return; }
        await clipboard.SetTextAsync(_description.Text ?? string.Empty);
        SetStatus(_selected is null ? "Descrizione copiata." : $"Descrizione di {_selected.Code} copiata.");
    }

    private async Task ReplaceImageAsync()
    {
        if (_selected is null || !TrySession(out var project, out var path)) return;
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Sostituisci {_selected.Code}",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Immagini") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;
        var result = await AiProductionService.AttachResultFileAsync(project, path, _selected, file.Path.LocalPath);
        SetStatus(result.Message);
        await RefreshAsync();
    }

    private async Task ChangeStatusAsync(bool approve)
    {
        if (_selected is null || !TrySession(out var project, out var path)) return;
        var result = approve ? AiProductionService.Approve(project, _selected) : AiProductionService.NeedsRevision(_selected);
        if (result.Success) await ProjectFileStore.SaveAsync(path, project);
        SetStatus(result.Message);
        RefreshList(_selected.Code);
    }

    private async Task SaveRulesAsync()
    {
        if (!TrySession(out var project, out var path)) return;
        ImageCollectionWorkspaceService.SetConsistencyRules(project, _rules.Text);
        await ProjectFileStore.SaveAsync(path, project);
        SetStatus("Regole di coerenza salvate per tutta la raccolta.");
    }

    private async Task RunChecksAsync()
    {
        if (!TrySession(out var project, out var path)) return;
        ImageCollectionWorkspaceService.SetConsistencyRules(project, _rules.Text);
        await ProjectFileStore.SaveAsync(path, project);
        _lastChecks = await ImageCollectionWorkspaceService.CheckAsync(project, path);
        _checks.ItemsSource = _lastChecks.Select(c => c.Message).ToList();
        var actionable = _lastChecks.Count(c => c.NeedsAction && c.Code.StartsWith("IMG-", StringComparison.OrdinalIgnoreCase));
        _checkSummary.Text = _lastChecks.Count == 0 ? "Nessun problema misurabile trovato nella raccolta." : $"{_lastChecks.Count} segnalazioni · {actionable} immagini richiedono un intervento concreto.";
    }

    private async Task PrepareFlaggedAsync()
    {
        if (!TrySession(out var project, out var path)) return;
        if (_lastChecks.Count == 0) await RunChecksAsync();
        var codes = _lastChecks.Where(c => c.NeedsAction && c.Code.StartsWith("IMG-", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var job in ImageCollectionWorkspaceService.Jobs(project).Where(j => codes.Contains(j.Code))) AiProductionService.NeedsRevision(job);
        if (codes.Count > 0) await ProjectFileStore.SaveAsync(path, project);
        SetStatus(codes.Count == 0 ? "Non ci sono immagini da preparare per la correzione." : $"{codes.Count} immagini segnate da rifare. Nessun file è stato modificato.");
        await RefreshAsync();
    }

    private void PrepareSelectedInstructions()
    {
        if (_selected is null || !TrySession(out var project, out _)) { _aiInstructions.Text = "Seleziona prima un'immagine nel tab Database."; return; }
        _aiInstructions.Text = ImageCollectionWorkspaceService.BuildCorrectionInstructions(project, _selected);
    }

    private async Task CopyAiInstructionsAsync()
    {
        if (string.IsNullOrWhiteSpace(_aiInstructions.Text)) PrepareSelectedInstructions();
        var clipboard = TopLevel.GetTopLevel(_window)?.Clipboard;
        if (clipboard is null) { SetStatus("Non riesco ad accedere agli appunti di Windows."); return; }
        await clipboard.SetTextAsync(_aiInstructions.Text ?? string.Empty);
        SetStatus("Istruzioni copiate per l'AI.");
    }

    private async Task ExportCorrectionPackAsync()
    {
        if (!TrySession(out var project, out _)) return;
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Prepara le immagini mancanti o da rifare",
            SuggestedFileName = AiImageBatchService.SuggestedPackName(project, correction: true),
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("File Excel XLSX") { Patterns = ["*.xlsx"] }]
        });
        if (file is null) return;
        var result = await AiImageBatchService.ExportPackXlsxAsync(project, file.Path.LocalPath,
            _provider.SelectedItem?.ToString() ?? AiImageBatchService.ProviderOpenAi,
            _advancedModel.IsChecked == true, onlyMissingOrToRedo: true);
        SetStatus(result.Message);
    }

    private async Task ImportResultZipAsync()
    {
        if (!TrySession(out var project, out var path)) return;
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importa le immagini ottenute",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Archivio ZIP") { Patterns = ["*.zip"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;
        var result = await AiImageBatchService.ImportResultZipAsync(project, path, file.Path.LocalPath);
        SetStatus(result.Message);
        await RefreshAsync();
    }

    private void UpdateExportInfo()
    {
        var mode = _layoutMode.SelectedItem?.ToString() ?? ImageCollectionLayoutExportService.External;
        var external = !string.Equals(mode, ImageCollectionLayoutExportService.Internal, StringComparison.Ordinal);
        _includeDescriptions.IsVisible = external;
        _descriptionFormat.IsVisible = external && _includeDescriptions.IsChecked == true;
        _exportInfo.Text = mode switch
        {
            ImageCollectionLayoutExportService.Internal => "Documento modificabile (DOCX) con le immagini già inserite. Gli originali restano nel progetto.",
            ImageCollectionLayoutExportService.Both => "Un pacchetto con documento modificabile + immagini originali separate. Le descrizioni restano facoltative.",
            _ => "Immagini originali separate per impaginare fuori Diez. Le descrizioni sono facoltative e possono essere TXT o DOCX con lo stesso nome base."
        };
    }

    private async Task ExportAsync()
    {
        if (!TrySession(out var project, out var path)) return;
        var mode = _layoutMode.SelectedItem?.ToString() ?? ImageCollectionLayoutExportService.External;
        var internalOnly = string.Equals(mode, ImageCollectionLayoutExportService.Internal, StringComparison.Ordinal);
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta la raccolta",
            SuggestedFileName = ImageCollectionLayoutExportService.SuggestedName(project, mode),
            DefaultExtension = internalOnly ? "docx" : "zip",
            FileTypeChoices = internalOnly
                ? [new FilePickerFileType("Documento modificabile DOCX") { Patterns = ["*.docx"] }]
                : [new FilePickerFileType("Archivio ZIP") { Patterns = ["*.zip"] }]
        });
        if (file is null) return;
        var result = await ImageCollectionLayoutChoiceService.ExportAsync(project, path, file.Path.LocalPath, mode,
            _includeDescriptions.IsChecked == true && _includeDescriptions.IsVisible,
            _descriptionFormat.SelectedItem?.ToString() ?? ImageCollectionDescriptionService.DescriptionTxt);
        SetStatus(result.Message);
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
        _status.Text = message;
        var main = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_window) as TextBlock;
        if (main is not null) main.Text = message;
    }

    private static StackPanel Field(string label, Control control) => new()
    {
        Spacing = 4,
        Children = { new TextBlock { Text = label }, control }
    };

    private static Button Button(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };
}
