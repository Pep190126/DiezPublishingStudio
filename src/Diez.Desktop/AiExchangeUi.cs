using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

internal static class AiExchangeUi
{
    public static void Attach(MainWindow window)
    {
        if (window.Content is not Border border || border.Child is not StackPanel root) return;
        var projectButtons = root.Children.OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.Ordinal)));
        if (projectButtons is null || projectButtons.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Prompt Pack AI", StringComparison.Ordinal))) return;

        var button = new Button
        {
            Content = "Prompt Pack AI",
            Width = 150,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ToolTip.SetTip(button, "Prepara il lavoro per l'AI, importa uno o più ZIP e controlla i risultati prima di approvarli.");
        button.Click += async (_, _) =>
        {
            if (!TrySession(window, out var project, out var path))
            {
                SetStatus(window, "Prima crea o apri un progetto .diez.");
                return;
            }
            await new AiExchangeWindow(project, path, message => SetStatus(window, message)).ShowDialog(window);
        };
        projectButtons.Children.Add(button);
    }

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
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

internal sealed class AiExchangeWindow : Window
{
    private readonly PreviewProject _project;
    private readonly string _projectPath;
    private readonly Action<string> _mainStatus;
    private AiExchangeState _state;
    private readonly ListBox _units;
    private readonly ComboBox _mode;
    private readonly CheckBox _consistent;
    private readonly TextBox _consistentRules;
    private readonly TextBox _paradigmRoles;
    private readonly TextBlock _summary;
    private readonly TextBlock _status;

    public AiExchangeWindow(PreviewProject project, string projectPath, Action<string> mainStatus)
    {
        _project = project;
        _projectPath = projectPath;
        _mainStatus = mainStatus;
        _state = AiExchangeStateStore.Load(project);

        Title = "Prompt Pack AI e risultati";
        Width = 1040;
        Height = 780;
        MinWidth = 820;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _units = new ListBox { Height = 190 };
        _mode = new ComboBox
        {
            ItemsSource = AiExchangeModes.All.Select(m => new ModeChoice(m, AiExchangeModes.UserLabel(m))).ToList(),
            SelectedIndex = 1,
            Width = 470
        };
        var imageCollection = BookTypeProfileService.IsImageCollection(project);
        _consistent = new CheckBox
        {
            Content = "Consistent — mantieni coerenti le immagini della raccolta",
            IsChecked = imageCollection && !string.IsNullOrWhiteSpace(ImageCollectionWorkspaceService.GetConsistencyRules(project)),
            IsVisible = imageCollection
        };
        _consistentRules = new TextBox
        {
            AcceptsReturn = true,
            Height = 90,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = ImageCollectionWorkspaceService.GetConsistencyRules(project),
            Watermark = "Es. stesso personaggio, stesso stile e tratto; ambientazioni libere.",
            IsVisible = imageCollection
        };
        _paradigmRoles = new TextBox
        {
            Watermark = "Ruolo/i del paradigma: personaggio, stile, palette...",
            Width = 500
        };
        _summary = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 16 };
        _status = new TextBlock
        {
            Text = "Diez prepara il package; al ritorno importa uno o più ZIP e ricompone i contenuti usando gli ID.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var sync = Button("Aggiorna contenuti", 165);
        var applyMode = Button("Applica modalità", 160);
        var addParadigm = Button("Aggiungi paradigma", 170);
        var export = Button("Crea Prompt Pack ZIP", 190);
        var import = Button("Importa risultati AI", 180);
        var review = Button("Controlla risultati", 175);

        sync.Click += async (_, _) => await SyncAsync();
        applyMode.Click += async (_, _) => await ApplyModeAsync();
        addParadigm.Click += async (_, _) => await AddParadigmAsync();
        export.Click += async (_, _) => await ExportAsync();
        import.Click += async (_, _) => await ImportAsync();
        review.Click += async (_, _) => await new AiExchangeReviewWindow(_project, _projectPath, _state, Report).ShowDialog(this);

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new StackPanel
                {
                    Spacing = 11,
                    Children =
                    {
                        new TextBlock { Text = "AI: prepara → genera → importa → controlla → approva", FontSize = 24, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        new TextBlock { Text = "Il Tipo libro decide cosa serve; Prompt Pack e API sono soltanto due modi per far andare e tornare lo stesso lavoro.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        _summary,
                        _units,
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { sync } },
                        new Separator(),
                        new TextBlock { Text = "Come usare input e AI", FontSize = 19 },
                        _mode,
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { applyMode } },
                        _consistent,
                        _consistentRules,
                        new Separator(),
                        new TextBlock { Text = "Immagini paradigma", FontSize = 19 },
                        new TextBlock { Text = "Puoi assegnare una o più immagini come riferimento. Se selezioni un elemento, il paradigma vale per quello; altrimenti per tutte le immagini del Job.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        _paradigmRoles,
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { addParadigm } },
                        new Separator(),
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { export, import, review } },
                        _status
                    }
                }
            }
        };
        Refresh();
    }

    private async Task SyncAsync()
    {
        _state = AiExchangeStateStore.Load(_project);
        await PersistAsync();
        Refresh();
        Report("Contenuti AI sincronizzati con le Work Unit Diez.");
    }

    private async Task ApplyModeAsync()
    {
        if (_mode.SelectedItem is not ModeChoice choice) return;
        var selected = SelectedUnit();
        var targets = selected is null ? _state.WorkUnits : [selected];
        foreach (var unit in targets) unit.Mode = choice.Mode;
        await PersistConsistencyAndStateAsync();
        Refresh();
        Report(selected is null
            ? $"Modalità '{choice.Label}' applicata a tutti i contenuti AI del progetto."
            : $"Modalità '{choice.Label}' applicata a {selected.Code}.");
    }

    private async Task AddParadigmAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Scegli una o più immagini paradigma",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Immagini") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"] }]
        });
        if (files.Count == 0) return;
        var roles = (_paradigmRoles.Text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (roles.Count == 0) roles.Add("reference");
        var selected = SelectedUnit();
        var targets = selected is null
            ? _state.WorkUnits.Where(w => string.Equals(w.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase)).ToList()
            : [selected];
        var added = 0;
        foreach (var file in files)
        {
            var material = await MaterialImporter.ImportAsync(file.Path.LocalPath);
            var existing = _project.Materials.FirstOrDefault(m => string.Equals(m.Sha256, material.Sha256, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                material.Summary = "Immagine paradigma AI · " + material.Summary;
                _project.Materials.Add(material);
                existing = material;
            }
            var paradigm = new AiExchangeParadigm
            {
                MaterialId = existing.MaterialId,
                Scope = selected is null ? "COLLECTION" : "ITEM",
                Roles = roles.ToList(),
                Description = string.Join(", ", roles)
            };
            _state.Paradigms.Add(paradigm);
            foreach (var unit in targets)
                if (!unit.ParadigmIds.Contains(paradigm.ParadigmId)) unit.ParadigmIds.Add(paradigm.ParadigmId);
            added++;
        }
        await PersistConsistencyAndStateAsync();
        Refresh();
        Report($"Aggiunte {added} immagini paradigma con ruolo: {string.Join(", ", roles)}.");
    }

    private async Task ExportAsync()
    {
        await PersistConsistencyAndStateAsync();
        var selected = SelectedUnit();
        var units = selected is null ? _state.WorkUnits.ToList() : [selected];
        if (units.Count == 0) { Report("Non ci sono contenuti AI da inserire nel Prompt Pack."); return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salva Prompt Pack Diez",
            SuggestedFileName = "diez-prompt-pack.zip",
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("Prompt Pack Diez") { Patterns = ["*.zip"] }]
        });
        if (file is null) return;
        var result = await AiExchangePromptPackBuilder.BuildAsync(_project, _projectPath, _state,
            units.Select(u => u.WorkUnitId), file.Path.LocalPath);
        Refresh();
        Report(result.Message);
    }

    private async Task ImportAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importa uno o più ZIP restituiti dall'AI",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Risultati AI Diez") { Patterns = ["*.zip"] }]
        });
        if (files.Count == 0) return;
        var result = await AiExchangeResponseImporter.ImportAsync(_project, _projectPath, _state,
            files.Select(f => f.Path.LocalPath));
        _state = AiExchangeStateStore.Load(_project);
        Refresh();
        Report(result.Message);
    }

    private async Task PersistConsistencyAndStateAsync()
    {
        if (BookTypeProfileService.IsImageCollection(_project))
            AiExchangeStateStore.EnsureVisualConsistencyContext(_project, _state, _consistent.IsChecked == true, _consistentRules.Text);
        await PersistAsync();
    }

    private async Task PersistAsync()
    {
        AiExchangeStateStore.Save(_project, _state);
        await ProjectFileStore.SaveAsync(_projectPath, _project);
    }

    private AiExchangeWorkUnit? SelectedUnit() =>
        (_units.SelectedItem as UnitRow)?.Unit;

    private void Refresh()
    {
        var rows = _state.WorkUnits.OrderBy(w => w.Position).ThenBy(w => w.Code)
            .Select(w => new UnitRow(w, StateLabel(w))).ToList();
        _units.ItemsSource = rows;
        var approved = _state.Versions.Count(v => v.Status == AiExchangeVersionStatuses.Approved);
        var candidates = _state.Versions.Count(v => v.Status == AiExchangeVersionStatuses.Candidate);
        var incomplete = _state.Versions.Count(v => v.Status == AiExchangeVersionStatuses.Incomplete);
        _summary.Text = $"{_state.WorkUnits.Count} contenuti · {approved} approvati · {candidates} da controllare · {incomplete} incompleti · {_state.Paradigms.Count} paradigmi";
    }

    private string StateLabel(AiExchangeWorkUnit unit)
    {
        var latest = _state.Versions.Where(v => v.WorkUnitId == unit.WorkUnitId).OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        if (latest is null) return "mancante";
        return latest.Status switch
        {
            AiExchangeVersionStatuses.Approved => "approvato",
            AiExchangeVersionStatuses.Incomplete => "incompleto",
            AiExchangeVersionStatuses.Stale => "da verificare",
            AiExchangeVersionStatuses.Rejected => "scartato",
            _ => "nuova proposta"
        };
    }

    private void Report(string message)
    {
        _status.Text = message;
        _mainStatus(message);
    }

    private static Button Button(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private sealed record ModeChoice(string Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record UnitRow(AiExchangeWorkUnit Unit, string State)
    {
        public override string ToString() => $"{Unit.Code} · {AiExchangeModes.UserLabel(Unit.Mode)} · {State}";
    }
}

internal sealed class AiExchangeReviewWindow : Window
{
    private readonly PreviewProject _project;
    private readonly string _projectPath;
    private readonly AiExchangeState _state;
    private readonly Action<string> _report;
    private readonly ListBox _list;
    private readonly ContentControl _preview;
    private readonly TextBox _description;
    private readonly TextBlock _status;
    private string? _externalPath;
    private Guid? _externalSourceVersionId;

    public AiExchangeReviewWindow(PreviewProject project, string projectPath, AiExchangeState state, Action<string> report)
    {
        _project = project;
        _projectPath = projectPath;
        _state = state;
        _report = report;
        Title = "Controlla risultati AI";
        Width = 1080;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _list = new ListBox { Width = 320, Height = 610 };
        _list.SelectionChanged += async (_, _) => await ShowSelectedAsync();
        _preview = new ContentControl { Width = 690, Height = 430 };
        _description = new TextBox { AcceptsReturn = true, Height = 115, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        var approve = Button("Approva", 120);
        var open = Button("Apri esternamente", 165);
        var reimport = Button("Reimporta modifica", 165);
        var saveDescription = Button("Salva descrizione", 155);
        var compare = Button("Confronta", 125);
        approve.Click += async (_, _) => await ApproveAsync();
        open.Click += async (_, _) => await OpenExternalAsync();
        reimport.Click += async (_, _) => await ReimportExternalAsync();
        saveDescription.Click += async (_, _) => await SaveDescriptionAsync();
        compare.Click += async (_, _) => await CompareAsync();

        Content = new Border
        {
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 9,
                Children =
                {
                    new TextBlock { Text = "Risultati AI — controlla prima di approvare", FontSize = 23 },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Children =
                        {
                            _list,
                            new StackPanel
                            {
                                Width = 700,
                                Spacing = 7,
                                Children =
                                {
                                    _preview,
                                    new TextBlock { Text = "Descrizione / testo associato" },
                                    _description,
                                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { saveDescription, open, reimport, compare, approve } },
                                    _status
                                }
                            }
                        }
                    }
                }
            }
        };
        RefreshList();
        if (_list.ItemCount > 0) _list.SelectedIndex = 0;
    }

    private AiExchangeWorkUnit? Unit => (_list.SelectedItem as ReviewRow)?.Unit;
    private AiExchangeVersion? Version => Unit is null ? null : _state.Versions
        .Where(v => v.WorkUnitId == Unit.WorkUnitId && v.Status != AiExchangeVersionStatuses.Rejected)
        .OrderByDescending(v => v.VersionNumber).FirstOrDefault();

    private async Task ShowSelectedAsync()
    {
        _externalPath = null;
        _externalSourceVersionId = null;
        var unit = Unit;
        var version = Version;
        if (unit is null || version is null)
        {
            _preview.Content = new TextBlock { Text = "Nessun risultato ancora disponibile." };
            _description.Text = string.Empty;
            return;
        }
        _description.Text = version.Description;
        if (version.MaterialId is Guid materialId)
        {
            var material = _project.Materials.FirstOrDefault(m => m.MaterialId == materialId);
            if (material is not null && string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            {
                var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(_projectPath, material);
                if (bytes is not null)
                {
                    _preview.Content = new Image
                    {
                        Source = new Bitmap(new MemoryStream(bytes)),
                        MaxWidth = 670,
                        MaxHeight = 410,
                        Stretch = Avalonia.Media.Stretch.Uniform
                    };
                }
            }
            else if (material is not null)
            {
                _preview.Content = new TextBox
                {
                    Text = string.IsNullOrWhiteSpace(version.TextContent) ? material.Preview : version.TextContent,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Height = 410
                };
            }
        }
        else
        {
            _preview.Content = new TextBox
            {
                Text = version.TextContent,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Height = 410
            };
        }
        _status.Text = $"{unit.Code} · versione {version.VersionNumber} · {version.Status} · descrizione {version.DescriptionStatus}";
    }

    private async Task SaveDescriptionAsync()
    {
        var unit = Unit;
        var version = Version;
        if (unit is null || version is null) return;
        version.Description = (_description.Text ?? string.Empty).Trim();
        if (string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
        {
            version.DescriptionStatus = string.IsNullOrWhiteSpace(version.Description)
                ? AiExchangeDescriptionStatuses.Missing
                : AiExchangeDescriptionStatuses.Valid;
            if (version.MaterialId.HasValue && version.DescriptionStatus == AiExchangeDescriptionStatuses.Valid &&
                version.Status == AiExchangeVersionStatuses.Incomplete)
                version.Status = AiExchangeVersionStatuses.Candidate;
            if (unit.LegacyAiJobId is Guid legacyId)
            {
                var legacy = _project.AiProductionJobs.FirstOrDefault(j => j.JobId == legacyId);
                if (legacy is not null) ImageCollectionDescriptionService.SetDescription(legacy, version.Description);
            }
        }
        await PersistAsync();
        RefreshList();
        _status.Text = "Descrizione aggiornata per questa versione.";
    }

    private async Task ApproveAsync()
    {
        var version = Version;
        if (version is null) return;
        if (!AiExchangeResultIngestor.Approve(_project, _state, version.VersionId, out var message))
        {
            _status.Text = message;
            return;
        }
        await PersistAsync();
        RefreshList();
        _status.Text = message;
        _report(message);
    }

    private async Task OpenExternalAsync()
    {
        var version = Version;
        if (version?.MaterialId is not Guid materialId) { _status.Text = "Questo risultato non ha un file da aprire."; return; }
        var material = _project.Materials.FirstOrDefault(m => m.MaterialId == materialId);
        if (material is null) return;
        var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(_projectPath, material);
        if (bytes is null) { _status.Text = "Non riesco a leggere il file dal progetto."; return; }
        var root = Path.Combine(Path.GetTempPath(), "DiezExternalReview");
        Directory.CreateDirectory(root);
        var extension = Path.GetExtension(material.FileName);
        _externalPath = Path.Combine(root, version.VersionId.ToString("N") + extension);
        await File.WriteAllBytesAsync(_externalPath, bytes);
        _externalSourceVersionId = version.VersionId;
        Process.Start(new ProcessStartInfo(_externalPath) { UseShellExecute = true });
        _status.Text = "File aperto nel programma associato. Se lo modifichi e lo salvi, usa 'Reimporta modifica'.";
    }

    private async Task ReimportExternalAsync()
    {
        if (_externalSourceVersionId is not Guid sourceId || string.IsNullOrWhiteSpace(_externalPath) || !File.Exists(_externalPath))
        {
            _status.Text = "Prima apri questa versione esternamente.";
            return;
        }
        var source = _state.Versions.First(v => v.VersionId == sourceId);
        var material = await MaterialImporter.ImportAsync(_externalPath);
        if (!string.IsNullOrWhiteSpace(source.ContentSha256) && string.Equals(source.ContentSha256, material.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            _status.Text = "Il file esterno non risulta modificato.";
            return;
        }
        var existing = _project.Materials.FirstOrDefault(m => string.Equals(m.Sha256, material.Sha256, StringComparison.OrdinalIgnoreCase));
        if (existing is null) { _project.Materials.Add(material); existing = material; }
        var version = AiExchangeResultIngestor.RegisterExternalEdit(_project, _state, sourceId, existing);
        await PersistAsync();
        RefreshList();
        _list.SelectedIndex = Math.Max(0, _list.SelectedIndex);
        _status.Text = $"Modifica esterna importata come versione {version.VersionNumber}. La descrizione va verificata prima dell'approvazione.";
    }

    private async Task CompareAsync()
    {
        var unit = Unit;
        var current = Version;
        if (unit is null || current is null) return;
        var approved = unit.ApprovedVersionId.HasValue
            ? _state.Versions.FirstOrDefault(v => v.VersionId == unit.ApprovedVersionId.Value)
            : null;
        if (approved is null || approved.VersionId == current.VersionId)
        {
            _status.Text = "Non c'è una versione approvata diversa da confrontare.";
            return;
        }
        await new AiExchangeComparisonWindow(_project, _projectPath, unit, approved, current).ShowDialog(this);
    }

    private async Task PersistAsync()
    {
        AiExchangeStateStore.Save(_project, _state);
        await ProjectFileStore.SaveAsync(_projectPath, _project);
    }

    private void RefreshList()
    {
        var selectedId = Unit?.WorkUnitId;
        var rows = _state.WorkUnits.OrderBy(w => w.Position).ThenBy(w => w.Code)
            .Select(w => new ReviewRow(w, Label(w))).ToList();
        _list.ItemsSource = rows;
        if (selectedId.HasValue)
        {
            var index = rows.FindIndex(r => r.Unit.WorkUnitId == selectedId.Value);
            if (index >= 0) _list.SelectedIndex = index;
        }
    }

    private string Label(AiExchangeWorkUnit unit)
    {
        var version = _state.Versions.Where(v => v.WorkUnitId == unit.WorkUnitId).OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        if (version is null) return "mancante";
        return version.Status switch
        {
            AiExchangeVersionStatuses.Approved => "✓ Approvato",
            AiExchangeVersionStatuses.Incomplete => "⚠ Incompleto",
            AiExchangeVersionStatuses.Stale => "⚠ Da verificare",
            _ => "● Nuova proposta"
        };
    }

    private static Button Button(string text, double width) => new() { Content = text, Width = width };

    private sealed record ReviewRow(AiExchangeWorkUnit Unit, string State)
    {
        public override string ToString() => $"{Unit.Code} · {State}";
    }
}

internal sealed class AiExchangeComparisonWindow : Window
{
    public AiExchangeComparisonWindow(
        PreviewProject project,
        string projectPath,
        AiExchangeWorkUnit unit,
        AiExchangeVersion approved,
        AiExchangeVersion candidate)
    {
        Title = $"Confronta {unit.Code}";
        Width = 1120;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var left = BuildVersion(project, projectPath, unit, approved, "Versione approvata");
        var right = BuildVersion(project, projectPath, unit, candidate, "Nuova proposta");
        Content = new Border
        {
            Padding = new Thickness(15),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                Children = { left, right }
            }
        };
    }

    private static Control BuildVersion(PreviewProject project, string projectPath, AiExchangeWorkUnit unit, AiExchangeVersion version, string title)
    {
        var panel = new StackPanel { Width = 530, Spacing = 7 };
        panel.Children.Add(new TextBlock { Text = $"{title} · v{version.VersionNumber}", FontSize = 19 });
        if (version.MaterialId is Guid materialId)
        {
            var material = project.Materials.FirstOrDefault(m => m.MaterialId == materialId);
            if (material is not null && string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            {
                var bytes = ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material).GetAwaiter().GetResult();
                if (bytes is not null)
                    panel.Children.Add(new Image { Source = new Bitmap(new MemoryStream(bytes)), Width = 510, Height = 470, Stretch = Avalonia.Media.Stretch.Uniform });
            }
            else if (material is not null)
                panel.Children.Add(new TextBox { Text = material.Preview, IsReadOnly = true, AcceptsReturn = true, Height = 470, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        }
        else
            panel.Children.Add(new TextBox { Text = version.TextContent, IsReadOnly = true, AcceptsReturn = true, Height = 470, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(new TextBox { Text = version.Description, IsReadOnly = true, AcceptsReturn = true, Height = 90, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        return panel;
    }
}
