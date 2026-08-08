using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

internal static class EpubWorkflowUi
{
    private const string ProjectFieldName = "_project";
    private const string ProjectPathFieldName = "_currentProjectPath";
    private const string StatusFieldName = "_status";

    public static void Attach(MainWindow window)
    {
        window.Title = "Diez Publishing Studio — 0.11 Preview";

        if (window.Content is not Border border || border.Child is not StackPanel root)
            return;

        var subtitle = root.Children
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text?.StartsWith("Preview 0.10", StringComparison.Ordinal) == true ||
                                 t.Text?.StartsWith("Preview 0.11", StringComparison.Ordinal) == true);
        if (subtitle is not null)
            subtitle.Text = "Preview 0.11 — Edition Metadata + Publication Candidate + EPUB 3.3 export";

        var projectButtons = root.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Button>().Any(button =>
                                         string.Equals(button.Content?.ToString(), "Edizione / Preflight", StringComparison.Ordinal)));
        if (projectButtons is null) return;
        if (projectButtons.Children.OfType<Button>().Any(button =>
                string.Equals(button.Content?.ToString(), "Esporta EPUB", StringComparison.Ordinal)))
            return;

        var epubButton = new Button
        {
            Content = "Esporta EPUB",
            Width = 145,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        epubButton.Click += async (_, _) => await ExportAsync(window);
        projectButtons.Children.Add(epubButton);
    }

    private static async Task ExportAsync(MainWindow window)
    {
        if (!TryGetSession(window, out var project, out _))
        {
            SetMainStatus(window, "Prima crea o apri un progetto .diez per esportare EPUB.");
            return;
        }

        if (!PublicationCandidateService.IsLatestCandidateCurrent(project))
        {
            SetMainStatus(window, "EPUB non esportabile: apri Edizione / Preflight e crea un Publication Candidate corrente.");
            return;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta EPUB 3.3",
            SuggestedFileName = EpubExportService.SuggestedFileName(project),
            DefaultExtension = "epub",
            FileTypeChoices = [new FilePickerFileType("EPUB 3") { Patterns = ["*.epub"] }]
        });
        if (file is null) return;

        try
        {
            var result = await EpubExportService.ExportAsync(project, file.Path.LocalPath);
            SetMainStatus(window, result.Message);
        }
        catch (Exception ex)
        {
            SetMainStatus(window, $"Esportazione EPUB fallita: {ex.Message}");
        }
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
