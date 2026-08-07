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
        Title = "Diez Publishing Studio — 0.1 Preview";
        Width = 1100;
        Height = 760;
        MinWidth = 820;
        MinHeight = 620;

        var logo = new TextBlock
        {
            Text = "∞",
            FontSize = 48,
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
            Text = "Preview 0.1 — progetto .diez + primo Intake materiali",
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var newButton = MakeButton("Nuovo progetto");
        newButton.Click += async (_, _) => await CreateProjectAsync();

        var openButton = MakeButton("Apri progetto .diez");
        openButton.Click += async (_, _) => await OpenProjectAsync();

        var importButton = MakeButton("Importa materiale");
        importButton.Click += async (_, _) => await ImportMaterialAsync();

        var saveButton = MakeButton("Salva");
        saveButton.Click += async (_, _) => await SaveCurrentAsync();

        _status = new TextBlock
        {
            Text = "Pronto. Crea o apri un progetto, poi importa TXT, Markdown, CSV o XLSX.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 900
        };

        _materialsList = new ListBox
        {
            Width = 900,
            Height = 160
        };
        _materialsList.SelectionChanged += (_, _) => ShowSelectedMaterial();

        _preview = new TextBox
        {
            Width = 900,
            Height = 210,
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
            Watermark = "Seleziona un materiale per vedere l'anteprima."
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { newButton, openButton, importButton, saveButton }
        };

        var materialsLabel = new TextBlock
        {
            Text = "Materiali del progetto",
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 900
        };

        var previewLabel = new TextBlock
        {
            Text = "Anteprima Intake",
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 900
        };

        Content = new Border
        {
            Padding = new Thickness(30),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 12,
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
        Width = 180,
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

        _currentProjectPath = file.Path.LocalPath;
        _project = ProjectFileStore.Create(Path.GetFileNameWithoutExtension(_currentProjectPath));
        await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
        RefreshMaterials();
        _status.Text = $"Creato e salvato: {_currentProjectPath}";
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
            var project = await ProjectFileStore.LoadAsync(path);
            _currentProjectPath = path;
            _project = project;
            RefreshMaterials();
            _status.Text = $"Aperto: {project.Name} · {project.Materials.Count} materiali · ultimo salvataggio {project.SavedAtLocal}";
        }
        catch (Exception ex)
        {
            _status.Text = $"Errore apertura: {ex.Message}";
        }
    }

    private async Task ImportMaterialAsync()
    {
        if (_project is null || string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            _status.Text = "Prima crea o apri un progetto .diez.";
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importa materiale",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Materiali supportati") { Patterns = ["*.txt", "*.md", "*.csv", "*.xlsx"] },
                new FilePickerFileType("Testo / Markdown") { Patterns = ["*.txt", "*.md"] },
                new FilePickerFileType("CSV") { Patterns = ["*.csv"] },
                new FilePickerFileType("Excel XLSX") { Patterns = ["*.xlsx"] }
            ]
        });

        var file = files.FirstOrDefault();
        if (file is null) return;

        try
        {
            _status.Text = "Analisi materiale in corso...";
            var material = await MaterialImporter.ImportAsync(file.Path.LocalPath);
            _project.Materials.Add(material);
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshMaterials();
            _materialsList.SelectedIndex = _project.Materials.Count - 1;
            _status.Text = $"Importato e salvato: {material.FileName} · {material.Summary}";
        }
        catch (Exception ex)
        {
            _status.Text = $"Errore importazione: {ex.Message}";
        }
    }

    private async Task SaveCurrentAsync()
    {
        if (_project is null || string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            _status.Text = "Nessun progetto aperto. Usa Nuovo progetto o Apri progetto .diez.";
            return;
        }

        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            _status.Text = $"Salvato: {_currentProjectPath}";
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
            .Select(m => $"{m.Kind}  ·  {m.FileName}  ·  {m.Summary}")
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
            $"Origine: {material.SourcePath}\n" +
            $"Dimensione: {material.SizeBytes:N0} byte\n" +
            $"SHA-256: {shortHash}...\n" +
            $"{material.Summary}\n\n" +
            material.Preview;
    }
}
