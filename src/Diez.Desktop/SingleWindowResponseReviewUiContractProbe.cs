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
            AiImageBatchService.CreateImageSeries(project, 3, "3 animali diversi 3 immagini", "Tavola").ToList();
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
            var failedVersions = new Dictionary<Guid, int>();
            foreach (var unit in units)
            {
                var latestVersion = state.Versions
                    .Where(v => v.WorkUnitId == unit.WorkUnitId)
                    .Select(v => v.VersionNumber)
                    .DefaultIfEmpty(0)
                    .Max();
                var failedVersion = latestVersion + 1;
                failedVersions[unit.WorkUnitId] = failedVersion;
                var packageId = "UI-CONTRACT-PACKAGE";
                var promptPackId = Guid.NewGuid();
                var renderRequestId = Guid.NewGuid().ToString("D");
                const string reason = "Renderer produced three bordered panels; one-composition hard lock failed.";
                AiExchangeResponseFailureStore.RecordFailure(
                    project,
                    packageId,
                    promptPackId,
                    unit.WorkUnitId,
                    failedVersion,
                    "Triptych rejected.",
                    reason,
                    renderRequestId,
                    new string('a', 64));
                if (!AiExchangeResponseFailureStore.RecordFailureVersion(
                        state,
                        unit,
                        packageId,
                        promptPackId,
                        failedVersion,
                        "Triptych rejected.",
                        reason,
                        renderRequestId,
                        new string('a', 64),
                        null))
                    throw new InvalidOperationException(unit.Code + ": FAILED centrale non registrabile.");
                RequireFailure(project, state, unit, failedVersion, "immediatamente dopo RecordFailure");
            }
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(path, project);

            var memoryState = AiExchangeStateStore.Load(project);
            foreach (var unit in units)
                RequireFailure(project, memoryState, unit, failedVersions[unit.WorkUnitId], "dopo ProjectFileStore.SaveAsync in memoria");

            var reloaded = await ProjectFileStore.LoadAsync(path);
            var reloadedState = AiExchangeStateStore.Load(reloaded);
            foreach (var unit in units)
                RequireFailure(reloaded, reloadedState, unit, failedVersions[unit.WorkUnitId], "dopo reload fisico del .diez");

            var sessionProject = typeof(MainWindow).GetField("_project", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as PreviewProject
                ?? throw new InvalidOperationException("Progetto sessione assente prima della Review.");
            var sessionState = AiExchangeStateStore.Load(sessionProject);
            foreach (var unit in units)
                RequireFailure(sessionProject, sessionState, unit, failedVersions[unit.WorkUnitId], "nella sessione MainWindow prima della Review");

            SingleWindowResponseReviewUi.Open(window);
            var afterOpenState = AiExchangeStateStore.Load(project);
            foreach (var unit in units)
                RequireFailure(project, afterOpenState, unit, failedVersions[unit.WorkUnitId], "subito dopo apertura sincrona della Review");
            await WaitAsync(260);
            var root = pageHost.Content is Control mounted
                ? Descendants(mounted).OfType<Grid>().FirstOrDefault(g => g.Name == "DiezResponseReviewPage")
                : null;
            if (root is null)
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
                throw new InvalidOperationException("failure_reason non visibile/auditabile nella pagina Review. Testo renderizzato: " + visibleText);
            if (visibleText.Contains("Risultato non ancora disponibile", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("FAILED auditato viene ancora presentato come risultato genericamente non disponibile.");

            if (previewHost.Content is not Control preview)
                throw new InvalidOperationException("Anteprima Response Review assente.");
            var previewText = string.Join("\n", Descendants(preview).OfType<TextBlock>().Select(t => t.Text ?? string.Empty));
            if (!previewText.Contains("nessun asset incluso", StringComparison.OrdinalIgnoreCase) ||
                !previewText.Contains("correttamente scartato", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Anteprima FAILED non spiega che l'asset non conforme è stato scartato. Anteprima: " + previewText);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private static void RequireFailure(
        PreviewProject project,
        AiExchangeState state,
        AiExchangeWorkUnit unit,
        int expectedVersion,
        string stage)
    {
        var failure = AiExchangeResponseFailureStore.Latest(project, state, unit.WorkUnitId);
        if (failure is null)
            throw new InvalidOperationException($"{unit.Code}: FAILED perso {stage}. Entity kinds: {string.Join(", ", project.Entities.Select(e => e.Kind))}");
        if (failure.CandidateVersion != expectedVersion)
            throw new InvalidOperationException($"{unit.Code}: FAILED v{failure.CandidateVersion} invece di v{expectedVersion} {stage}.");
        var central = state.Versions.FirstOrDefault(v =>
            v.WorkUnitId == unit.WorkUnitId &&
            v.VersionNumber == expectedVersion &&
            AiExchangeResponseFailureStore.IsFailureVersion(v));
        if (central is null)
            throw new InvalidOperationException($"{unit.Code}: audit FAILED centrale assente {stage}.");
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
