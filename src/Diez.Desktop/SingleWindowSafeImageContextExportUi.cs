using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

/// <summary>
/// Guided visual transport UI. It selects the active Work Units and delegates the complete ZIP
/// pipeline to AiVisualPromptPackService; response import delegates to the audited V2 importer.
/// </summary>
internal static class SingleWindowSafeImageContextExportUi
{
    private const string SafeButtonName = "DiezSafeImageContextExport";
    private const string SafeImportName = "DiezSafeImageContextImport";
    private const string SafeReviewName = "DiezSafeResponseReview";

    public static void Attach(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost is null) return;
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty) EnsureCurrentPage(window);
        };
        EnsureCurrentPage(window);
    }

    internal static void EnsureCurrentPage(MainWindow window)
    {
        if (!TrySession(window, out var project, out var path)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page) return;

        CleanLayoutOnlyCopy(page);
        var oldExport = Descendants(page).OfType<Button>().FirstOrDefault(b =>
            b.IsVisible && string.Equals(b.Content?.ToString(), "Crea Prompt Pack ZIP", StringComparison.Ordinal));
        var oldImport = Descendants(page).OfType<Button>().FirstOrDefault(b =>
            b.IsVisible && string.Equals(b.Content?.ToString(), "Importa risultati AI", StringComparison.Ordinal));
        var oldReview = Descendants(page).OfType<Button>().FirstOrDefault(b =>
            b.IsVisible && string.Equals(b.Content?.ToString(), "Controlla risultati", StringComparison.Ordinal));
        var row = oldExport?.Parent as StackPanel ?? oldImport?.Parent as StackPanel ?? oldReview?.Parent as StackPanel;
        if (row is null) return;

        if (oldExport is not null && !Descendants(page).Any(c => string.Equals(c.Name, SafeButtonName, StringComparison.Ordinal)))
        {
            oldExport.IsVisible = false;
            var safe = new Button
            {
                Name = SafeButtonName,
                Content = "Crea Prompt Pack ZIP",
                Width = 190,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            ToolTip.SetTip(safe,
                "Esporta un solo Prompt Pack con prompt VISUAL-ONLY e una clean-room queue guidata. Il launcher interno accompagna l'utente Work Unit per Work Unit e produce Response parziali importabili insieme.");
            safe.Click += async (_, _) =>
            {
                var state = AiExchangeStateStore.Load(project);
                var activeLegacyIds = VisualPromptSessionService.ActiveLegacyJobIds(project);
                var units = state.WorkUnits
                    .Where(u => string.Equals(u.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase) &&
                                u.LegacyAiJobId.HasValue && activeLegacyIds.Contains(u.LegacyAiJobId.Value))
                    .OrderBy(u => u.Position)
                    .ThenBy(u => u.Code)
                    .ToList();
                if (units.Count == 0)
                {
                    SetStatus(window, "Non ci sono immagini della sessione visuale attiva da esportare.");
                    return;
                }

                var nextVersion = BookPackageNamingService.PeekNextVersion(project);
                var suggestedName = BookPackageNamingService.PromptPackFileName(project, nextVersion);
                var firstPart = BookPackageNamingService.ResponsePartFileName(project, nextVersion, 1);
                var lastPart = BookPackageNamingService.ResponsePartFileName(project, nextVersion, units.Count);
                var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Salva Prompt Pack Diez con clean-room queue",
                    SuggestedFileName = suggestedName,
                    DefaultExtension = "zip",
                    FileTypeChoices = [new FilePickerFileType("Prompt Pack Diez") { Patterns = ["*.zip"] }]
                });
                if (file is null) return;

                var result = await AiVisualPromptPackService.BuildAsync(
                    project,
                    path,
                    state,
                    units.Select(u => u.WorkUnitId),
                    EnsureZip(file.Path.LocalPath));
                SetStatus(window, result.Success
                    ? result.Message + $" · Apri {PromptPackCleanRoomQueueService.LauncherFileName} nel Pack. Response: {firstPart}" +
                      (units.Count > 1 ? $" … {lastPart}" : string.Empty) +
                      " · poi importale tutte insieme con Importa risultati AI."
                    : result.Message);
            };

            var index = row.Children.IndexOf(oldExport);
            row.Children.Insert(index < 0 ? row.Children.Count : index + 1, safe);
        }

        if (oldImport is not null && !Descendants(page).Any(c => string.Equals(c.Name, SafeImportName, StringComparison.Ordinal)))
        {
            oldImport.IsVisible = false;
            var safeImport = new Button
            {
                Name = SafeImportName,
                Content = "Importa risultati AI",
                Width = 180,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            ToolTip.SetTip(safeImport,
                "Seleziona insieme uno o più Response ZIP, inclusi i part-NNN della clean-room queue. Diez li aggrega sullo stesso Prompt Pack/snapshot; Candidate e FAILED restano auditati separatamente.");
            safeImport.Click += async (_, _) =>
            {
                var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Importa insieme i Response ZIP Diez (anche part-NNN)",
                    AllowMultiple = true,
                    FileTypeFilter = [new FilePickerFileType("Risultati AI Diez") { Patterns = ["*.zip"] }]
                });
                if (files.Count == 0) return;

                var state = AiExchangeStateStore.Load(project);
                var result = await AiExchangeResponseImportV2.ImportAsync(
                    project, path, state, files.Select(f => f.Path.LocalPath));
                SetStatus(window, result.Message);
                SingleWindowResponseReviewUi.Open(window);
            };

            var index = row.Children.IndexOf(oldImport);
            row.Children.Insert(index < 0 ? row.Children.Count : index + 1, safeImport);
        }

        if (oldReview is not null && !Descendants(page).Any(c => string.Equals(c.Name, SafeReviewName, StringComparison.Ordinal)))
        {
            oldReview.IsVisible = false;
            var safeReview = new Button
            {
                Name = SafeReviewName,
                Content = "Controlla risultati",
                Width = 175,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            ToolTip.SetTip(safeReview,
                "Apre la revisione scrollabile: Candidate con asset e FAILED provider senza asset restano stati distinti e auditabili, anche quando arrivano da Response parziali.");
            safeReview.Click += (_, _) => SingleWindowResponseReviewUi.Open(window);
            var index = row.Children.IndexOf(oldReview);
            row.Children.Insert(index < 0 ? row.Children.Count : index + 1, safeReview);
        }
    }

    private static void CleanLayoutOnlyCopy(Control page)
    {
        foreach (var text in Descendants(page).OfType<TextBlock>())
        {
            var value = text.Text ?? string.Empty;
            if (value.Contains("formato, margini e bleed", StringComparison.OrdinalIgnoreCase))
                text.Text = value.Replace("formato, margini e bleed", "formato", StringComparison.OrdinalIgnoreCase);
            else if (value.Contains("margini e bleed", StringComparison.OrdinalIgnoreCase))
                text.Text = value.Replace("margini e bleed", "parametri d'impaginazione", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string EnsureZip(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? path : path + ".zip";

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private static void SetStatus(MainWindow window, string message)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var status = host.GetType().GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as TextBlock;
        if (status is not null) status.Text = message;
        var main = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as TextBlock;
        if (main is not null) main.Text = message;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        var stack = new Stack<Control>();
        var seen = new HashSet<Control>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current)) continue;
            yield return current;
            switch (current)
            {
                case Panel panel:
                    for (var i = panel.Children.Count - 1; i >= 0; i--) stack.Push(panel.Children[i]);
                    break;
                case Border border when border.Child is Control child: stack.Push(child); break;
                case ScrollViewer scroll when scroll.Content is Control child: stack.Push(child); break;
                case ContentControl content when content.Content is Control child: stack.Push(child); break;
            }
        }
    }
}
