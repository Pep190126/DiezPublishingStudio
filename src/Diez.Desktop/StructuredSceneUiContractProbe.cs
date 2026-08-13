using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class StructuredSceneUiContractProbe
{
    public static async Task RunAsync(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
                       ?? throw new InvalidOperationException("Scene probe: page host assente.");
        var tempPath = Path.Combine(Path.GetTempPath(), "diez-scene-ui-" + Guid.NewGuid().ToString("N") + ".diez");
        try
        {
            var project = ProjectFileStore.Create("Structured Scene UI Contract");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            var multi = MultiSubjectProfileService.Load(project);
            multi.Enabled = true;
            MultiSubjectProfileService.SetCount(multi, 2);
            var subjects = MultiSubjectProfileService.ActiveSubjects(multi).ToList();
            MultiSubjectProfileService.TryRename(multi, subjects[0], "Milo", out _);
            MultiSubjectProfileService.TryRename(multi, subjects[1], "Luna", out _);
            multi.ActiveSubjectId = subjects[0].SubjectId;
            MultiSubjectProfileService.Save(project, multi);
            await ProjectFileStore.SaveAsync(tempPath, project);
            SetSession(window, project, tempPath);

            SingleWindowNativeV11Ui.ShowQuantity(window, host);
            await WaitAsync();
            SingleWindowSubjectStyleUi.Refresh(window);
            SingleWindowStructuredSceneUi.Refresh(window);
            await WaitAsync();

            var page = pageHost.Content as Control ?? throw new InvalidOperationException("Scene probe: pagina Quantità assente.");
            var enabled = Require<CheckBox>(page, "StructuredSceneEnabled");
            var count = Require<NumericUpDown>(page, "StructuredSceneCount");
            var selector = Require<ComboBox>(page, "StructuredSceneSelector");
            var name = Require<TextBox>(page, "StructuredSceneName");
            var description = Require<TextBox>(page, "StructuredSceneDescription");
            Require<Button>(page, "StructuredSceneAdd");
            Require<Button>(page, "StructuredSceneRemove");
            if (enabled.IsChecked == true)
                throw new InvalidOperationException("Scene probe: scene strutturate devono essere facoltative/OFF per default.");
            if (count.Maximum != StructuredSceneProfileService.MaxScenes)
                throw new InvalidOperationException("Scene probe: massimo scene UI non allineato al modello.");

            enabled.IsChecked = true;
            count.Value = 2;
            await WaitAsync(220);
            var scenes = StructuredSceneProfileService.Load(project);
            StructuredSceneProfileService.SetCount(scenes, 2);
            var active = StructuredSceneProfileService.ActiveScenes(scenes).ToList();
            StructuredSceneProfileService.TryRename(scenes, active[0], "Gioco in giardino", out _);
            StructuredSceneProfileService.TryRename(scenes, active[1], "Merenda sotto l'albero", out _);
            active[0].Description = "Milo rincorre una farfalla mentre Luna lo osserva.";
            active[1].Description = "Milo e Luna condividono una merenda tranquilla.";
            StructuredSceneProfileService.SetSubjectParticipation(scenes, active[0].SceneId, subjects[0].SubjectId, true);
            StructuredSceneProfileService.SetSubjectParticipation(scenes, active[0].SceneId, subjects[1].SubjectId, true);
            scenes.ActiveSceneId = active[0].SceneId;
            StructuredSceneProfileService.Save(project, scenes);
            await ProjectFileStore.SaveAsync(tempPath, project);
            SingleWindowStructuredSceneUi.Refresh(window);
            await WaitAsync();

            page = pageHost.Content as Control ?? throw new InvalidOperationException("Scene probe: pagina persa.");
            selector = Require<ComboBox>(page, "StructuredSceneSelector");
            name = Require<TextBox>(page, "StructuredSceneName");
            description = Require<TextBox>(page, "StructuredSceneDescription");
            if (!string.Equals(name.Text, "Gioco in giardino", StringComparison.Ordinal))
                throw new InvalidOperationException("Scene probe: nome scena attiva non caricato.");
            if (!string.Equals(description.Text, active[0].Description, StringComparison.Ordinal))
                throw new InvalidOperationException("Scene probe: descrizione non legata allo SceneId attivo.");
            if (Values(selector).Count != 2)
                throw new InvalidOperationException("Scene probe: selector non mostra due scene.");

            var stableIds = active.Select(x => x.SceneId).ToArray();
            StructuredSceneProfileService.SetCount(scenes, 1);
            StructuredSceneProfileService.Save(project, scenes);
            var reduced = StructuredSceneProfileService.Load(project);
            if (reduced.Scenes.Count < 2 || !stableIds.All(id => reduced.Scenes.Any(x => string.Equals(x.SceneId, id, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("Scene probe: ridurre il numero di scene cancella lo storico SceneId.");
            StructuredSceneProfileService.SetCount(reduced, 2);
            reduced.ActiveSceneId = stableIds[0];
            StructuredSceneProfileService.Save(project, reduced);

            var consistent = Descendants(page).OfType<CheckBox>().FirstOrDefault(x => x.Name == "NativeConsistent")
                             ?? throw new InvalidOperationException("Scene probe: Consistent assente.");
            consistent.IsChecked = true;
            await WaitAsync();
            SingleWindowSubjectStyleUi.Refresh(window);
            SingleWindowStructuredSceneUi.Refresh(window);
            await WaitAsync();
            page = pageHost.Content as Control ?? throw new InvalidOperationException("Scene probe: pagina persa dopo Consistent.");
            var membership = Require<StackPanel>(page, "DiezSubjectSceneMembership");
            if (!membership.IsVisible)
                throw new InvalidOperationException("Scene probe: membership scene non visibile nel Consistent del soggetto.");
            var sceneChecks = Descendants(membership).OfType<CheckBox>().ToList();
            if (sceneChecks.Count != 2)
                throw new InvalidOperationException("Scene probe: Consistent non elenca tutte le scene attive.");
            var persisted = StructuredSceneProfileService.Load(project);
            var first = StructuredSceneProfileService.ActiveScenes(persisted).First();
            if (!first.ParticipantSubjectIds.Contains(subjects[0].SubjectId, StringComparer.OrdinalIgnoreCase) ||
                !first.ParticipantSubjectIds.Contains(subjects[1].SubjectId, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Scene probe: membership non usa SubjectId stabili.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static T Require<T>(Control root, string name) where T : Control =>
        Descendants(root).OfType<T>().FirstOrDefault(x => x.Name == name)
        ?? throw new InvalidOperationException($"Scene probe: controllo mancante {name}.");

    private static List<string> Values(ComboBox combo) =>
        (combo.ItemsSource as System.Collections.IEnumerable)?.Cast<object>().Select(x => x?.ToString() ?? string.Empty).ToList() ?? [];

    private static void SetSession(MainWindow window, PreviewProject project, string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
        typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, path);
    }

    private static async Task WaitAsync(int ms = 170)
    {
        await Task.Delay(ms);
        await Dispatcher.UIThread.InvokeAsync(() => { });
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
                case Panel p:
                    for (var i = p.Children.Count - 1; i >= 0; i--) stack.Push(p.Children[i]);
                    break;
                case Border b when b.Child is Control child: stack.Push(child); break;
                case ScrollViewer s when s.Content is Control child: stack.Push(child); break;
                case ContentControl c when c.Content is Control child: stack.Push(child); break;
            }
        }
    }
}
