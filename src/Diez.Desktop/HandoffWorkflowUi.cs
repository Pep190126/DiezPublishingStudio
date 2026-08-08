using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

internal static class HandoffWorkflowUi
{
    private const string ProjectFieldName = "_project";
    private const string ProjectPathFieldName = "_currentProjectPath";
    private const string StatusFieldName = "_status";

    public static void Attach(MainWindow window)
    {
        window.Title = "Diez Publishing Studio — 0.13 Preview";

        if (window.Content is not Border border || border.Child is not StackPanel root)
            return;

        var subtitle = root.Children
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text?.StartsWith("Preview 0.", StringComparison.Ordinal) == true);
        if (subtitle is not null)
            subtitle.Text = "Preview 0.13 — Handoff editabile: DOCX + CSV/XLSX + ZIP immagini originali";

        var projectButtons = root.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Button>().Any(button =>
                                         string.Equals(button.Content?.ToString(), "Edizione / Preflight", StringComparison.Ordinal)));
        if (projectButtons is null) return;
        if (projectButtons.Children.OfType<Button>().Any(button =>
                string.Equals(button.Content?.ToString(), "Export / Handoff", StringComparison.Ordinal)))
            return;

        var handoffButton = new Button
        {
            Content = "Export / Handoff",
            Width = 150,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        handoffButton.Click += async (_, _) => await OpenAsync(window);
        projectButtons.Children.Add(handoffButton);
    }

    private static async Task OpenAsync(MainWindow window)
    {
        if (!TryGetSession(window, out var project, out var projectPath))
        {
            SetMainStatus(window, "Prima crea o apri un progetto .diez per usare gli export di handoff.");
            return;
        }

        var dialog = new HandoffWindow(window, project, projectPath, message => SetMainStatus(window, message));
        await dialog.ShowDialog(window);
    }

    private static bool TryGetSession(MainWindow window, out PreviewProject project, out string projectPath)
    {
        project = null!;
        projectPath = string.Empty;

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var projectField = typeof(MainWindow).GetField(ProjectFieldName, flags);
        var pathField = typeof(MainWindow).GetField(ProjectPathFieldName, flags);
        if (projectField?.GetValue(window) is not PreviewProject currentProject) return false;
        if (pathField?.GetValue(window) is not string currentPath || string.IsNullOrWhiteSpace(currentPath)) return false;

        project = currentProject;
        projectPath = currentPath;
        return true;
    }

    private static void SetMainStatus(MainWindow window, string message)
    {
        var statusField = typeof(MainWindow).GetField(StatusFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (statusField?.GetValue(window) is TextBlock status) status.Text = message;
    }
}

internal sealed class HandoffWindow : Window
{
    private readonly MainWindow _owner;
    private readonly PreviewProject _project;
    private readonly string _projectPath;
    private readonly Action<string> _setMainStatus;
    private readonly TextBlock _status;

    public HandoffWindow(MainWindow owner, PreviewProject project, string projectPath, Action<string> setMainStatus)
    {
        _owner = owner;
        _project = project;
        _projectPath = projectPath;
        _setMainStatus = setMainStatus;

        Title = "Export / Handoff editabile";
        Width = 700;
        Height = 390;
        MinWidth = 620;
        MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var title = new TextBlock
        {
            Text = "Consegna il progetto senza bloccarlo in un formato finale",
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var explanation = new TextBlock
        {
            Text = "DOCX, CSV e XLSX richiedono un Publication Candidate corrente. Lo ZIP immagini copia invece gli originali incorporati nel .diez, senza ridimensionare, ricomprimere o aggiungere file accessori.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 620,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var docx = MakeButton("DOCX editoriale");
        docx.Click += async (_, _) => await ExportDocxAsync();
        var csv = MakeButton("CSV Master");
        csv.Click += async (_, _) => await ExportCsvAsync();
        var xlsx = MakeButton("XLSX Master");
        xlsx.Click += async (_, _) => await ExportXlsxAsync();
        var images = MakeButton("ZIP immagini originali");
        images.Click += async (_, _) => await ExportImagesAsync();

        _status = new TextBlock
        {
            Text = BuildReadinessText(),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 620,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var close = new Button
        {
            Content = "Chiudi",
            Width = 120,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        close.Click += (_, _) => Close();

        Content = new Border
        {
            Padding = new Thickness(22),
            Child = new StackPanel
            {
                Spacing = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    title,
                    explanation,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { docx, csv, xlsx }
                    },
                    images,
                    _status,
                    close
                }
            }
        };
    }

    private string BuildReadinessText()
    {
        var preflight = EditionFreezeService.RunPreflight(_project);
        var candidate = PublicationCandidateService.IsLatestCandidateCurrent(_project);
        var images = _project.Materials.Count(m => m.Kind.StartsWith("Immagine", StringComparison.OrdinalIgnoreCase));
        var editorial = preflight.Ready && candidate ? "handoff editoriale pronto" : "handoff editoriale da finalizzare in Edizione / Preflight";
        return $"Stato: {editorial} · {images} immagini originali disponibili nel progetto.";
    }

    private static Button MakeButton(string text) => new()
    {
        Content = text,
        Width = 175,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private async Task ExportDocxAsync()
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta DOCX editoriale",
            SuggestedFileName = DocxExportService.SuggestedFileName(_project),
            DefaultExtension = "docx",
            FileTypeChoices = [new FilePickerFileType("Documento Word DOCX") { Patterns = ["*.docx"] }]
        });
        if (file is null) return;

        try
        {
            var result = await DocxExportService.ExportAsync(_project, file.Path.LocalPath);
            Report(result.Message);
        }
        catch (Exception ex) { Report($"Esportazione DOCX fallita: {ex.Message}"); }
    }

    private async Task ExportCsvAsync()
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta Master CSV",
            SuggestedFileName = HandoffExportService.SuggestedCsvFileName(_project),
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV UTF-8") { Patterns = ["*.csv"] }]
        });
        if (file is null) return;

        try
        {
            var result = await HandoffExportService.ExportMasterCsvAsync(_project, file.Path.LocalPath);
            Report(result.Message);
        }
        catch (Exception ex) { Report($"Esportazione CSV fallita: {ex.Message}"); }
    }

    private async Task ExportXlsxAsync()
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta Master XLSX",
            SuggestedFileName = HandoffExportService.SuggestedXlsxFileName(_project),
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("Cartella di lavoro Excel XLSX") { Patterns = ["*.xlsx"] }]
        });
        if (file is null) return;

        try
        {
            var result = await HandoffExportService.ExportMasterXlsxAsync(_project, file.Path.LocalPath);
            Report(result.Message);
        }
        catch (Exception ex) { Report($"Esportazione XLSX fallita: {ex.Message}"); }
    }

    private async Task ExportImagesAsync()
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta immagini originali",
            SuggestedFileName = HandoffExportService.SuggestedImageZipFileName(_project),
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("Archivio ZIP immagini") { Patterns = ["*.zip"] }]
        });
        if (file is null) return;

        try
        {
            var result = await HandoffExportService.ExportOriginalImagesZipAsync(_project, _projectPath, file.Path.LocalPath);
            Report(result.Message);
        }
        catch (Exception ex) { Report($"Esportazione immagini fallita: {ex.Message}"); }
    }

    private void Report(string message)
    {
        _status.Text = message;
        _setMainStatus(message);
    }
}
