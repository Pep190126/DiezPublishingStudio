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
        window.Title = ProductInfo.WindowTitle;

        if (window.Content is not Border border || border.Child is not StackPanel root)
            return;

        var subtitle = root.Children
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text?.StartsWith("Preview 0.", StringComparison.Ordinal) == true ||
                                 t.Text?.StartsWith("Pre-finale", StringComparison.Ordinal) == true);
        if (subtitle is not null)
            subtitle.Text = ProductInfo.Subtitle;

        var projectButtons = root.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Button>().Any(button =>
                                         string.Equals(button.Content?.ToString(), "Edizione / Preflight", StringComparison.Ordinal)));
        if (projectButtons is null) return;

        foreach (var button in projectButtons.Children.OfType<Button>())
            button.Width = 135;

        if (projectButtons.Children.OfType<Button>().Any(button =>
                string.Equals(button.Content?.ToString(), "Export / Handoff", StringComparison.Ordinal)))
            return;

        var handoffButton = new Button
        {
            Content = "Export / Handoff",
            Width = 135,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        handoffButton.Click += async (_, _) => await OpenAsync(window);
        projectButtons.Children.Add(handoffButton);
    }

    private static async Task OpenAsync(MainWindow window)
    {
        if (!TryGetSession(window, out var project, out var projectPath))
        {
            SetMainStatus(window, "Prima crea o apri un progetto .diez per esportare o consegnare il lavoro.");
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

        Title = $"Esporta / Consegna — Diez {ProductInfo.DisplayVersion}";
        Width = 780;
        Height = 545;
        MinWidth = 680;
        MinHeight = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var title = new TextBlock
        {
            Text = "Scegli cosa vuoi portare fuori da Diez",
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var explanation = new TextBlock
        {
            Text = "Puoi creare un singolo file modificabile oppure un pacchetto completo da dare a Word, Publisher, Excel, Canva o a un impaginatore. Diez non blocca il lavoro in un PDF o EPUB finale.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 690,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var docx = MakeButton("Documento Word (DOCX)");
        docx.Click += async (_, _) => await ExportDocxAsync();
        SetHelp(docx, "Crea il manoscritto modificabile per Word, Publisher o un impaginatore. Se hai pianificato immagini, le inserisce anche nel documento.");

        var csv = MakeButton("Tabella CSV");
        csv.Click += async (_, _) => await ExportCsvAsync();
        SetHelp(csv, "Esporta il testo di lavoro in una tabella CSV semplice e riutilizzabile.");

        var xlsx = MakeButton("Tabella Excel (XLSX)");
        xlsx.Click += async (_, _) => await ExportXlsxAsync();
        SetHelp(xlsx, "Esporta il testo di lavoro in un vero file Excel modificabile.");

        var plan = MakeButton("Posizione immagini");
        plan.Click += async (_, _) => await OpenIllustrationPlanAsync();
        SetHelp(plan, "Per i libri illustrati: indica a quale capitolo appartiene ogni immagine, dove dovrebbe comparire e la sua didascalia.");

        var images = MakeButton("Solo immagini (ZIP)");
        images.Click += async (_, _) => await ExportImagesAsync();
        SetHelp(images, "Crea uno ZIP con soltanto le immagini originali. È l'uscita principale per i coloring book e per consegnare gli asset separati.");

        var production = new Button
        {
            Content = "Pacchetto completo per impaginatore",
            Width = 310,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        production.Click += async (_, _) => await ExportProductionPackageAsync();
        SetHelp(production, "Raccoglie in un unico ZIP DOCX, CSV/XLSX, immagini originali, dati del libro, piano immagini e controllo di integrità.");

        _status = new TextBlock
        {
            Text = BuildReadinessText(),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 690,
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
                Spacing = 13,
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
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { plan, images }
                    },
                    production,
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
        var images = _project.Materials.Count(IllustrationPlanService.IsImage);
        var editorial = preflight.Ready && candidate
            ? "progetto approvato: gli export controllati e il pacchetto completo sono disponibili"
            : "prima completa Prepara consegna per gli export controllati; lo ZIP di sole immagini resta disponibile";
        return $"Stato: {editorial} · {images} immagini originali · {_project.IllustrationPlacements.Count} posizioni immagine definite.";
    }

    private static Button MakeButton(string text) => new()
    {
        Content = text,
        Width = 205,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static void SetHelp(Control control, string text) => ToolTip.SetTip(control, text);

    private async Task OpenIllustrationPlanAsync()
    {
        var dialog = new IllustrationPlanWindow(_project, _projectPath);
        await dialog.ShowDialog(this);
        var status = BuildReadinessText();
        _status.Text = status;
        _setMainStatus(status);
    }

    private async Task ExportDocxAsync()
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Crea documento Word modificabile",
            SuggestedFileName = DocxExportService.SuggestedFileName(_project),
            DefaultExtension = "docx",
            FileTypeChoices = [new FilePickerFileType("Documento Word DOCX") { Patterns = ["*.docx"] }]
        });
        if (file is null) return;

        try
        {
            var result = await DocxExportService.ExportAsync(_project, _projectPath, file.Path.LocalPath);
            Report(result.Message);
        }
        catch (Exception ex) { Report($"Esportazione DOCX fallita: {ex.Message}"); }
    }

    private async Task ExportCsvAsync()
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Crea tabella CSV",
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
            Title = "Crea tabella Excel",
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
            Title = "Crea ZIP con le sole immagini originali",
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

    private async Task ExportProductionPackageAsync()
    {
        if (!PublicationCandidateService.IsLatestCandidateCurrent(_project))
        {
            Report("Pacchetto completo bloccato: prima usa Prepara consegna e approva la versione che vuoi esportare.");
            return;
        }

        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Crea pacchetto completo per impaginazione",
            SuggestedFileName = ProductionPackageService.SuggestedFileName(_project),
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("Pacchetto completo Diez ZIP") { Patterns = ["*.zip"] }]
        });
        if (file is null) return;

        try
        {
            var result = await ProductionPackageService.ExportAsync(_project, _projectPath, file.Path.LocalPath);
            Report(result.Message);
        }
        catch (Exception ex) { Report($"Creazione pacchetto completo fallita: {ex.Message}"); }
    }

    private void Report(string message)
    {
        _status.Text = message;
        _setMainStatus(message);
    }
}
