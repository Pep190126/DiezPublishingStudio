using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

public sealed class MainWindow : Window
{
    private readonly TextBlock _status;
    private string? _currentProjectPath;

    public MainWindow()
    {
        Title = "Diez Publishing Studio — Preview";
        Width = 1100;
        Height = 720;
        MinWidth = 760;
        MinHeight = 520;

        var logo = new TextBlock
        {
            Text = "∞",
            FontSize = 58,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var title = new TextBlock
        {
            Text = "Diez Publishing Studio",
            FontSize = 30,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var subtitle = new TextBlock
        {
            Text = "Prima build installabile — verifica avvio, progetto .diez e salvataggio",
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var newButton = new Button
        {
            Content = "Nuovo progetto",
            Width = 180,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        newButton.Click += async (_, _) => await CreateProjectAsync();

        var openButton = new Button
        {
            Content = "Apri progetto .diez",
            Width = 180,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        openButton.Click += async (_, _) => await OpenProjectAsync();

        var saveButton = new Button
        {
            Content = "Salva",
            Width = 180,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        saveButton.Click += async (_, _) => await SaveCurrentAsync();

        _status = new TextBlock
        {
            Text = "Pronto. Questa preview serve prima di tutto a verificare l'installer sul tuo PC.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { newButton, openButton, saveButton }
        };

        Content = new Border
        {
            Padding = new Thickness(40),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 18,
                Children = { logo, title, subtitle, buttons, _status }
            }
        };
    }

    private async Task CreateProjectAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Crea progetto Diez",
            SuggestedFileName = "NuovoProgetto.diez",
            DefaultExtension = "diez",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Progetto Diez") { Patterns = new[] { "*.diez" } }
            }
        });

        if (file is null) return;

        _currentProjectPath = file.Path.LocalPath;
        await WriteProjectAsync(_currentProjectPath, Path.GetFileNameWithoutExtension(_currentProjectPath));
        _status.Text = $"Creato e salvato: {_currentProjectPath}";
    }

    private async Task OpenProjectAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Apri progetto Diez",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Progetto Diez") { Patterns = new[] { "*.diez" } }
            }
        });

        var file = files.FirstOrDefault();
        if (file is null) return;

        try
        {
            _currentProjectPath = file.Path.LocalPath;
            var json = await File.ReadAllTextAsync(_currentProjectPath);
            var project = JsonSerializer.Deserialize<PreviewProject>(json);
            _status.Text = project is null
                ? "Il file non contiene un progetto preview valido."
                : $"Aperto: {project.Name} — ultimo salvataggio {project.SavedAtLocal}";
        }
        catch (Exception ex)
        {
            _status.Text = $"Errore apertura: {ex.Message}";
        }
    }

    private async Task SaveCurrentAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            _status.Text = "Nessun progetto aperto. Usa Nuovo progetto o Apri progetto .diez.";
            return;
        }

        await WriteProjectAsync(_currentProjectPath, Path.GetFileNameWithoutExtension(_currentProjectPath));
        _status.Text = $"Salvato: {_currentProjectPath}";
    }

    private static async Task WriteProjectAsync(string path, string name)
    {
        var project = new PreviewProject(
            "diez-project-preview",
            1,
            name,
            DateTimeOffset.Now.ToString("G"),
            Guid.NewGuid());

        var json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private sealed record PreviewProject(
        string Format,
        int SchemaVersion,
        string Name,
        string SavedAtLocal,
        Guid ProjectId);
}
