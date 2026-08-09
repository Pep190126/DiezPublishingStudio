using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class ImageCollectionDescriptionUi
{
    private static readonly HashSet<Window> Attached = [];

    public static void Attach(MainWindow mainWindow)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            foreach (var window in desktop.Windows.ToList())
            {
                if (window is AiImageBatchWindow && Attached.Add(window))
                    AttachToBatchWindow(window);
            }
        };
        mainWindow.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private static void AttachToBatchWindow(Window window)
    {
        window.Closed += (_, _) => Attached.Remove(window);
        if (!TryGetSession(window, out var project, out var projectPath)) return;
        if (!TryGetRootStack(window, out var root)) return;

        HideOldImagesOnlyExport(root);

        var selector = new ComboBox { Width = 390 };
        var selectedInfo = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var description = new TextBox
        {
            AcceptsReturn = true,
            Height = 230,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Watermark = "Descrizione dell'immagine. Può essere anche molto lunga: non viene abbreviata da Diez."
        };
        var layoutMode = new ComboBox
        {
            ItemsSource = new[]
            {
                ImageCollectionLayoutExportService.External,
                ImageCollectionLayoutExportService.Internal,
                ImageCollectionLayoutExportService.Both
            },
            SelectedIndex = 0,
            Width = 250
        };
        var includeDescriptions = new CheckBox
        {
            Content = "Allega anche le descrizioni alla raccolta",
            IsChecked = false
        };
        var descriptionFormat = new ComboBox
        {
            ItemsSource = new[]
            {
                ImageCollectionDescriptionService.DescriptionTxt,
                ImageCollectionDescriptionService.DescriptionDocx
            },
            SelectedIndex = 0,
            Width = 130
        };
        var descriptionFormatField = Field("Formato descrizione", descriptionFormat);
        var exportExplanation = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var save = Button("Salva descrizione", 155);
        var copy = Button("Copia descrizione", 155);
        var export = Button("Esporta", 150);

        var imageJobs = new List<AiProductionJob>();

        void UpdateExportChoice()
        {
            var mode = layoutMode.SelectedItem?.ToString() ?? ImageCollectionLayoutExportService.External;
            var hasExternalPart = !string.Equals(mode, ImageCollectionLayoutExportService.Internal, StringComparison.Ordinal);
            includeDescriptions.IsVisible = hasExternalPart;
            descriptionFormatField.IsVisible = hasExternalPart && includeDescriptions.IsChecked == true;
            var format = descriptionFormat.SelectedItem?.ToString() ?? ImageCollectionDescriptionService.DescriptionTxt;
            var descriptionExample = string.Equals(format, ImageCollectionDescriptionService.DescriptionDocx, StringComparison.Ordinal)
                ? "IMG-023.png + IMG-023.docx"
                : "IMG-023.png + IMG-023.txt";
            exportExplanation.Text = mode switch
            {
                ImageCollectionLayoutExportService.Internal =>
                    "Crea un DOCX di lavoro con le immagini approvate già inserite, una per pagina e senza trasformare gli originali conservati nel progetto.",
                ImageCollectionLayoutExportService.Both =>
                    $"Crea un unico ZIP con il DOCX per impaginazione interna e gli originali separati. Se alleghi le descrizioni, usa coppie come {descriptionExample}.",
                _ =>
                    $"Crea uno ZIP con le immagini originali separate. Le descrizioni sono facoltative; se le alleghi, usa coppie come {descriptionExample}."
            };
        }

        void RefreshJobs(string? keepCode = null)
        {
            imageJobs = project.AiProductionJobs
                .Where(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase))
                .OrderBy(j => ParseNumber(j.Code))
                .ThenBy(j => j.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            selector.ItemsSource = imageJobs.Select(j => $"{j.Code} — {j.Title}").ToList();
            var index = string.IsNullOrWhiteSpace(keepCode)
                ? (imageJobs.Count > 0 ? 0 : -1)
                : imageJobs.FindIndex(j => string.Equals(j.Code, keepCode, StringComparison.OrdinalIgnoreCase));
            selector.SelectedIndex = index >= 0 ? index : (imageJobs.Count > 0 ? 0 : -1);
        }

        void LoadSelected()
        {
            if (selector.SelectedIndex < 0 || selector.SelectedIndex >= imageJobs.Count)
            {
                selectedInfo.Text = "Non ci sono ancora immagini nella raccolta.";
                description.Text = string.Empty;
                return;
            }
            var job = imageJobs[selector.SelectedIndex];
            var hasImage = job.ResultMaterialId.HasValue ? "immagine presente" : "immagine non ancora ricevuta";
            selectedInfo.Text = $"{job.Code} — {job.Title} · {hasImage} · {job.Status}";
            description.Text = ImageCollectionDescriptionService.GetDescription(job);
        }

        async Task SaveSelectedAsync(bool report)
        {
            if (selector.SelectedIndex < 0 || selector.SelectedIndex >= imageJobs.Count) return;
            var job = imageJobs[selector.SelectedIndex];
            ImageCollectionDescriptionService.SetDescription(job, description.Text);
            await ProjectFileStore.SaveAsync(projectPath, project);
            if (report) Report(window, status, $"Descrizione di {job.Code} salvata. Rimane legata a questa posizione della raccolta.");
        }

        selector.SelectionChanged += (_, _) => LoadSelected();
        layoutMode.SelectionChanged += (_, _) => UpdateExportChoice();
        includeDescriptions.IsCheckedChanged += (_, _) => UpdateExportChoice();
        descriptionFormat.SelectionChanged += (_, _) => UpdateExportChoice();
        save.Click += async (_, _) => await SaveSelectedAsync(true);
        copy.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
            if (clipboard is null)
            {
                Report(window, status, "Non riesco ad accedere agli appunti di Windows.");
                return;
            }
            await clipboard.SetTextAsync(description.Text ?? string.Empty);
            var code = selector.SelectedIndex >= 0 && selector.SelectedIndex < imageJobs.Count ? imageJobs[selector.SelectedIndex].Code : "immagine";
            Report(window, status, $"Descrizione di {code} copiata negli appunti.");
        };
        export.Click += async (_, _) =>
        {
            await SaveSelectedAsync(false);
            var mode = layoutMode.SelectedItem?.ToString() ?? ImageCollectionLayoutExportService.External;
            var internalOnly = string.Equals(mode, ImageCollectionLayoutExportService.Internal, StringComparison.Ordinal);
            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Esporta la raccolta",
                SuggestedFileName = ImageCollectionLayoutExportService.SuggestedName(project, mode),
                DefaultExtension = internalOnly ? "docx" : "zip",
                FileTypeChoices = internalOnly
                    ? [new FilePickerFileType("Documento Word DOCX") { Patterns = ["*.docx"] }]
                    : [new FilePickerFileType("Archivio ZIP") { Patterns = ["*.zip"] }]
            });
            if (file is null) return;
            var result = await ImageCollectionLayoutChoiceService.ExportAsync(
                project,
                projectPath,
                file.Path.LocalPath,
                mode,
                includeDescriptions.IsChecked == true && includeDescriptions.IsVisible,
                descriptionFormat.SelectedItem?.ToString() ?? ImageCollectionDescriptionService.DescriptionTxt);
            Report(window, status, result.Message);
        };

        ToolTip.SetTip(description, "Testo completo legato all'immagine selezionata. Puoi modificarlo, salvarlo e copiarlo senza limiti pratici di lunghezza imposti da Diez.");
        ToolTip.SetTip(copy, "Copia negli appunti tutta la descrizione dell'immagine selezionata.");
        ToolTip.SetTip(layoutMode, "Esterna mantiene gli originali separati; interna crea un DOCX di lavoro; entrambi prepara tutte e due le consegne.");
        ToolTip.SetTip(includeDescriptions, "Facoltativo per Coloring e raccolte immagini. Se lo attivi scegli sotto se creare TXT o DOCX con lo stesso nome base dell'immagine.");
        ToolTip.SetTip(descriptionFormat, "TXT crea IMG-023.txt; DOCX crea IMG-023.docx. Entrambi restano collegati all'originale IMG-023.* tramite lo stesso nome base.");
        ToolTip.SetTip(export, "Esporta secondo la modalità scelta senza usare il DOCX come unica copia delle immagini originali.");

        root.Children.Add(new Separator());
        root.Children.Add(new TextBlock { Text = "Scheda immagine e descrizione", FontSize = 20 });
        root.Children.Add(new TextBlock
        {
            Text = "La descrizione resta nel progetto anche se non la esporti. Puoi modificarla o copiarla in qualsiasi momento.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        root.Children.Add(Field("Immagine", selector));
        root.Children.Add(selectedInfo);
        root.Children.Add(Field("Descrizione completa", description));
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { save, copy }
        });
        root.Children.Add(new Separator());
        root.Children.Add(new TextBlock { Text = "Come vuoi impaginare?", FontSize = 20 });
        root.Children.Add(layoutMode);
        root.Children.Add(exportExplanation);
        root.Children.Add(includeDescriptions);
        root.Children.Add(descriptionFormatField);
        root.Children.Add(export);
        root.Children.Add(status);

        RefreshJobs();
        LoadSelected();
        UpdateExportChoice();
    }

    private static void HideOldImagesOnlyExport(Control control)
    {
        foreach (var child in Children(control))
        {
            if (child is Button button && string.Equals(button.Content?.ToString(), "ZIP immagini approvate", StringComparison.Ordinal))
            {
                button.IsVisible = false;
                ToolTip.SetTip(button, "Sostituito da Esporta, con scelta tra impaginazione esterna, interna o entrambe.");
            }
            HideOldImagesOnlyExport(child);
        }
    }

    private static IEnumerable<Control> Children(Control control)
    {
        if (control is Panel panel)
            foreach (var child in panel.Children) yield return child;
        if (control is Border border && border.Child is Control borderChild) yield return borderChild;
        if (control is ScrollViewer scroll && scroll.Content is Control scrollChild) yield return scrollChild;
        if (control is ContentControl content && content.Content is Control contentChild) yield return contentChild;
    }

    private static bool TryGetRootStack(Window window, out StackPanel root)
    {
        root = null!;
        if (window.Content is not Border border || border.Child is not ScrollViewer scroll || scroll.Content is not StackPanel stack)
            return false;
        root = stack;
        return true;
    }

    private static bool TryGetSession(Window window, out PreviewProject project, out string projectPath)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = window.GetType().GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        projectPath = window.GetType().GetField("_projectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(projectPath);
    }

    private static void Report(Window window, TextBlock localStatus, string message)
    {
        localStatus.Text = message;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        if (window.GetType().GetField("_status", flags)?.GetValue(window) is TextBlock existingStatus)
            existingStatus.Text = message;
        if (window.GetType().GetField("_mainStatus", flags)?.GetValue(window) is Action<string> mainStatus)
            mainStatus(message);
    }

    private static int ParseNumber(string? code)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : int.MaxValue;
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
