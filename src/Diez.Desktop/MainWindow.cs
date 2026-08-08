using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

public sealed class MainWindow : Window
{
    private readonly TextBlock _status;
    private readonly ListBox _materialsList;
    private readonly ListBox _structureList;
    private readonly TextBox _preview;
    private string? _currentProjectPath;
    private PreviewProject? _project;

    public MainWindow(string? startupProjectPath = null)
    {
        Title = "Diez Publishing Studio — 0.3 Preview";
        Width = 1140;
        Height = 880;
        MinWidth = 900;
        MinHeight = 700;

        var logo = new TextBlock { Text = "∞", FontSize = 42, HorizontalAlignment = HorizontalAlignment.Center };
        var title = new TextBlock { Text = "Diez Publishing Studio", FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center };
        var subtitle = new TextBlock
        {
            Text = "Preview 0.3 — materiali incorporati + prima struttura editoriale automatica",
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var newButton = MakeButton("Nuovo progetto");
        newButton.Click += async (_, _) => await CreateProjectAsync();
        var openButton = MakeButton("Apri .diez");
        openButton.Click += async (_, _) => await OpenProjectAsync();
        var importButton = MakeButton("Importa materiali");
        importButton.Click += async (_, _) => await ImportMaterialsAsync();
        var removeButton = MakeButton("Rimuovi materiale");
        removeButton.Click += async (_, _) => await RemoveSelectedMaterialAsync();
        var saveButton = MakeButton("Salva");
        saveButton.Click += async (_, _) => await SaveCurrentAsync();

        _status = new TextBlock
        {
            Text = "Pronto. I documenti testuali vengono anche trasformati in una prima struttura Document/Part/Chapter/Section.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 960
        };

        _materialsList = new ListBox { Width = 960, Height = 125 };
        _materialsList.SelectionChanged += (_, _) => ShowSelectedMaterial();

        _structureList = new ListBox { Width = 960, Height = 175 };
        _structureList.SelectionChanged += (_, _) => ShowSelectedContentNode();

        _preview = new TextBox
        {
            Width = 960,
            Height = 185,
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Seleziona un materiale o un elemento della struttura."
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { newButton, openButton, importButton, removeButton, saveButton }
        };

        Content = new Border
        {
            Padding = new Thickness(26),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10,
                Children =
                {
                    logo,
                    title,
                    subtitle,
                    buttons,
                    _status,
                    MakeSectionLabel("Materiali incorporati nel progetto"),
                    _materialsList,
                    MakeSectionLabel("Struttura editoriale rilevata"),
                    _structureList,
                    MakeSectionLabel("Dettaglio / anteprima"),
                    _preview
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(startupProjectPath))
        {
            Opened += async (_, _) =>
            {
                if (File.Exists(startupProjectPath)) await OpenProjectPathAsync(startupProjectPath);
            };
        }
    }

    private static Button MakeButton(string text) => new()
    {
        Content = text,
        Width = 170,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static TextBlock MakeSectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 17,
        HorizontalAlignment = HorizontalAlignment.Left,
        Width = 960
    };

    private async Task CreateProjectAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Crea progetto Diez",
            SuggestedFileName = "NuovoProgetto.diez",
            DefaultExtension = "diez",
            FileTypeChoices = [new FilePickerFileType("Progetto Diez") { Patterns = ["*.diez"] }]
        });
        if (file is null) return;

