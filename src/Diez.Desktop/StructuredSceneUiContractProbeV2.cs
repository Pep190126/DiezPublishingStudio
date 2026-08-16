using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class StructuredSceneUiContractProbeV2
{
    public static async Task RunAsync(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
                       ?? throw new InvalidOperationException("Scene V2 probe: page host assente.");
        var tempPath = Path.Combine(Path.GetTempPath(), "diez-scene-ui-v2-" + Guid.NewGuid().ToString("N") + ".diez");
        try
        {
            var project = ProjectFileStore.Create("Structured Scene UI V2 Contract");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            var coloring = BookTypePromptProfileService.LoadColoring(project);
            coloring.EnvironmentDescription = "Un giardino tranquillo come ambientazione generale.";
            BookTypePromptProfileService.SaveColoring(project, coloring);

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

            var page = pageHost.Content as Control ?? throw new InvalidOperationException("Scene V2 probe: pagina Quantità assente.");
            var environment = Require<TextBox>(page, "VisualEnvironmentInstructions");
            var mode = Require<ComboBox>(page, "EnvironmentSceneMode");
            var selector = Require<ComboBox>(page, "StructuredSceneSelector");
            var name = Require<TextBox>(page, "StructuredSceneName");
            var add = Require<Button>(page, "StructuredSceneAdd");
            var archive = Require<Button>(page, "StructuredSceneArchive");
            var toolbar = Require<StackPanel>(page, "DiezStructuredSceneToolbar");

            if (!Values(mode).SequenceEqual(new[] { "Ambientazione generica", "Definisci scene" }, StringComparer.Ordinal))
                throw new InvalidOperationException("Scene V2 probe: switch Ambientazione/Scene errato.");
            if (!string.Equals(mode.SelectedItem?.ToString(), "Ambientazione generica", StringComparison.Ordinal))
                throw new InvalidOperationException("Scene V2 probe: modalità iniziale non generica.");
            if (toolbar.IsVisible)
                throw new InvalidOperationException("Scene V2 probe: toolbar scene visibile con modalità generica.");
            if (!string.Equals(environment.Text, coloring.EnvironmentDescription, StringComparison.Ordinal))
                throw new InvalidOperationException("Scene V2 probe: testo ambientazione generale non preservato.");

            SelectByText(mode, "Definisci scene");
            await WaitAsync(220);
            var scenes = StructuredSceneProfileService.Load(project);
            if (!scenes.Enabled || StructuredSceneProfileService.ActiveScenes(scenes).Count != 1)
                throw new InvalidOperationException("Scene V2 probe: attivazione scene non crea la prima scena.");
            if (!toolbar.IsVisible)
                throw new InvalidOperationException("Scene V2 probe: toolbar scene non visibile.");

            var first = StructuredSceneProfileService.ActiveScene(scenes)!;
            var firstId = first.SceneId;
            environment.Text = "Milo rincorre una farfalla mentre Luna lo osserva.";
            await WaitAsync();
            scenes = StructuredSceneProfileService.Load(project);
            if (!string.Equals(StructuredSceneProfileService.ActiveScene(scenes)?.Description, environment.Text, StringComparison.Ordinal))
                throw new InvalidOperationException("Scene V2 probe: lo stesso editor non salva la scena corrente.");

            first = StructuredSceneProfileService.ActiveScene(scenes)!;
            StructuredSceneProfileService.TryRename(scenes, first, "Gioco in giardino", out _);
            StructuredSceneProfileService.Save(project, scenes);
            SingleWindowStructuredSceneUi.Refresh(window);
            await WaitAsync();
            if (!string.Equals(name.Text, "Gioco in giardino", StringComparison.Ordinal))
                throw new InvalidOperationException("Scene V2 probe: nome scena non legato allo SceneId.");

            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(220);
            scenes = StructuredSceneProfileService.Load(project);
            var active = StructuredSceneProfileService.ActiveScenes(scenes).ToList();
            if (active.Count != 2)
                throw new InvalidOperationException("Scene V2 probe: + Nuova scena non crea una seconda scena.");
            var secondId = active[1].SceneId;
            if (string.Equals(firstId, secondId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Scene V2 probe: nuova scena ha riutilizzato SceneId.");
            if (!string.IsNullOrEmpty(environment.Text))
                throw new InvalidOperationException("Scene V2 probe: editor non pronto per la nuova scena.");
            environment.Text = "Milo e Luna condividono una merenda sotto un albero.";
            await WaitAsync();
            if (Values(selector).Count != 2)
                throw new InvalidOperationException("Scene V2 probe: selector non elenca le scene create.");

            archive.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(220);
            scenes = StructuredSceneProfileService.Load(project);
            if (StructuredSceneProfileService.ActiveScenes(scenes).Count != 1)
                throw new InvalidOperationException("Scene V2 probe: archiviazione non riduce le scene attive.");
            if (!scenes.Scenes.Any(x => string.Equals(x.SceneId, secondId, StringComparison.OrdinalIgnoreCase) && x.Archived))
                throw new InvalidOperationException("Scene V2 probe: scena archiviata non conserva SceneId storico.");

            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(220);
            scenes = StructuredSceneProfileService.Load(project);
            active = StructuredSceneProfileService.ActiveScenes(scenes).ToList();
            if (active.Count != 2 || active.Any(x => string.Equals(x.SceneId, secondId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Scene V2 probe: nuova scena ricicla uno SceneId archiviato.");

            foreach (var scene in active)
            {
                StructuredSceneProfileService.SetSubjectParticipation(scenes, scene.SceneId, subjects[0].SubjectId, false);
                StructuredSceneProfileService.SetSubjectParticipation(scenes, scene.SceneId, subjects[1].SubjectId, false);
            }
            StructuredSceneProfileService.Save(project, scenes);

            var consistent = Descendants(page).OfType<CheckBox>().FirstOrDefault(x => x.Name == "NativeConsistent")
                             ?? throw new InvalidOperationException("Scene V2 probe: Consistent assente.");
            consistent.IsChecked = true;
            await WaitAsync();
            SingleWindowSubjectStyleUi.Refresh(window);
            SingleWindowStructuredSceneUi.Refresh(window);
            await WaitAsync();

            page = pageHost.Content as Control ?? throw new InvalidOperationException("Scene V2 probe: pagina persa dopo Consistent.");
            var membership = Require<StackPanel>(page, "DiezSubjectSceneMembership");
            var list = Require<ListBox>(page, "SubjectSceneListBox");
            if (!membership.IsVisible || !list.IsVisible)
                throw new InvalidOperationException("Scene V2 probe: list box scene non visibile nel Consistent.");
            var checks = (list.ItemsSource as System.Collections.IEnumerable)?.Cast<object>().OfType<CheckBox>().ToList() ?? [];
            if (checks.Count != 2)
                throw new InvalidOperationException("Scene V2 probe: list box non contiene tutte le scene attive.");

            checks[0].IsChecked = true;
            checks[1].IsChecked = true;
            await WaitAsync();
            var persisted = StructuredSceneProfileService.Load(project);
            if (StructuredSceneProfileService.ActiveScenes(persisted).Any(scene =>
                    !scene.ParticipantSubjectIds.Contains(subjects[0].SubjectId, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Scene V2 probe: membership list box non persiste SubjectId.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static T Require<T>(Control root, string name) where T : Control =>
        Descendants(root).OfType<T>().FirstOrDefault(x => x.Name == name)
        ?? throw new InvalidOperationException($"Scene V2 probe: controllo mancante {name}.");

    private static List<string> Values(ComboBox combo) =>
        (combo.ItemsSource as System.Collections.IEnumerable)?.Cast<object>().Select(x => x?.ToString() ?? string.Empty).ToList() ?? [];

    private static void SelectByText(ComboBox combo, string value)
    {
        combo.SelectedItem = (combo.ItemsSource as System.Collections.IEnumerable)?.Cast<object>()
            .FirstOrDefault(x => string.Equals(x?.ToString(), value, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Scene V2 probe: voce combo mancante " + value);
    }

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
