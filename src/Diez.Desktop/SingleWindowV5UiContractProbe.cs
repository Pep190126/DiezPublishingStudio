using System.Collections;
using System.Reflection;
using Avalonia.Controls;

namespace DiezPublishingStudio;

internal static class SingleWindowV5UiContractProbe
{
    public static async Task RunAsync(MainWindow window)
    {
        var temp = Path.Combine(Path.GetTempPath(), "diez-ui-v9-" + Guid.NewGuid().ToString("N") + ".diez");
        try
        {
            typeof(MainWindow).GetField("_project", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(window, null);
            typeof(MainWindow).GetField("_currentProjectPath", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(window, null);
            SingleWindowV5StartupUi.ShowStart(window);
            var host = SingleWindowEntryPointUi.GetHost(window);
            var pageHost = PageHost(host);
            AssertText(pageHost.Content as Control, "Diez Publishing Studio");
            AssertButton(pageHost.Content as Control, "Nuovo progetto");
            AssertButton(pageHost.Content as Control, "Apri progetto .diez");

            var project = ProjectFileStore.Create("Coloring V9 Contract");
            await ProjectFileStore.SaveAsync(temp, project);
            SetSession(window, project, temp);
            SingleWindowV5StartupUi.ShowStart(window);
            AssertText(pageHost.Content as Control, "Quale libro stai preparando?");
            if (!Descendants(pageHost.Content as Control).OfType<ComboBox>().Any())
                throw new InvalidOperationException("La scelta del Tipo libro non è visibile.");

            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            await ProjectFileStore.SaveAsync(temp, project);
            SingleWindowEntryPointUi.Invoke(host, "OpenQuantity");
            SingleWindowColoringProfileUi.EnsureCurrentPage(window);
            SingleWindowImageSpecsUi.EnsureCurrentPage(window);
            SingleWindowConsistencyCriteriaUi.EnsureCurrentPage(window);
            var quantityPage = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina quantità assente.");

            AssertText(quantityPage, "Quante immagini vuoi creare?");
            if (Descendants(quantityPage).OfType<RadioButton>().Any())
                throw new InvalidOperationException("La pagina Coloring contiene ancora radio legacy.");
            if (!Descendants(quantityPage).OfType<TextBox>().Any(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly && !t.AcceptsReturn))
                throw new InvalidOperationException("Il numero immagini non è digitabile.");

            AssertText(quantityPage, "Soggetto/i — scelta e descrizione");
            AssertText(quantityPage, "Ambiente / scenario — descrizione");
            var subject = NamedTextBox(quantityPage, "ColoringSubjectDescription");
            var environment = NamedTextBox(quantityPage, "ColoringEnvironmentDescription");
            if (!subject.AcceptsReturn || !environment.AcceptsReturn || !subject.IsUndoEnabled || !environment.IsUndoEnabled)
                throw new InvalidOperationException("I box Soggetto/Ambiente devono essere multilinea con Undo/Redo.");
            subject.Text = "Un piccolo drago sorridente con grandi ali, seduto mentre legge un libro aperto.";
            environment.Text = "Biblioteca magica con scaffali semplici, una finestra ad arco e poche stelle decorative sullo sfondo.";

            AssertText(quantityPage, "SOLO 2 COLORI");
            AssertText(quantityPage, "#000000");
            AssertText(quantityPage, "#FFFFFF");

            var style = NamedCombo(quantityPage, "ColoringStyle");
            var styles = Values(style);
            foreach (var expected in new[] { "Bold & Easy", "Line Art pulita", "Line Art dettagliata", "Kawaii / Cartoon", "Mandala / Pattern" })
                if (!styles.Contains(expected, StringComparer.Ordinal)) throw new InvalidOperationException($"Stile Coloring mancante: {expected}");

            var lineWeight = NamedCombo(quantityPage, "ColoringLineWeight");
            var lineWeights = Values(lineWeight);
            foreach (var expected in new[]
            {
                "Molto spesso — Extra Bold", "Spesso — Bold", "Medio", "Sottile — Fine",
                "Molto sottile — Extra Fine", "Variabile — contorni principali più spessi, dettagli più sottili"
            })
                if (!lineWeights.Contains(expected, StringComparer.Ordinal))
                    throw new InvalidOperationException($"Spessore linee mancante: {expected}");

            style.SelectedItem = "Line Art dettagliata";
            if (!string.Equals(lineWeight.SelectedItem?.ToString(), "Sottile — Fine", StringComparison.Ordinal))
                throw new InvalidOperationException("Line Art dettagliata deve proporre linee sottili come default.");
            lineWeight.SelectedItem = "Molto sottile — Extra Fine";

            foreach (var name in new[] { "ColoringAudience", "ColoringDifficulty", "ColoringComplexity", "ColoringDensity", "ColoringBackground", "ColoringWhiteSpace" })
                _ = NamedCombo(quantityPage, name);

            AssertText(quantityPage, "Specifiche immagine / stampa");
            _ = NamedCombo(quantityPage, "ImageSpecPreset");
            _ = NamedCombo(quantityPage, "ImageSpecOrientation");
            _ = NamedCombo(quantityPage, "ImageSpecQuality");
            _ = NamedCombo(quantityPage, "ImageSpecLineDetail");
            foreach (var name in new[] { "ImageSpecAspectRatio", "ImageSpecPixelWidth", "ImageSpecPixelHeight", "ImageSpecDpi", "ImageSpecSafeMargin" })
            {
                var field = NamedTextBox(quantityPage, name);
                if (!field.IsEnabled || field.IsReadOnly) throw new InvalidOperationException($"Specifica immagine non editabile: {name}");
            }

            var consistent = Descendants(quantityPage).OfType<CheckBox>().FirstOrDefault(c =>
                (c.Content?.ToString() ?? string.Empty).StartsWith("Consistent", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Il comando Consistent non è visibile.");
            consistent.IsChecked = false;
            SingleWindowConsistencyCriteriaUi.EnsureCurrentPage(window);
            var criteriaPanel = Descendants(quantityPage).FirstOrDefault(c => string.Equals(c.Name, "DiezConsistencyCriteriaPanel", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("Il pannello criteri Consistent non è stato creato.");
            if (criteriaPanel.IsVisible) throw new InvalidOperationException("I criteri Consistent sono visibili con Consistent OFF.");

            consistent.IsChecked = true;
            if (!criteriaPanel.IsVisible) throw new InvalidOperationException("I criteri Consistent non compaiono con Consistent ON.");
            foreach (var expected in new[] { "Personaggio", "Stile", "Palette / colori", "Tratto / dettaglio", "Ambientazioni / oggetti ricorrenti", "Composizione" })
                AssertText(criteriaPanel, expected);
            var levelCombos = Descendants(criteriaPanel).OfType<ComboBox>().Where(c => (c.Name ?? string.Empty).StartsWith("ConsistencyLevel_", StringComparison.Ordinal)).ToList();
            if (levelCombos.Count != 6) throw new InvalidOperationException($"Criteri attesi 6, trovati {levelCombos.Count}.");
            foreach (var combo in levelCombos)
                if (combo.ItemsSource is not IEnumerable source || source.Cast<object>().Count() != 3)
                    throw new InvalidOperationException($"{combo.Name} non offre tre livelli.");

            SingleWindowEntryPointUi.Invoke(host, "OpenPrompt", 12);
            SingleWindowColoringProfileUi.EnsureCurrentPage(window);
            SingleWindowImageSpecsUi.EnsureCurrentPage(window);
            SingleWindowPromptTargetAiUi.EnsureCurrentPage(window);
            var promptPage = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina prompt assente.");
            AssertText(promptPage, "DEVE FARE");
            AssertText(promptPage, "NON DEVE FARE");
            AssertText(promptPage, "PROMPT — modificabile");
            AssertText(promptPage, "AI per cui preparare il prompt specifico");

            var editors = Descendants(promptPage).OfType<TextBox>().Where(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly).ToList();
            if (editors.Count < 3) throw new InvalidOperationException("I tre box editabili non sono visibili.");
            if (editors.Take(3).Any(t => !t.IsUndoEnabled)) throw new InvalidOperationException("Undo/Redo non è abilitato sui tre editor.");
            editors[0].Text = string.Empty;
            editors[1].Text = string.Empty;

            var basePrompt = SingleWindowEntryPointUi.Invoke(host, "BuildPrompt", project, 12)?.ToString() ?? string.Empty;
            editors[2].Text = basePrompt;
            var richPrompt = editors[2].Text ?? string.Empty;
            foreach (var required in new[]
            {
                "PROFILO EDITORIALE COLORING BOOK:",
                "SOGGETTO/I RICHIESTI DALL'UTENTE:", "piccolo drago sorridente", "AMBIENTE / SCENARIO RICHIESTO DALL'UTENTE:", "Biblioteca magica",
                "Line Art dettagliata", "Molto sottile — Extra Fine", "linee molto sottili", "ESATTAMENTE DUE SOLI COLORI", "#000000", "#FFFFFF",
                "Nessun terzo valore cromatico", "SPECIFICHE TECNICHE:", "Dimensioni finali", "Aspect ratio", "Risoluzione target", "DPI", "Qualità", "Margine di sicurezza", "Bleed / abbondanza"
            })
                if (!richPrompt.Contains(required, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Il prompt Coloring non è abbastanza ricco: manca '{required}'.");
            if (richPrompt.Length < 1500)
                throw new InvalidOperationException($"Prompt Coloring troppo breve ({richPrompt.Length} caratteri): deve essere corposo anche con breve input utente.");

            var targetAi = NamedCombo(promptPage, "PromptTargetAi");
            var targetNames = Values(targetAi);
            foreach (var expected in new[] { "Generico / nessuna AI specifica", "ChatGPT / OpenAI", "Gemini", "Altra / nuova AI" })
                if (!targetNames.Contains(expected, StringComparer.Ordinal)) throw new InvalidOperationException($"Provider prompt mancante: {expected}");
            AssertButton(promptPage, "Prepara prompt per AI scelta");
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static ComboBox NamedCombo(Control root, string name) =>
        Descendants(root).OfType<ComboBox>().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"ComboBox mancante: {name}");

    private static TextBox NamedTextBox(Control root, string name) =>
        Descendants(root).OfType<TextBox>().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"TextBox mancante: {name}");

    private static List<string> Values(ComboBox combo) =>
        combo.ItemsSource is IEnumerable source ? source.Cast<object>().Select(x => x.ToString() ?? string.Empty).ToList() : [];

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
                case Border border when border.Child is Control child: stack.Push(child); break;
                case ScrollViewer scroll when scroll.Content is Control child: stack.Push(child); break;
                case ContentControl content when content.Content is Control child: stack.Push(child); break;
            }
        }
    }
}
