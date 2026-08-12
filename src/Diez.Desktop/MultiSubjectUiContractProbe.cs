using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class MultiSubjectUiContractProbe
{
    public static async Task RunAsync(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
                       ?? throw new InvalidOperationException("Multi-subject probe: page host assente.");
        var tempPath = Path.Combine(Path.GetTempPath(), "diez-multisubject-ui-" + Guid.NewGuid().ToString("N") + ".diez");
        try
        {
            var project = ProjectFileStore.Create("Multi Subject UI Contract");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            var p = BookTypePromptProfileService.LoadColoring(project);
            p.SubjectDescription = "animali della foresta";
            p.Style = "Kawaii";
            BookTypePromptProfileService.SaveColoring(project, p);
            await ProjectFileStore.SaveAsync(tempPath, project);
            SetSession(window, project, tempPath);

            SingleWindowNativeV11Ui.ShowQuantity(window, host);
            await WaitAsync();
            SingleWindowColoringStylePolicyUi.Refresh(window);
            SingleWindowSubjectStyleUi.Refresh(window);
            SingleWindowMultiSubjectLabelUi.Refresh(window);
            await WaitAsync();

            var page = pageHost.Content as Control ?? throw new InvalidOperationException("Multi-subject probe: pagina Quantità assente.");
            var subject = Require<TextBox>(page, "VisualSubjectInstructions");
            var originalSubjectReference = subject;
            var enabled = Require<CheckBox>(page, "MultiSubjectEnabled");
            var count = Require<NumericUpDown>(page, "MultiSubjectCount");
            var selector = Require<ComboBox>(page, "MultiSubjectSelector");
            var name = Require<TextBox>(page, "MultiSubjectName");
            Require<Button>(page, "MultiSubjectAdd");
            Require<Button>(page, "MultiSubjectRemove");

            if (count.Maximum != MultiSubjectProfileService.MaxSubjects || MultiSubjectProfileService.MaxSubjects != 12)
                throw new InvalidOperationException("Multi-subject probe: massimo soggetti non è 12.");
            if (enabled.IsChecked == true)
                throw new InvalidOperationException("Multi-subject probe: modalità Multi deve essere facoltativa/OFF per default.");
            if (!HasVisibleLabel(page, "Tema / gruppo di soggetti"))
                throw new InvalidOperationException("Multi-subject probe: label tema/gruppo non attiva con Multi OFF.");
            if (!(subject.Watermark ?? string.Empty).Contains("gruppi/temi", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Multi-subject probe: placeholder OFF non guida verso gruppi/temi.");

            enabled.IsChecked = true;
            count.Value = 3;
            await WaitAsync(240);
            var model = MultiSubjectProfileService.Load(project);
            MultiSubjectProfileService.SetCount(model, 3);
            var subjects = MultiSubjectProfileService.ActiveSubjects(model).ToList();
            Rename(model, subjects[0], "Milo");
            Rename(model, subjects[1], "Luna");
            Rename(model, subjects[2], "Toby");
            subjects[0].Description = "gatto piccolo con macchia a cuore sopra l'occhio sinistro";
            subjects[1].Description = "cane con orecchie lunghe e morbide";
            subjects[2].Description = "coniglio giovane con un orecchio piegato";
            model.ActiveSubjectId = subjects[0].SubjectId;
            MultiSubjectProfileService.Save(project, model);
            await ProjectFileStore.SaveAsync(tempPath, project);
            SingleWindowSubjectStyleUi.Refresh(window);
            SingleWindowMultiSubjectLabelUi.Refresh(window);
            await WaitAsync();

            page = pageHost.Content as Control ?? throw new InvalidOperationException("Multi-subject probe: pagina persa.");
            subject = Require<TextBox>(page, "VisualSubjectInstructions");
            if (!ReferenceEquals(subject, originalSubjectReference))
                throw new InvalidOperationException("Multi-subject probe: è stato creato un secondo box descrizione invece di riusare quello esistente.");
            if (!HasVisibleLabel(page, "Descrizione — Milo"))
                throw new InvalidOperationException("Multi-subject probe: label descrizione non segue il soggetto attivo.");
            if (!string.Equals(subject.Text, subjects[0].Description, StringComparison.Ordinal))
                throw new InvalidOperationException("Multi-subject probe: box descrizione non carica la descrizione legata al SubjectId.");
            if (Values(selector).Count != 3)
                throw new InvalidOperationException("Multi-subject probe: selector non mostra tre soggetti.");
            if (string.IsNullOrWhiteSpace(name.Text) || !string.Equals(name.Text, "Milo", StringComparison.Ordinal))
                throw new InvalidOperationException("Multi-subject probe: nome soggetto attivo non editabile/visibile.");

            var stableIds = subjects.Select(x => x.SubjectId).ToArray();
            MultiSubjectProfileService.SetCount(model, 2);
            MultiSubjectProfileService.Save(project, model);
            var afterReduce = MultiSubjectProfileService.Load(project);
            if (afterReduce.Subjects.Count < 3 || !stableIds.All(id => afterReduce.Subjects.Any(x => string.Equals(x.SubjectId, id, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("Multi-subject probe: riduzione numero cancella lo storico SubjectId.");
            MultiSubjectProfileService.SetCount(afterReduce, 3);
            afterReduce.ActiveSubjectId = stableIds[0];
            MultiSubjectProfileService.Save(project, afterReduce);

            var consistent = Descendants(page).OfType<CheckBox>().FirstOrDefault(x => x.Name == "NativeConsistent")
                             ?? throw new InvalidOperationException("Multi-subject probe: Consistent assente.");
            consistent.IsChecked = true;
            await WaitAsync();
            SingleWindowSubjectStyleUi.Refresh(window);
            await WaitAsync();
            page = pageHost.Content as Control ?? throw new InvalidOperationException("Multi-subject probe: pagina persa dopo Consistent.");
            var subjectConsistency = Require<StackPanel>(page, "DiezSubjectConsistencyScope");
            if (!subjectConsistency.IsVisible)
                throw new InvalidOperationException("Multi-subject probe: Consistent per soggetto non visibile con Multi ON.");
            Require<ComboBox>(page, "ConsistencySubjectSelector");
            foreach (var key in new[] { "outfit", "expression", "action", "framing", "co_scene" })
                Require<ComboBox>(page, "SubjectConsistencyLevel_" + key);

            var legacyCharacter = Descendants(page).OfType<ComboBox>().FirstOrDefault(x => x.Name == "ConsistencyLevel_character");
            if (legacyCharacter is not null && legacyCharacter.IsVisible)
                throw new InvalidOperationException("Multi-subject probe: il criterio generico 'personaggio può variare' resta visibile in modalità per-soggetto.");

            var style = Require<ComboBox>(page, "ColoringStyle");
            Select(style, "Custom");
            await WaitAsync();
            SingleWindowSubjectStyleUi.Refresh(window);
            await WaitAsync();
            var custom = Require<TextBox>(page, "ColoringCustomStyleNotes");
            var saveCustom = Require<CheckBox>(page, "ColoringSaveCustomStyle");
            if (!custom.IsVisible || !saveCustom.IsVisible)
                throw new InvalidOperationException("Multi-subject probe: descrizione Custom HARD / opt-in libreria non visibili.");
            if (!HasVisibleLabel(page, "Stile Custom — descrizione HARD"))
                throw new InvalidOperationException("Multi-subject probe: Custom è ancora presentato come nota SOFT.");

            const string customDefinition = "rounded editorial ink style with playful asymmetry, tiny floral accents and clean organic contours";
            custom.Text = customDefinition;
            await WaitAsync();
            if (!string.Equals(ColoringIndependentHardProfileService.Resolve(project).Style, customDefinition, StringComparison.Ordinal))
                throw new InvalidOperationException("Multi-subject probe: testo Custom non diventa STYLE HARD.");
            saveCustom.IsChecked = true;
            await WaitAsync();
            if (!CustomStyleLibraryService.Load().Any(x => string.Equals(x.Definition, customDefinition, StringComparison.Ordinal)))
                throw new InvalidOperationException("Multi-subject probe: opt-in Custom non salva lo stile riutilizzabile.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static void Rename(MultiSubjectProfile model, MultiSubjectDefinition subject, string name)
    {
        if (!MultiSubjectProfileService.TryRename(model, subject, name, out var error))
            throw new InvalidOperationException("Multi-subject probe: " + error);
    }

    private static T Require<T>(Control root, string name) where T : Control =>
        Descendants(root).OfType<T>().FirstOrDefault(x => x.Name == name)
        ?? throw new InvalidOperationException($"Multi-subject probe: controllo mancante {name}.");

    private static bool HasVisibleLabel(Control root, string text) =>
        Descendants(root).OfType<TextBlock>().Any(x => x.IsVisible && string.Equals(x.Text, text, StringComparison.Ordinal));

    private static List<string> Values(ComboBox combo)
    {
        if (combo.ItemsSource is not IEnumerable source) return [];
        return source.Cast<object>().Select(x => x?.ToString() ?? string.Empty).ToList();
    }

    private static void Select(ComboBox combo, string text)
    {
        if (combo.ItemsSource is not IEnumerable source) throw new InvalidOperationException("Multi-subject probe: ItemsSource assente.");
        combo.SelectedItem = source.Cast<object>().FirstOrDefault(x => string.Equals(x?.ToString(), text, StringComparison.OrdinalIgnoreCase))
                             ?? throw new InvalidOperationException("Multi-subject probe: voce mancante " + text);
    }

    private static void SetSession(MainWindow window, PreviewProject project, string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
        typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, path);
    }

    private static async Task WaitAsync(int ms = 160)
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
