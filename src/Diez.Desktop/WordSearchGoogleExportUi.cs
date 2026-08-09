using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

internal static class WordSearchGoogleExportUi
{
    public static void Attach(MainWindow window)
    {
        if (window.Content is not Control root) return;
        var exportTab = FindExportTab(root);
        if (exportTab?.Content is not Control content) return;

        ReplaceButton(content, "Esporta database", async () => await ExportDatabaseAsync(window));
        ReplaceButton(content, "Esporta XLSX", async () => await ExportXlsxAsync(window));
        ReplaceButton(content, "Esporta CSV", async () => await ExportCsvAsync(window));
    }

    private static void ReplaceButton(Control root, string text, Func<Task> action)
    {
        foreach (var panel in Descendants(root).OfType<Panel>())
        {
            for (var i = 0; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is not Button old || !string.Equals(old.Content?.ToString(), text, StringComparison.Ordinal)) continue;
                var replacement = new Button
                {
                    Content = text,
                    Width = old.Width,
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };
                replacement.Click += async (_, _) => await action();
                ToolTip.SetTip(replacement, text switch
                {
                    "Esporta database" => "Database Word Search completo e reimportabile: sul PC, in Fogli Google o in entrambe le destinazioni.",
                    "Esporta XLSX" => "Output Puzzle 1...Puzzle N in XLSX: sul PC, in Fogli Google o entrambi.",
                    _ => "Output Puzzle 1...Puzzle N in CSV: sul PC, importato in Fogli Google o entrambi."
                });
                panel.Children.RemoveAt(i);
                panel.Children.Insert(i, replacement);
                return;
            }
        }
    }

    private static async Task ExportDatabaseAsync(MainWindow window)
    {
        if (!TrySession(window, out var project)) return;
        var destination = await OutputDestinationUi.ChooseAsync(window, "Fogli Google", "Database Word Search completo e reimportabile (XLSX)");
        if (destination is null) return;
        var suggested = WordSearchFullDatabaseExportService.SuggestedName(project);
        var path = destination == OutputDestination.Google
            ? OutputDestinationUi.TempPath(suggested)
            : await PickPathAsync(window, "Esporta database Word Search", suggested, "xlsx", "Database Word Search XLSX", "*.xlsx");
        if (string.IsNullOrWhiteSpace(path)) return;

        var deleteAfter = destination == OutputDestination.Google;
        try
        {
            var result = await WordSearchFullDatabaseExportService.ExportAsync(project, path);
            if (!result.Success) { SetStatus(window, result.Message); return; }
            var messages = new List<string>();
            if (destination != OutputDestination.Google) messages.Add(result.Message);
            if (destination is OutputDestination.Google or OutputDestination.Both)
            {
                var google = await GoogleDocsExportService.ExportXlsxAsync(path, Path.GetFileName(path));
                messages.Add(google.Message);
            }
            SetStatus(window, string.Join("  ", messages));
        }
        catch (Exception ex) { SetStatus(window, "Errore esportazione database: " + ex.Message); }
        finally { if (deleteAfter) TryDelete(path); }
    }

    private static async Task ExportXlsxAsync(MainWindow window)
    {
        if (!TrySession(window, out var project)) return;
        var destination = await OutputDestinationUi.ChooseAsync(window, "Fogli Google", "Word Search a colonne Puzzle 1...Puzzle N (XLSX)");
        if (destination is null) return;
        var suggested = WordSearchColumnExportService.SuggestedXlsxName(project);
        var path = destination == OutputDestination.Google
            ? OutputDestinationUi.TempPath(suggested)
            : await PickPathAsync(window, "Esporta Word Search in XLSX", suggested, "xlsx", "Foglio XLSX", "*.xlsx");
        if (string.IsNullOrWhiteSpace(path)) return;

        var deleteAfter = destination == OutputDestination.Google;
        try
        {
            var result = await WordSearchColumnExportService.ExportXlsxAsync(project, path);
            if (!result.Success) { SetStatus(window, result.Message); return; }
            var messages = new List<string>();
            if (destination != OutputDestination.Google) messages.Add(result.Message);
            if (destination is OutputDestination.Google or OutputDestination.Both)
            {
                var google = await GoogleDocsExportService.ExportXlsxAsync(path, Path.GetFileName(path));
                messages.Add(google.Message);
            }
            SetStatus(window, string.Join("  ", messages));
        }
        catch (Exception ex) { SetStatus(window, "Errore esportazione XLSX: " + ex.Message); }
        finally { if (deleteAfter) TryDelete(path); }
    }

    private static async Task ExportCsvAsync(MainWindow window)
    {
        if (!TrySession(window, out var project)) return;
        var destination = await OutputDestinationUi.ChooseAsync(window, "Fogli Google", "Word Search a colonne Puzzle 1...Puzzle N (CSV)");
        if (destination is null) return;
        var suggested = WordSearchColumnExportService.SuggestedCsvName(project);
        var path = destination == OutputDestination.Google
            ? OutputDestinationUi.TempPath(suggested)
            : await PickPathAsync(window, "Esporta Word Search in CSV", suggested, "csv", "CSV", "*.csv");
        if (string.IsNullOrWhiteSpace(path)) return;

        var deleteAfter = destination == OutputDestination.Google;
        try
        {
            var result = await WordSearchColumnExportService.ExportCsvAsync(project, path);
            if (!result.Success) { SetStatus(window, result.Message); return; }
            var messages = new List<string>();
            if (destination != OutputDestination.Google) messages.Add(result.Message);
            if (destination is OutputDestination.Google or OutputDestination.Both)
            {
                var google = await GoogleDocsExportService.ExportCsvAsSheetAsync(path, Path.GetFileNameWithoutExtension(path));
                messages.Add(google.Message);
            }
            SetStatus(window, string.Join("  ", messages));
        }
        catch (Exception ex) { SetStatus(window, "Errore esportazione CSV: " + ex.Message); }
        finally { if (deleteAfter) TryDelete(path); }
    }

    private static async Task<string?> PickPathAsync(MainWindow window, string title, string suggested, string extension, string typeName, string pattern)
    {
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggested,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = [pattern] }]
        });
        return file?.Path.LocalPath;
    }

    private static TabItem? FindExportTab(Control root)
    {
        foreach (var control in Descendants(root))
        {
            if (control is not TabControl tabs || tabs.ItemsSource is not IEnumerable<TabItem> items) continue;
            var export = items.FirstOrDefault(i => string.Equals(i.Header?.ToString(), "Esporta", StringComparison.Ordinal));
            if (export is not null) return export;
        }
        return null;
    }

    private static bool TrySession(MainWindow window, out PreviewProject project)
    {
        project = typeof(MainWindow).GetField("_project", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as PreviewProject ?? null!;
        return project is not null;
    }

    private static void SetStatus(MainWindow window, string message)
    {
        var status = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as TextBlock;
        if (status is not null) status.Text = message;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        if (root is Panel panel)
            foreach (var child in panel.Children.OfType<Control>())
                foreach (var nested in Descendants(child)) yield return nested;
        if (root is Border border && border.Child is Control borderChild)
            foreach (var nested in Descendants(borderChild)) yield return nested;
        if (root is ContentControl content && content.Content is Control contentChild)
            foreach (var nested in Descendants(contentChild)) yield return nested;
    }
}
