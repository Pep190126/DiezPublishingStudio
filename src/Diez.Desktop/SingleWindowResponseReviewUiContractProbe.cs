using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class SingleWindowResponseReviewUiContractProbe
{
    public static async Task RunAsync(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost")
            ?? throw new InvalidOperationException("PageHost non disponibile per Response Review contract.");
        var previewHost = Field<ContentControl>(host, "_previewHost")
            ?? throw new InvalidOperationException("PreviewHost non disponibile per Response Review contract.");
        var path = Path.Combine(Path.GetTempPath(), "diez-response-review-contract-" + Guid.NewGuid().ToString("N") + ".diez");
        try
        {
            var project = ProjectFileStore.Create("Response Review Contract");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            var jobs = AiImageBatchService.CreateImageSeries(project, 3, "3 animali diversi 3 immagini", "Tavola").ToList();
            VisualPromptSessionService.EnsureActive(project);
            await ProjectFileStore.SaveAsync(path, project);
            SetSession(window, project, path);

            var state = AiExchangeStateStore.Load(project);
            var active = VisualPromptSessionService.ActiveLegacyJobIds(project);
            var units = state.WorkUnits
                .Where(u => u.LegacyAiJobId.HasValue && active.Contains(u.LegacyAiJobId.Value))
                .OrderBy(u => u.Position)
                .ToList();
            if (units.Count != 3) throw new InvalidOperationException("Response Review contract: tre Work Unit non disponibili.");
            foreach (var unit in units)
            {
                AiExchangeResponseFailureStore.RecordFailure(
                    project,
                    "UI-CONTRACT-PACKAGE",
                    Guid.NewGuid(),
                    unit.WorkUnitId,
                    1,
                    "Triptych rejected.",
                    "Renderer produced three bordered panels; one-composition hard lock failed.",
                    Guid.NewGuid().ToString("D"),
                    new string('a', 64));
            }
            await ProjectFileStore.SaveAsync(path, project);

            SingleWindowResponseReviewUi.Open(window);
            await WaitAsync(260);
            if (pageHost.Content is not Grid root || root.Name != "DiezResponseReviewPage")
                throw new InvalidOperationException("Response Review non usa la pagina sicura dedicata.");
            var scroll = Descendants(root).OfType<ScrollViewer>().FirstOrDefault(s => s.Name == "DiezResponseReviewScroll")
                ?? throw new InvalidOperationException("Response Review non contiene ScrollViewer principale.");
            if (scroll.VerticalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled)
                throw new InvalidOperationException("Scroll verticale Response Review disabilitato.");
            var list = Descendants(root).OfType<ListBox>().FirstOrDefault(l => l.Name == "DiezResponseReviewList")
                ?? throw new InvalidOperationException("Lista Response Review assente.");
            if (list.ItemsSource is null) throw new InvalidOperationException("Lista Response Review senza righe.");

            await WaitAsync(260);
            var visibleText = string.Join("\n", Descendants(root).OfType<TextBlock>().Select(t => t.Text ?? string.Empty)) + "\n" +
                              string.Join("\n", Descendants(root).OfType<TextBox>().Select(t => t.Text ?? string.Empty));
            if (!visibleText.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("FAILED provider non visibile nella pagina Review.");
            if (!visibleText.Contains("three bordered panels", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("failure_reason non visibile/auditabile nella pagina Review.");
            if (visibleText.Contains("Risultato non ancora disponibile", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("FAILED auditato viene ancora presentato come risultato genericamente non disponibile.");

            if (previewHost.Content is not Control preview)
                throw new InvalidOperationException("Anteprima Response Review assente.");
            var previewText = string.Join("\n", Descendants(preview).OfType<TextBlock>().Select(t => t.Text ?? string.Empty));
            if (!previewText.Contains("nessun asset incluso", StringComparison.OrdinalIgnoreCase) ||
                !previewText.Contains("correttamente scartato", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Anteprima FAILED non spiega che l'asset non conforme è stato scartato.");
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private static async Task WaitAsync(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static void SetSession(MainWindow window, PreviewProject project, string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
        typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, path);
    }

    private static T? Field<T>(object owner, string name) where T : class =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) as T;

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
