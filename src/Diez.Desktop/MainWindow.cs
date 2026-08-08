using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

public sealed class MainWindow : Window
{
    private readonly TextBlock _status;
    private readonly ListBox _materialsList;
    private readonly TextBox _preview;
    private string? _currentProjectPath;
    private PreviewProject? _project;

    public MainWindow(string? startupProjectPath = null)
    {
        Title = "Diez Publishing Studio — 0.2 Preview";
        Width = 1120;
        Height = 790;
        MinWidth = 860;
        MinHeight = 640;

        var logo = new TextBlock
        {
            Text = "∞",
            FontSize = 44,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var title = new TextBlock
        {
            Text = "Diez Publishing Studio",
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var subtitle = new TextBlock
        {
            Text = "Preview 0.2 — pacchetto .diez reale + Intake documenti e immagini",
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
            Text = "Pronto. Supportati TXT, MD, CSV, XLSX, DOCX, ODT, RTF, PDF e immagini comuni.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 940
        };

        _materialsList = new ListBox
        {
            Width = 940,
            Height = 165
        };
        _materialsList.SelectionChanged += (_, _) => ShowSelectedMaterial();

        _preview = new TextBox
        {
            Width = 940,
            Height = 230,
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Seleziona un materiale per vedere l'anteprima."
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { newButton, openButton, importButton, removeButton, saveButton }
        };

        var materialsLabel = new TextBlock
        {
            Text = "Materiali incorporati nel progetto",
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 940
        };

        var previewLabel = new TextBlock
        {
            Text = "Anteprima Intake",
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 940
        };

        Content = new Border
        {
            Padding = new Thickness(28),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 11,
                Children =
                {
                    logo,
                    title,
                    subtitle,
                    buttons,
                    _status,
                    materialsLabel,
                    _materialsList,
                    previewLabel,
                    _preview
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(startupProjectPath))
        {
            Opened += async (_, _) =>
            {
                if (File.Exists(startupProjectPath))
                    await OpenProjectPathAsync(startupProjectPath);
            };
        }
    }

    private static Button MakeButton(string text) => new()
    {
        Content = text,
        Width = 170,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private async Task CreateProjectAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Crea progetto Diez",
            SuggestedFileName = "NuovoProgetto.diez",
            DefaultExtension = "diez",
            FileTypeChoices =
            [
                new FilePickerFileType("Progetto Diez") { Patterns = ["*.diez"] }
            ]
        });

        if (file is null) return;

        try
        {
            _currentProjectPath = file.Path.LocalPath;
            _project = ProjectFileStore.Create(Path.GetFileNameWithoutExtension(_currentProjectPath));
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshMaterials();
            _status.Text = $"Creato pacchetto .diez: {_currentProjectPath}";
        }
        catch (Exception ex)
        {
            _status.Text = $"Errore creazione: {ex.Message}";
        }
    }

    private async Task OpenProjectAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Apri progetto Diez",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Progetto Diez") { Patterns = ["*.diez"] }
            ]
        });

        var file = files.FirstOrDefault();
        if (file is null) return;
        await OpenProjectPathAsync(file.Path.LocalPath);
    }

    private async Task OpenProjectPathAsync(string path)
    {
        try
        {
            var wasPackage = ProjectFileStore.IsPackageFile(path);
            var project = await ProjectFileStore.LoadAsync(path);
            _currentProjectPath = path;
            _project = project;
            RefreshMaterials();
            _status.Text = wasPackage
                ? $"Aperto: {project.Name} · {project.Materials.Count} materiali · ultimo salvataggio {project.SavedAtLocal}"
                : $"Aperto progetto Preview 0.1: {project.Name}. Al prossimo Salva verrà convertito automaticamente nel nuovo pacchetto .diez.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Errore apertura: {ex.Message}";
        }
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
                new FilePickerFileType("Materiali supportati")
                {
                    Patterns = ["*.txt", "*.md", "*.csv", "*.xlsx", "*.docx", "*.odt", "*.rtf", "*.pdf", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"]
                },
                new FilePickerFileType("Documenti") { Patterns = ["*.txt", "*.md", "*.docx", "*.odt", "*.rtf", "*.pdf"] },
                new FilePickerFileType("Tabelle") { Patterns = ["*.csv", "*.xlsx"] },
                new FilePickerFileType("Immagini") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"] }
            ]
        });

        if (files.Count == 0) return;

        var imported = 0;
        var duplicates = 0;
        var errors = new List<string>();
        MaterialEntry? lastImported = null;

        _status.Text = $"Analisi di {files.Count} materiali in corso...";

        foreach (var file in files)
        {
            try
            {
                var material = await MaterialImporter.ImportAsync(file.Path.LocalPath);
                if (_project.Materials.Any(existing =>
                        string.Equals(existing.Sha256, material.Sha256, StringComparison.OrdinalIgnoreCase)))
                {
                    duplicates++;
                    continue;
                }

                _project.Materials.Add(material);
                imported++;
                lastImported = material;
            }
            catch (Exception ex)
            {
                errors.Add($"{file.Name}: {ex.Message}");
            }
        }

        try
        {
            if (imported > 0)
                await ProjectFileStore.SaveAsync(_currentProjectPath, _project);

            RefreshMaterials();
            if (lastImported is not null)
                _materialsList.SelectedIndex = _project.Materials.IndexOf(lastImported);

            var message = $"Importati {imported} materiali";
            if (duplicates > 0) message += $" · {duplicates} duplicati ignorati";
            if (errors.Count > 0) message += $" · {errors.Count} errori: {string.Join("; ", errors.Take(2))}";
            else if (imported > 0) message += " · originali incorporati nel .diez";
            _status.Text = message;
        }
        catch (Exception ex)
        {
            _status.Text = $"Errore durante il salvataggio del pacchetto: {ex.Message}";
        }
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
        _project.Materials.RemoveAt(index);

        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshMaterials();
            _status.Text = $"Rimosso dal progetto: {removed.FileName}. Il file sorgente originale sul PC non è stato toccato.";
        }
        catch (Exception ex)
        {
            _project.Materials.Insert(index, removed);
            RefreshMaterials();
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
            RefreshMaterials();
            _status.Text = $"Salvato pacchetto .diez: {_currentProjectPath}";
        }
        catch (Exception ex)
        {
            _status.Text = $"Errore salvataggio: {ex.Message}";
        }
    }

    private void RefreshMaterials()
    {
        _preview.Text = string.Empty;
        if (_project is null)
        {
            _materialsList.ItemsSource = null;
            return;
        }

        _materialsList.ItemsSource = _project.Materials
            .Select(m => $"{(m.IsEmbedded ? "●" : "○")}  {m.Kind}  ·  {m.FileName}  ·  {m.Summary}")
            .ToList();
    }

    private void ShowSelectedMaterial()
    {
        if (_project is null || _materialsList.SelectedIndex < 0 || _materialsList.SelectedIndex >= _project.Materials.Count)
        {
            _preview.Text = string.Empty;
            return;
        }

        var material = _project.Materials[_materialsList.SelectedIndex];
        var shortHash = material.Sha256.Length > 16 ? material.Sha256[..16] : material.Sha256;
        _preview.Text =
            $"{material.FileName}\n" +
            $"Tipo: {material.Kind}\n" +
            $"Origine importazione: {material.SourcePath}\n" +
            $"Dimensione: {material.SizeBytes:N0} byte\n" +
            $"SHA-256: {shortHash}...\n" +
            $"Nel progetto: {(material.IsEmbedded ? "originale incorporato nel .diez" : "solo metadati — sorgente non disponibile al salvataggio")}\n" +
            $"{material.Summary}\n\n" +
            material.Preview;
    }
}
