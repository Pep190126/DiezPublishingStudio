using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Deterministic headless contract for the Environment/Scene switch.
/// The probe intentionally stays synchronous while it is dispatched on Avalonia's UI thread:
/// control events update the in-memory project immediately, while physical autosave remains an
/// independent background concern covered by the storage self-tests.
/// </summary>
internal static class StructuredSceneUiContractProbeV3
{
    public static void Run(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
                       ?? throw new InvalidOperationException("Scene V3 probe: page host assente.");
        var sessionPath = Path.Combine(Path.GetTempPath(), "diez-scene-ui-v3-" + Guid.NewGuid().ToString("N") + ".diez");

        var project = ProjectFileStore.Create("Structured Scene UI V3 Contract");
        BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
        var coloring = BookTypePromptProfileService.LoadColoring(project);
        coloring.EnvironmentDescription = "Un giardino tranquillo come ambientazione generale.";
        BookTypePromptProfileService.SaveColoring(project, coloring);

        var multi = MultiSubjectProfileService.Load(project);
        multi.Enabled = true;
        MultiSubjectProfileService.SetCount(multi, 2);
        var subjects = MultiSubjectProfileService.ActiveSubjects(multi).ToList();
        RenameSubject(multi, subjects[0], "Milo");
        RenameSubject(multi, subjects[1], "Luna");
        multi.ActiveSubjectId = subjects[0].SubjectId;
        MultiSubjectProfileService.Save(project, multi);
        SetSession(window, project, sessionPath);

        SingleWindowNativeV11Ui.ShowQuantity(window, host);
        Pump();
        SingleWindowSubjectStyleUi.Refresh(window);
        SingleWindowStructuredSceneUi.Refresh(window);
        Pump();

        var page = pageHost.Content as Control
                   ?? throw new InvalidOperationException("Scene V3 probe: pagina Quantità assente.");
        var environment = Require<TextBox>(page, "VisualEnvironmentInstructions");
        var mode = Require<ComboBox>(page, "EnvironmentSceneMode");
        var selector = Require<ComboBox>(page, "StructuredSceneSelector");
        var name = Require<TextBox>(page, "StructuredSceneName");
        var add = Require<Button>(page, "StructuredSceneAdd");
        var archive = Require<Button>(page, "StructuredSceneArchive");
        var toolbar = Require<StackPanel>(page, "DiezStructuredSceneToolbar");

        var modeValues = Values(mode);
        if (!modeValues.SequenceEqual(new[] { "Ambientazione generica", "Definisci scene" }, StringComparer.Ordinal))
            throw new InvalidOperationException("Scene V3 probe: switch Ambientazione/Scene errato.");
        if (!string.Equals(mode.SelectedItem?.ToString(), "Ambientazione generica", StringComparison.Ordinal))
            throw new InvalidOperationException("Scene V3 probe: modalità iniziale non generica.");
        if (toolbar.IsVisible)
            throw new InvalidOperationException("Scene V3 probe: toolbar scene visibile in modalità generica.");
        if (!string.Equals(environment.Text, coloring.EnvironmentDescription, StringComparison.Ordinal))
            throw new InvalidOperationException("Scene V3 probe: ambientazione generale non preservata.");

        SelectByText(mode, "Definisci scene");
        Pump();
        var scenes = StructuredSceneProfileService.Load(project);
        var active = StructuredSceneProfileService.ActiveScenes(scenes).ToList();
        if (!scenes.Enabled || active.Count != 1)
            throw new InvalidOperationException("Scene V3 probe: attivazione Scene non crea una prima scena univoca.");
        if (!toolbar.IsVisible)
            throw new InvalidOperationException("Scene V3 probe: toolbar scene non visibile in modalità Scene.");

        var firstId = active[0].SceneId;
        environment.Text = "Milo rincorre una farfalla mentre Luna lo osserva.";
        Pump();
        scenes = StructuredSceneProfileService.Load(project);
        if (!string.Equals(StructuredSceneProfileService.ActiveScene(scenes)?.Description, environment.Text, StringComparison.Ordinal))
            throw new InvalidOperationException("Scene V3 probe: lo stesso editor Ambientazione non salva la scena corrente.");

        name.Text = "Gioco in giardino";
        name.RaiseEvent(new RoutedEventArgs(Control.LostFocusEvent));
        Pump();
        scenes = StructuredSceneProfileService.Load(project);
        if (!string.Equals(StructuredSceneProfileService.ActiveScene(scenes)?.Name, "Gioco in giardino", StringComparison.Ordinal))
            throw new InvalidOperationException("Scene V3 probe: rinomina scena non legata allo SceneId.");

        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        scenes = StructuredSceneProfileService.Load(project);
        active = StructuredSceneProfileService.ActiveScenes(scenes).ToList();
        if (active.Count != 2)
            throw new InvalidOperationException("Scene V3 probe: + Nuova scena non crea una seconda scena.");
        var secondId = active[1].SceneId;
        if (string.Equals(firstId, secondId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Scene V3 probe: nuova scena ha riutilizzato SceneId.");
        if (!string.IsNullOrEmpty(environment.Text))
            throw new InvalidOperationException("Scene V3 probe: editor non pronto per la nuova scena.");

        environment.Text = "Milo e Luna condividono una merenda sotto un albero.";
        Pump();
        if (Values(selector).Count != 2)
            throw new InvalidOperationException("Scene V3 probe: selettore non elenca le scene create.");

        archive.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        scenes = StructuredSceneProfileService.Load(project);
        if (StructuredSceneProfileService.ActiveScenes(scenes).Count != 1)
            throw new InvalidOperationException("Scene V3 probe: archiviazione non riduce le scene attive.");
        if (!scenes.Scenes.Any(x => string.Equals(x.SceneId, secondId, StringComparison.OrdinalIgnoreCase) && x.Archived))
            throw new InvalidOperationException("Scene V3 probe: scena archiviata non conserva SceneId storico.");

        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        scenes = StructuredSceneProfileService.Load(project);
        active = StructuredSceneProfileService.ActiveScenes(scenes).ToList();
        if (active.Count != 2 || active.Any(x => string.Equals(x.SceneId, secondId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Scene V3 probe: una nuova scena ricicla uno SceneId archiviato.");

        foreach (var scene in active)
        {
            StructuredSceneProfileService.SetSubjectParticipation(scenes, scene.SceneId, subjects[0].SubjectId, false);
            StructuredSceneProfileService.SetSubjectParticipation(scenes, scene.SceneId, subjects[1].SubjectId, false);
        }
        StructuredSceneProfileService.Save(project, scenes);

        var consistent = Descendants(page).OfType<CheckBox>().FirstOrDefault(x => x.Name == "NativeConsistent")
                         ?? throw new InvalidOperationException("Scene V3 probe: Consistent assente.");
        consistent.IsChecked = true;
        Pump();
        SingleWindowSubjectStyleUi.Refresh(window);
        SingleWindowStructuredSceneUi.Refresh(window);
        Pump();

        page = pageHost.Content as Control
               ?? throw new InvalidOperationException("Scene V3 probe: pagina persa dopo Consistent.");
        var membership = Require<StackPanel>(page, "DiezSubjectSceneMembership");
        var list = Require<ListBox>(page, "SubjectSceneListBox");
        if (!membership.IsVisible || !list.IsVisible)
            throw new InvalidOperationException("Scene V3 probe: list box scene non visibile nel Consistent.");
        var checks = (list.ItemsSource as System.Collections.IEnumerable)?.Cast<object>().OfType<CheckBox>().ToList() ?? [];
        if (checks.Count != 2)
            throw new InvalidOperationException("Scene V3 probe: list box non contiene tutte le scene attive.");

        checks[0].IsChecked = true;
        checks[1].IsChecked = true;
        Pump();
        var persisted = StructuredSceneProfileService.Load(project);
        if (StructuredSceneProfileService.ActiveScenes(persisted).Any(scene =>
                !scene.ParticipantSubjectIds.Contains(subjects[0].SubjectId, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Scene V3 probe: membership list box non persiste SubjectId.");

        SelectByText(mode, "Ambientazione generica");
        Pump();
        if (!string.Equals(environment.Text, coloring.EnvironmentDescription, StringComparison.Ordinal))
            throw new InvalidOperationException("Scene V3 probe: tornando a Ambientazione generica non viene ripristinato il testo canonico.");

        var finalScenes = StructuredSceneProfileService.Load(project);
        if (finalScenes.Scenes.Select(x => x.SceneId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != finalScenes.Scenes.Count)
            throw new InvalidOperationException("Scene V3 probe: SceneId non univoci nello storico.");
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
    }

    private static void RenameSubject(MultiSubjectProfile model, MultiSubjectDefinition subject, string name)
    {
        if (!MultiSubjectProfileService.TryRename(model, subject, name, out var error))
            throw new InvalidOperationException("Scene V3 probe: " + error);
    }

    private static T Require<T>(Control root, string name) where T : Control =>
        Descendants(root).OfType<T>().FirstOrDefault(x => x.Name == name)
        ?? throw new InvalidOperationException($"Scene V3 probe: controllo mancante {name}.");

    private static List<string> Values(ComboBox combo) =>
        (combo.ItemsSource as System.Collections.IEnumerable)?.Cast<object>().Select(x => x?.ToString() ?? string.Empty).ToList() ?? [];

    private static void SelectByText(ComboBox combo, string value)
    {
        combo.SelectedItem = (combo.ItemsSource as System.Collections.IEnumerable)?.Cast<object>()
            .FirstOrDefault(x => string.Equals(x?.ToString(), value, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Scene V3 probe: voce combo mancante " + value);
    }

    private static void SetSession(MainWindow window, PreviewProject project, string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
        typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, path);
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