        try
        {
            _currentProjectPath = file.Path.LocalPath;
            _project = ProjectFileStore.Create(Path.GetFileNameWithoutExtension(_currentProjectPath));
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews();
            _status.Text = $"Creato pacchetto .diez: {_currentProjectPath}";
        }
        catch (Exception ex) { _status.Text = $"Errore creazione: {ex.Message}"; }
    }

    private async Task OpenProjectAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Apri progetto Diez",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Progetto Diez") { Patterns = ["*.diez"] }]
        });
        var file = files.FirstOrDefault();
        if (file is not null) await OpenProjectPathAsync(file.Path.LocalPath);
    }

    private async Task OpenProjectPathAsync(string path)
    {
        try
        {
            var wasPackage = ProjectFileStore.IsPackageFile(path);
            var project = await ProjectFileStore.LoadAsync(path);
            _currentProjectPath = path;
            _project = project;
            RefreshViews();
            _status.Text = wasPackage
                ? $"Aperto: {project.Name} · {project.Materials.Count} materiali · {project.ContentNodes.Count} elementi editoriali"
                : $"Aperto progetto Preview 0.1: {project.Name}. Al prossimo Salva verrà convertito nel pacchetto .diez corrente.";
        }
        catch (Exception ex) { _status.Text = $"Errore apertura: {ex.Message}"; }
    }

    private async Task ImportMaterialsAsync()
    {
        if (_project is null || string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            _status.Text = "Prima crea o apri un progetto .diez.";
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importa materiali",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Materiali supportati") { Patterns = ["*.txt", "*.md", "*.csv", "*.xlsx", "*.docx", "*.odt", "*.rtf", "*.pdf", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"] },
                new FilePickerFileType("Documenti") { Patterns = ["*.txt", "*.md", "*.docx", "*.odt", "*.rtf", "*.pdf"] },
                new FilePickerFileType("Tabelle") { Patterns = ["*.csv", "*.xlsx"] },
                new FilePickerFileType("Immagini") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"] }
            ]
        });
        if (files.Count == 0) return;

        var imported = 0;
        var duplicates = 0;
        var editorialNodes = 0;
        var errors = new List<string>();
        MaterialEntry? lastImported = null;
        _status.Text = $"Analisi di {files.Count} materiali in corso...";

        foreach (var file in files)
        {
            try
            {
                var sourcePath = file.Path.LocalPath;
                var material = await MaterialImporter.ImportAsync(sourcePath);
                if (_project.Materials.Any(existing => string.Equals(existing.Sha256, material.Sha256, StringComparison.OrdinalIgnoreCase)))
                {
                    duplicates++;
                    continue;
                }

                material.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
                _project.Materials.Add(material);
                var nodes = ContentStructureAnalyzer.Analyze(material);
                _project.ContentNodes.AddRange(nodes);
                editorialNodes += nodes.Count;
                imported++;
                lastImported = material;
            }
            catch (Exception ex) { errors.Add($"{file.Name}: {ex.Message}"); }
        }

        try
        {
            if (imported > 0) await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews();
            if (lastImported is not null) _materialsList.SelectedIndex = _project.Materials.IndexOf(lastImported);

            var message = $"Importati {imported} materiali · creati {editorialNodes} elementi editoriali";
            if (duplicates > 0) message += $" · {duplicates} duplicati ignorati";
            if (errors.Count > 0) message += $" · {errors.Count} errori: {string.Join("; ", errors.Take(2))}";
            else if (imported > 0) message += " · originali incorporati nel .diez";
            _status.Text = message;
        }
        catch (Exception ex) { _status.Text = $"Errore durante il salvataggio del pacchetto: {ex.Message}"; }
    }

    private async Task RemoveSelectedMaterialAsync()
    {
        if (_project is null || string.IsNullOrWhiteSpace(_currentProjectPath) ||
            _materialsList.SelectedIndex < 0 || _materialsList.SelectedIndex >= _project.Materials.Count)
        {
            _status.Text = "Seleziona prima un materiale da rimuovere.";
            return;
        }

        var index = _materialsList.SelectedIndex;
        var removed = _project.Materials[index];
        var removedNodes = _project.ContentNodes.Where(n => n.MaterialId == removed.MaterialId).ToList();
        _project.Materials.RemoveAt(index);
        _project.ContentNodes.RemoveAll(n => n.MaterialId == removed.MaterialId);

        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews();
            _status.Text = $"Rimosso: {removed.FileName} e {removedNodes.Count} elementi editoriali collegati. La sorgente sul PC non è stata toccata.";
        }
        catch (Exception ex)
        {
            _project.Materials.Insert(index, removed);
            _project.ContentNodes.AddRange(removedNodes);
            RefreshViews();
            _status.Text = $"Rimozione annullata per errore di salvataggio: {ex.Message}";
        }
    }

    private async Task SaveCurrentAsync()
    {
        if (_project is null || string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            _status.Text = "Nessun progetto aperto. Usa Nuovo progetto o Apri .diez.";
            return;
        }

        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews();
            _status.Text = $"Salvato: {_currentProjectPath} · {_project.ContentNodes.Count} elementi editoriali";
        }
        catch (Exception ex) { _status.Text = $"Errore salvataggio: {ex.Message}"; }
    }

    private void RefreshViews()
    {
        _preview.Text = string.Empty;
        if (_project is null)
        {
            _materialsList.ItemsSource = null;
            _structureList.ItemsSource = null;
            return;
        }

        _materialsList.ItemsSource = _project.Materials
            .Select(m => $"{(m.IsEmbedded ? "●" : "○")}  {m.Kind}  ·  {m.FileName}  ·  {m.Summary}")
            .ToList();

        _structureList.ItemsSource = _project.ContentNodes
            .OrderBy(n => _project.Materials.FindIndex(m => m.MaterialId == n.MaterialId))
            .ThenBy(n => n.Ordinal)
            .Select(n => $"{NodePrefix(n.Kind)}  {n.Title}  ·  {n.SourceLocator}")
            .ToList();
    }

    private void ShowSelectedMaterial()
    {
        if (_project is null || _materialsList.SelectedIndex < 0 || _materialsList.SelectedIndex >= _project.Materials.Count) return;
        _structureList.SelectedIndex = -1;
        var material = _project.Materials[_materialsList.SelectedIndex];
        var shortHash = material.Sha256.Length > 16 ? material.Sha256[..16] : material.Sha256;
        _preview.Text =
            $"{material.FileName}\nTipo: {material.Kind}\nOrigine importazione: {material.SourcePath}\n" +
            $"Dimensione: {material.SizeBytes:N0} byte\nSHA-256: {shortHash}...\n" +
            $"Nel progetto: {(material.IsEmbedded ? "originale incorporato nel .diez" : "solo metadati") }\n" +
            $"Testo editoriale estratto: {(string.IsNullOrWhiteSpace(material.ExtractedText) ? "no" : "sì") }\n{material.Summary}\n\n{material.Preview}";
    }

    private void ShowSelectedContentNode()
    {
        if (_project is null || _structureList.SelectedIndex < 0) return;
        var ordered = _project.ContentNodes
            .OrderBy(n => _project.Materials.FindIndex(m => m.MaterialId == n.MaterialId))
            .ThenBy(n => n.Ordinal)
            .ToList();
        if (_structureList.SelectedIndex >= ordered.Count) return;
        _materialsList.SelectedIndex = -1;
        var node = ordered[_structureList.SelectedIndex];
        _preview.Text = $"{node.Kind}: {node.Title}\nProvenienza: {node.SourceLocator}\nOrdine: {node.Ordinal}\n\n{node.Body}";
    }

    private static string NodePrefix(string kind) => kind switch
    {
        "Document" => "DOC",
        "Part" => "PARTE",
        "Chapter" => "CAP",
        _ => "SEZ"
    };
}
