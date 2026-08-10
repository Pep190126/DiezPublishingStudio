using System.Collections;
using System.Reflection;
using Avalonia.Controls;

namespace DiezPublishingStudio;

internal static class SingleWindowV5UiContractProbe
{
    public static async Task RunAsync(MainWindow window)
    {
        var temp = Path.Combine(Path.GetTempPath(), "diez-ui-v6-" + Guid.NewGuid().ToString("N") + ".diez");
        try
        {
            // 1. No project: the first visible logical page must be the guided start page.
            typeof(MainWindow).GetField("_project", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(window, null);
            typeof(MainWindow).GetField("_currentProjectPath", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(window, null);
            SingleWindowV5StartupUi.ShowStart(window);
            var host = SingleWindowEntryPointUi.GetHost(window);
            var pageHost = PageHost(host);
            AssertText(pageHost.Content as Control, "Diez Publishing Studio");
            AssertButton(pageHost.Content as Control, "Nuovo progetto");
            AssertButton(pageHost.Content as Control, "Apri progetto .diez");

            // 2. Existing project: book type choice must be the first page.
            var project = ProjectFileStore.Create("Coloring V6 Contract");
            await ProjectFileStore.SaveAsync(temp, project);
            SetSession(window, project, temp);
            SingleWindowV5StartupUi.ShowStart(window);
            AssertText(pageHost.Content as Control, "Quale libro stai preparando?");
            if (!Descendants(pageHost.Content as Control).OfType<ComboBox>().Any())
                throw new InvalidOperationException("La scelta del Tipo libro non è visibile.");

            // 3. Coloring: exact quantity page, no legacy radios.
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            await ProjectFileStore.SaveAsync(temp, project);
            SingleWindowEntryPointUi.Invoke(host, "OpenQuantity");
            SingleWindowConsistencyCriteriaUi.EnsureCurrentPage(window);
            AssertText(pageHost.Content as Control, "Quante immagini vuoi creare?");
            var quantityEditors = Descendants(pageHost.Content as Control).OfType<TextBox>()
                .Where(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly).ToList();
            if (!quantityEditors.Any(t => !t.AcceptsReturn))
                throw new InvalidOperationException("Il numero immagini non è digitabile.");
            if (Descendants(pageHost.Content as Control).OfType<RadioButton>().Any())
                throw new InvalidOperationException("La pagina Coloring contiene ancora radio legacy.");

            // 4. Consistent OFF: criteria must not occupy the page.
            var consistent = Descendants(pageHost.Content as Control).OfType<CheckBox>().FirstOrDefault(c =>
                (c.Content?.ToString() ?? string.Empty).StartsWith("Consistent", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Il comando Consistent non è visibile.");
            consistent.IsChecked = false;
            SingleWindowConsistencyCriteriaUi.EnsureCurrentPage(window);
            var criteriaPanel = Descendants(pageHost.Content as Control)
                .FirstOrDefault(c => string.Equals(c.Name, "DiezConsistencyCriteriaPanel", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("Il pannello criteri Consistent non è stato creato.");
            if (criteriaPanel.IsVisible)
                throw new InvalidOperationException("I criteri Consistent sono visibili anche con Consistent OFF.");

            // 5. Consistent ON: all six criteria, three levels each, and All consistent must appear.
            consistent.IsChecked = true;
            if (!criteriaPanel.IsVisible)
                throw new InvalidOperationException("I criteri Consistent non compaiono quando Consistent è ON.");
            AssertText(criteriaPanel, "Quali aspetti devono restare coerenti?");
            AssertText(criteriaPanel, "Personaggio");
            AssertText(criteriaPanel, "Stile");
            AssertText(criteriaPanel, "Palette / colori");
            AssertText(criteriaPanel, "Tratto / dettaglio");
            AssertText(criteriaPanel, "Ambientazioni / oggetti ricorrenti");
            AssertText(criteriaPanel, "Composizione");
            var allLocked = Descendants(criteriaPanel).OfType<CheckBox>().FirstOrDefault(c =>
                (c.Content?.ToString() ?? string.Empty).StartsWith("Tutto coerente", StringComparison.OrdinalIgnoreCase));
            if (allLocked is null) throw new InvalidOperationException("Manca l'opzione Tutto coerente.");

            var levelCombos = Descendants(criteriaPanel).OfType<ComboBox>()
                .Where(c => (c.Name ?? string.Empty).StartsWith("ConsistencyLevel_", StringComparison.Ordinal)).ToList();
            if (levelCombos.Count != 6)
                throw new InvalidOperationException($"Criteri di coerenza attesi: 6; trovati: {levelCombos.Count}.");
            foreach (var combo in levelCombos)
            {
                if (combo.ItemsSource is not IEnumerable source || source.Cast<object>().Count() != 3)
                    throw new InvalidOperationException($"Il criterio {combo.Name} non offre i tre livelli previsti.");
            }

            allLocked.IsChecked = true;
            if (levelCombos.Any(c => !string.Equals(c.SelectedItem?.ToString(), "Da mantenere", StringComparison.Ordinal)))
                throw new InvalidOperationException("Tutto coerente non imposta tutti i criteri su Da mantenere.");

            // 6. Prompt page: the three human-facing editors must exist and support undo/redo.
            SingleWindowEntryPointUi.Invoke(host, "OpenPrompt", 12);
            AssertText(pageHost.Content as Control, "DEVE FARE");
            AssertText(pageHost.Content as Control, "NON DEVE FARE");
            AssertText(pageHost.Content as Control, "PROMPT — modificabile");
            var editors = Descendants(pageHost.Content as Control).OfType<TextBox>()
                .Where(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly).ToList();
            if (editors.Count < 3) throw new InvalidOperationException("I tre box editabili non sono visibili.");
            if (editors.Take(3).Any(t => !t.IsUndoEnabled))
                throw new InvalidOperationException("Undo/Redo non è abilitato sui tre editor.");
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static void SetSession(MainWindow window, PreviewProject project, string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
        typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, path);
    }

    private static ContentControl PageHost(object host) =>
        host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
        ?? throw new InvalidOperationException("PageHost single-window non disponibile.");

    private static void AssertText(Control? root, string expected)
    {
        if (!Descendants(root).OfType<TextBlock>().Any(t => (t.Text ?? string.Empty).Contains(expected, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Testo UI mancante: {expected}");
    }

    private static void AssertButton(Control? root, string expected)
    {
        if (!Descendants(root).OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), expected, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Pulsante UI mancante: {expected}");
    }

    private static IEnumerable<Control> Descendants(Control? root)
    {
        if (root is null) yield break;
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
                case Border border when border.Child is Control child:
                    stack.Push(child);
                    break;
                case ScrollViewer scroll when scroll.Content is Control child:
                    stack.Push(child);
                    break;
                case ContentControl content when content.Content is Control child:
                    stack.Push(child);
                    break;
            }
        }
    }
}
