using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

/// <summary>
/// Replaces the visible V2 Prompt Pack export button with the safe enhancer path.
/// Intake/paradigm/correction controls remain owned by SingleWindowAiImageContextUi.
/// Also keeps visible copy aligned with the current contract: layout-only settings are not AI presets.
/// </summary>
internal static class SingleWindowSafeImageContextExportUi
{
    private const string SafeButtonName = "DiezSafeImageContextExport";

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
        if (Descendants(page).Any(c => string.Equals(c.Name, SafeButtonName, StringComparison.Ordinal))) return;

        var candidate = Descendants(page).OfType<Button>().FirstOrDefault(b =>
            b.IsVisible && string.Equals(b.Content?.ToString(), "Crea Prompt Pack ZIP", StringComparison.Ordinal));
        if (candidate?.Parent is not StackPanel row) return;
        candidate.IsVisible = false;

        var safe = new Button
        {
            Name = SafeButtonName,
            Content = "Crea Prompt Pack ZIP",
            Width = 190,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ToolTip.SetTip(safe,
            "Esporta file reali di base/intake/paradigmi, descrizioni, preserve/change e tutti i preset effettivi di generazione in request-context.json. I parametri d'impaginazione restano fuori.");
        safe.Click += async (_, _) =>
        {
            var state = AiExchangeStateStore.Load(project);
            var units = state.WorkUnits
                .Where(u => string.Equals(u.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
                .OrderBy(u => u.Position)
                .ThenBy(u => u.Code)
                .ToList();
            if (units.Count == 0)
            {
                SetStatus(window, "Non ci sono immagini / Work Unit da esportare nel Prompt Pack.");
                return;
            }

            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva Prompt Pack Diez con contesto immagini completo",
                SuggestedFileName = "diez-prompt-pack.zip",
                DefaultExtension = "zip",
                FileTypeChoices = [new FilePickerFileType("Prompt Pack Diez") { Patterns = ["*.zip"] }]
            });
            if (file is null) return;

            var target = file.Path.LocalPath;
            var built = await AiExchangePromptPackBuilder.BuildAsync(
                project, path, state, units.Select(u => u.WorkUnitId), target);
            if (!built.Success)
            {
                SetStatus(window, built.Message);
                return;
            }

            var enhanced = await AiExchangeImageRequestContextSafeEnhancer.EnhancePromptPackAsync(
                project, path, state, units.Select(u => u.WorkUnitId), target);
            SetStatus(window, enhanced.Success
                ? built.Message + " · " + enhanced.Message
                : "Prompt Pack core creato, ma il contesto immagini completo non è stato aggiunto: " + enhanced.Message);
        };

        var index = row.Children.IndexOf(candidate);
        row.Children.Insert(index < 0 ? row.Children.Count : index + 1, safe);
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
