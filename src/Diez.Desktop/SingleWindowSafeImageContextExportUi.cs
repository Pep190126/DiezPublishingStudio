using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

/// <summary>
/// Authoritative Prompt Pack / response transport controls for the guided visual workflow.
/// Export uses the enriched context plus the canonical prompt-engineering finalizer;
/// import uses the audited V2 importer with per-item asset verification.
/// </summary>
internal static class SingleWindowSafeImageContextExportUi
{
    private const string SafeButtonName = "DiezSafeImageContextExport";
    private const string SafeImportName = "DiezSafeImageContextImport";

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
        var row = oldExport?.Parent as StackPanel ?? oldImport?.Parent as StackPanel;
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
                "Esporta il solo profilo attivo, prompt professionali provider-specific, un'immagine per Work Unit, file reali e request-context.json.");
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

                var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Salva Prompt Pack Diez con prompt engineering completo",
                    SuggestedFileName = "diez-prompt-pack.zip",
                    DefaultExtension = "zip",
                    FileTypeChoices = [new FilePickerFileType("Prompt Pack Diez") { Patterns = ["*.zip"] }]
                });
                if (file is null) return;

                var target = EnsureZip(file.Path.LocalPath);
                var built = await AiExchangePromptPackBuilder.BuildAsync(
                    project, path, state, units.Select(u => u.WorkUnitId), target);
                if (!built.Success)
                {
                    SetStatus(window, built.Message);
                    return;
                }

                var enhanced = await AiExchangeImageRequestContextSafeEnhancer.EnhancePromptPackAsync(
                    project, path, state, units.Select(u => u.WorkUnitId), target);
                if (!enhanced.Success)
                {
                    SetStatus(window, "Prompt Pack core creato, ma il contesto immagini completo non è stato aggiunto: " + enhanced.Message);
                    return;
                }

                PromptPackPromptEngineeringFinalizer.Finalize(
                    target, project, state, units.Select(u => u.WorkUnitId));
                await ProjectFileStore.SaveAsync(path, project);
                SetStatus(window,
                    $"Prompt Pack pronto: {units.Count} Work Unit · 1 immagine per Work Unit · profilo {BookTypeProfileService.Get(project)} isolato · prompt engine v{PromptEngineeringEngine.EngineVersion}.");
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
                "Verifica manifest e asset di ogni Work Unit prima dell'import e controlla la Candidate risultante dopo l'ingest.");
            safeImport.Click += async (_, _) =>
            {
                var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Importa uno o più ZIP restituiti dall'AI",
                    AllowMultiple = true,
                    FileTypeFilter = [new FilePickerFileType("Risultati AI Diez") { Patterns = ["*.zip"] }]
                });
                if (files.Count == 0) return;

                var state = AiExchangeStateStore.Load(project);
                var result = await AiExchangeResponseImportV2.ImportAsync(
                    project, path, state, files.Select(f => f.Path.LocalPath));
                SetStatus(window, result.Message);
                SingleWindowEntryPointUi.Invoke(host, "OpenReview");
            };

            var index = row.Children.IndexOf(oldImport);
            row.Children.Insert(index < 0 ? row.Children.Count : index + 1, safeImport);
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
