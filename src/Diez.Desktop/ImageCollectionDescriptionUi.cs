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
        var includeDescriptions = new CheckBox
        {
            Content = "Allega anche le descrizioni alla raccolta",
            IsChecked = false
        };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var save = Button("Salva descrizione", 155);
        var copy = Button("Copia descrizione", 155);
        var export = Button("Esporta raccolta", 165);

        var imageJobs = new List<AiProductionJob>();

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
            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Esporta la raccolta approvata",
                SuggestedFileName = ImageCollectionDescriptionService.SuggestedCollectionZipName(project),
                DefaultExtension = "zip",
                FileTypeChoices = [new FilePickerFileType("Archivio ZIP") { Patterns = ["*.zip"] }]
            });
            if (file is null) return;
            var result = await ImageCollectionDescriptionService.ExportApprovedCollectionAsync(
                project, projectPath, file.Path.LocalPath, includeDescriptions.IsChecked == true);
            Report(window, status, result.Message);
        };

        ToolTip.SetTip(description, "Testo completo legato all'immagine selezionata. Puoi modificarlo, salvarlo e copiarlo senza limiti pratici di lunghezza imposti da Diez.");
        ToolTip.SetTip(copy, "Copia negli appunti tutta la descrizione dell'immagine selezionata.");
        ToolTip.SetTip(includeDescriptions, "Se selezionato, nello ZIP ogni immagine avrà un file .txt con lo stesso numero e lo stesso nome base: IMG-023.png + IMG-023.txt.");
        ToolTip.SetTip(export, "Esporta le immagini approvate. La casella sopra decide se allegare anche un file di descrizione gemello per ogni immagine.");

        root.Children.Add(new Separator());
        root.Children.Add(new TextBlock { Text = "Scheda immagine e descrizione", FontSize = 20 });
        root.Children.Add(new TextBlock
        {
            Text = "La descrizione resta sempre nel progetto. Puoi modificarla o copiarla; soltanto al momento dell'esportazione scegli se allegarla anche alla raccolta.",
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
        root.Children.Add(includeDescriptions);
        root.Children.Add(export);
        root.Children.Add(status);

        RefreshJobs();
        LoadSelected();
    }

    private static void HideOldImagesOnlyExport(Control control)
    {
        foreach (var child in Children(control))
        {
            if (child is Button button && string.Equals(button.Content?.ToString(), "ZIP immagini approvate", StringComparison.Ordinal))
            {
                button.IsVisible = false;
                ToolTip.SetTip(button, "Sostituito da Esporta raccolta, che permette di scegliere se allegare anche le descrizioni.");
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
