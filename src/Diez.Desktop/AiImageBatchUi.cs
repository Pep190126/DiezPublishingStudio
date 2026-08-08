using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

internal static class AiImageBatchUi
{
    public static void Attach(MainWindow window)
    {
        if (window.Content is not Border border || border.Child is not StackPanel root) return;
        var projectButtons = root.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.Ordinal)));
        if (projectButtons is null) return;
        if (projectButtons.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Serie immagini AI", StringComparison.Ordinal))) return;

        var button = new Button
        {
            Content = "Serie immagini AI",
            Width = 155,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ToolTip.SetTip(button, "Prepara molte immagini in una volta, crea un unico XLSX per l'AI e importa uno o più ZIP senza perdere l'ordine IMG-###.");
        button.Click += async (_, _) =>
        {
            if (!TryGetSession(window, out var project, out var path))
            {
                SetStatus(window, "Prima crea o apri un progetto Diez.");
                return;
            }
            var dialog = new AiImageBatchWindow(project, path, message => SetStatus(window, message));
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

internal sealed class AiImageBatchWindow : Window
{
    private readonly PreviewProject _project;
    private readonly string _projectPath;
    private readonly Action<string> _mainStatus;
    private readonly TextBox _count;
    private readonly TextBox _theme;
    private readonly TextBox _titlePrefix;
    private readonly ComboBox _provider;
    private readonly CheckBox _bestModel;
    private readonly CheckBox _onlyMissing;
    private readonly TextBlock _summary;
    private readonly TextBlock _status;

    public AiImageBatchWindow(PreviewProject project, string projectPath, Action<string> mainStatus)
    {
        _project = project;
        _projectPath = projectPath;
        _mainStatus = mainStatus;

        Title = "Serie di immagini con AI";
        Width = 940;
        Height = 720;
        MinWidth = 780;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _count = new TextBox { Text = "50", Width = 100 };
        _theme = new TextBox
        {
            AcceptsReturn = true,
            Height = 110,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Es. 50 pagine da colorare nostalgiche, line art pulita, soggetti diversi, niente testo nell'immagine."
        };
        _titlePrefix = new TextBox { Text = "Immagine", Width = 250 };
        _provider = new ComboBox
        {
            ItemsSource = new[] { AiImageBatchService.ProviderOpenAi, AiImageBatchService.ProviderGemini, AiImageBatchService.ProviderOther },
            SelectedIndex = 0,
            Width = 230
        };
        _bestModel = new CheckBox
        {
            Content = "Chiedi il modello immagini più avanzato disponibile",
            IsChecked = true
        };
        _onlyMissing = new CheckBox
        {
            Content = "Nel prossimo XLSX metti solo immagini mancanti o da rifare",
            IsChecked = true
        };
        _summary = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 16 };
        _status = new TextBlock
        {
            Text = "Puoi creare una serie, consegnarla all'AI in un solo XLSX e riportare i risultati con uno o più ZIP. Diez ricompone tutto per ID.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var createSeries = MakeButton("Crea la serie", 145);
        var exportPack = MakeButton("Crea XLSX per l'AI", 175);
        var copyPhrase = MakeButton("Copia frase per la chat", 190);
        var importZip = MakeButton("Importa ZIP ricevuto", 185);
        var exportApproved = MakeButton("ZIP immagini approvate", 190);

        createSeries.Click += async (_, _) => await CreateSeriesAsync();
        exportPack.Click += async (_, _) => await ExportPackAsync();
        copyPhrase.Click += async (_, _) => await CopyPhraseAsync();
        importZip.Click += async (_, _) => await ImportZipAsync();
        exportApproved.Click += async (_, _) => await ExportApprovedAsync();

        Help(createSeries, "Crea tanti elementi IMG-### in una volta. Ognuno mantiene per sempre il proprio ID e la propria posizione logica.");
        Help(exportPack, "Crea un unico XLSX con istruzioni generali e una riga per ogni immagine da generare.");
        Help(copyPhrase, "Copia una frase breve da usare quando alleghi l'XLSX a una chat AI.");
        Help(importZip, "Puoi importare anche ZIP parziali. Se oggi arrivano 97 immagini e domani 3, Diez riempie i buchi usando gli ID.");
        Help(exportApproved, "Esporta solo le immagini approvate, ordinate con nomi IMG-001, IMG-002 e così via. Lo ZIP contiene solo immagini.");
        Help(_bestModel, "Nel file Diez scrive il nome del modello più avanzato noto per il servizio scelto e aggiunge una alternativa se quel modello non è disponibile.");
        Help(_onlyMissing, "Utile per rettifiche: dopo un primo ZIP, il nuovo XLSX contiene soltanto ciò che manca o hai segnato da rifare.");

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = "Prepara molte immagini senza fare il fattorino tra Diez e l'AI", FontSize = 23, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        new TextBlock
                        {
                            Text = "La sequenza dipende dagli ID IMG-###, non dall'ordine in cui ricevi o importi i file.",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        Field("Quante immagini vuoi preparare?", _count),
                        Field("Nome breve della serie", _titlePrefix),
                        Field("Descrivi tema e regole comuni", _theme),
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 10,
                            Children = { createSeries }
                        },
                        new Separator(),
                        new TextBlock { Text = "Prepara il pacchetto da dare all'AI", FontSize = 20 },
                        Field("Quale AI pensi di usare?", _provider),
                        _bestModel,
                        _onlyMissing,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            Children = { exportPack, copyPhrase }
                        },
                        new Separator(),
                        new TextBlock { Text = "Riporta i risultati nel progetto", FontSize = 20 },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            Children = { importZip, exportApproved }
                        },
                        _summary,
                        _status
                    }
                }
            }
        };

        RefreshSummary();
    }

    private async Task CreateSeriesAsync()
    {
        if (!int.TryParse((_count.Text ?? string.Empty).Trim(), out var count) || count < 1 || count > 500)
        {
            Report("Inserisci un numero di immagini da 1 a 500.");
            return;
        }
        var theme = (_theme.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(theme))
        {
            Report("Descrivi prima il tema e le regole comuni della serie.");
            return;
        }
        var created = AiImageBatchService.CreateImageSeries(_project, count, theme, _titlePrefix.Text ?? "Immagine");
        await ProjectFileStore.SaveAsync(_projectPath, _project);
        RefreshSummary();
        Report($"Create {created.Count} immagini con ID stabili. Ora puoi creare un solo XLSX per l'AI.");
    }

    private async Task ExportPackAsync()
    {
        var onlyMissing = _onlyMissing.IsChecked == true;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salva il pacchetto da dare all'AI",
            SuggestedFileName = AiImageBatchService.SuggestedPackName(_project, onlyMissing),
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("Excel XLSX") { Patterns = ["*.xlsx"] }]
        });
        if (file is null) return;
        var provider = _provider.SelectedItem?.ToString() ?? AiImageBatchService.ProviderOther;
        var result = await AiImageBatchService.ExportPackXlsxAsync(_project, file.Path.LocalPath, provider, _bestModel.IsChecked == true, onlyMissing);
        Report(result.Message);
    }

    private async Task CopyPhraseAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            Report("Non riesco ad accedere agli appunti di Windows.");
            return;
        }
        await clipboard.SetTextAsync(AiImageBatchService.ChatInstruction);
        Report("Frase copiata. Allega l'XLSX alla chat AI e incolla questa frase.");
    }

    private async Task ImportZipAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importa uno ZIP ricevuto dall'AI",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Archivio ZIP") { Patterns = ["*.zip"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;
        var result = await AiImageBatchService.ImportResultZipAsync(_project, _projectPath, file.Path.LocalPath);
        RefreshSummary();
        Report(result.Message);
    }

    private async Task ExportApprovedAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta solo le immagini approvate",
            SuggestedFileName = AiImageBatchService.SuggestedApprovedZipName(_project),
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("Archivio ZIP") { Patterns = ["*.zip"] }]
        });
        if (file is null) return;
        var result = await AiImageBatchService.ExportApprovedImagesZipAsync(_project, _projectPath, file.Path.LocalPath);
        Report(result.Message);
    }

    private void RefreshSummary()
    {
        var all = _project.AiProductionJobs.Where(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase)).ToList();
        var received = all.Count(j => j.ResultMaterialId.HasValue);
        var approved = all.Count(j => string.Equals(j.Status, AiProductionService.StatusApproved, StringComparison.Ordinal));
        var missing = all.Count(j => !j.ResultMaterialId.HasValue || string.Equals(j.Status, AiProductionService.StatusNeedsRevision, StringComparison.Ordinal));
        _summary.Text = $"Serie nel progetto: {all.Count} · ricevute: {received} · approvate: {approved} · mancanti/da rifare: {missing}";
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

    private static StackPanel Field(string label, Control input) => new()
    {
        Spacing = 4,
        Children = { new TextBlock { Text = label }, input }
    };

    private static Button MakeButton(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };
}