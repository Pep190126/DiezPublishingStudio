using System.Collections;
using System.Reflection;
using Avalonia.Controls;

namespace DiezPublishingStudio;

internal static class SingleWindowV5UiContractProbe
{
    private static readonly string[] ExpectedResolutionClasses =
    [
        "HD — lato lungo 1280 px",
        "Full HD — lato lungo 1920 px",
        "2K — lato lungo 2560 px",
        "4K UHD — lato lungo 3840 px",
        "8K UHD — lato lungo 7680 px",
        "Stampa — dimensioni fisiche × DPI",
        "Personalizzata — usa i pixel indicati"
    ];

    public static async Task RunAsync(MainWindow window)
    {
        var temp = Path.Combine(Path.GetTempPath(), "diez-ui-v10-" + Guid.NewGuid().ToString("N") + ".diez");
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

            var project = ProjectFileStore.Create("Visual books V10 Contract");
            await ProjectFileStore.SaveAsync(temp, project);
            SetSession(window, project, temp);
            SingleWindowV5StartupUi.ShowStart(window);
            AssertText(pageHost.Content as Control, "Quale libro stai preparando?");
            if (!Descendants(pageHost.Content as Control).OfType<ComboBox>().Any())
                throw new InvalidOperationException("La scelta del Tipo libro non è visibile.");

            await TestColoringAsync(window, host, pageHost, project, temp);
            await TestImageCollectionAsync(window, host, pageHost, project, temp);
            await TestIllustratedBookAsync(window, host, pageHost, project, temp);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static async Task TestColoringAsync(MainWindow window, object host, ContentControl pageHost, PreviewProject project, string path)
    {
        BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
        await ProjectFileStore.SaveAsync(path, project);
        SingleWindowEntryPointUi.Invoke(host, "OpenQuantity");
        SingleWindowColoringProfileUi.EnsureCurrentPage(window);
        SingleWindowImageSpecsUi.EnsureCurrentPage(window);
        SingleWindowConsistencyCriteriaUi.EnsureCurrentPage(window);
        var quantityPage = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina quantità Coloring assente.");

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
            throw new InvalidOperationException("I box Soggetto/Ambiente Coloring devono essere multilinea con Undo/Redo.");
        subject.Text = "Un piccolo drago sorridente con grandi ali, seduto mentre legge un libro aperto.";
        environment.Text = "Biblioteca magica con scaffali semplici, una finestra ad arco e poche stelle decorative sullo sfondo.";

        AssertText(quantityPage, "SOLO 2 COLORI");
        AssertText(quantityPage, "#000000");
        AssertText(quantityPage, "#FFFFFF");

        var style = NamedCombo(quantityPage, "ColoringStyle");
        foreach (var expected in new[] { "Bold & Easy", "Line Art pulita", "Line Art dettagliata", "Kawaii / Cartoon", "Mandala / Pattern" })
            if (!Values(style).Contains(expected, StringComparer.Ordinal)) throw new InvalidOperationException($"Stile Coloring mancante: {expected}");

        var lineWeight = NamedCombo(quantityPage, "ColoringLineWeight");
        foreach (var expected in new[]
        {
            "Molto spesso — Extra Bold", "Spesso — Bold", "Medio", "Sottile — Fine",
            "Molto sottile — Extra Fine", "Variabile — contorni principali più spessi, dettagli più sottili"
        })
            if (!Values(lineWeight).Contains(expected, StringComparer.Ordinal))
                throw new InvalidOperationException($"Spessore linee mancante: {expected}");

        style.SelectedItem = "Line Art dettagliata";
        if (!string.Equals(lineWeight.SelectedItem?.ToString(), "Sottile — Fine", StringComparison.Ordinal))
            throw new InvalidOperationException("Line Art dettagliata deve proporre linee sottili come default.");
        lineWeight.SelectedItem = "Molto sottile — Extra Fine";

        AssertImageSpecsAndResolutionClass(quantityPage, "4K UHD — lato lungo 3840 px", 3840);

        var consistent = FindConsistent(quantityPage);
        consistent.IsChecked = false;
        SingleWindowConsistencyCriteriaUi.EnsureCurrentPage(window);
        var criteriaPanel = NamedControl(quantityPage, "DiezConsistencyCriteriaPanel");
        if (criteriaPanel.IsVisible) throw new InvalidOperationException("I criteri Consistent sono visibili con Consistent OFF.");
        consistent.IsChecked = true;
        if (!criteriaPanel.IsVisible) throw new InvalidOperationException("I criteri Consistent non compaiono con Consistent ON.");

        var consistencyCombos = Descendants(criteriaPanel).OfType<ComboBox>()
            .Where(c => (c.Name ?? string.Empty).StartsWith("ConsistencyLevel_", StringComparison.Ordinal)).ToList();
        if (consistencyCombos.Count != 6) throw new InvalidOperationException($"Criteri Coloring attesi 6, trovati {consistencyCombos.Count}.");
        var palette = consistencyCombos.First(c => c.Name == "ConsistencyLevel_palette");
        if (palette.IsEnabled || Values(palette).Count != 1 || palette.SelectedItem?.ToString() != "Da mantenere")
            throw new InvalidOperationException("Nel Coloring la palette B/N deve essere fissa e non modificabile.");
        foreach (var combo in consistencyCombos.Where(c => c != palette))
            if (Values(combo).Count != 3) throw new InvalidOperationException($"{combo.Name} non offre i tre livelli previsti.");

        SingleWindowEntryPointUi.Invoke(host, "OpenPrompt", 12);
        SingleWindowColoringProfileUi.EnsureCurrentPage(window);
        SingleWindowImageSpecsUi.EnsureCurrentPage(window);
        SingleWindowPromptTargetAiUi.EnsureCurrentPage(window);
        var promptPage = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina prompt Coloring assente.");
        AssertText(promptPage, "DEVE FARE");
        AssertText(promptPage, "NON DEVE FARE");
        AssertText(promptPage, "PROMPT — modificabile");
        AssertText(promptPage, "AI per cui preparare il prompt specifico");

        var editors = Descendants(promptPage).OfType<TextBox>().Where(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly).ToList();
        if (editors.Count < 3 || editors.Take(3).Any(t => !t.IsUndoEnabled))
            throw new InvalidOperationException("I tre editor prompt devono essere visibili e avere Undo/Redo.");
        editors[0].Text = string.Empty;
        editors[1].Text = string.Empty;
        editors[2].Text = SingleWindowEntryPointUi.Invoke(host, "BuildPrompt", project, 12)?.ToString() ?? string.Empty;
        var richPrompt = editors[2].Text ?? string.Empty;
        foreach (var required in new[]
        {
            "PROFILO EDITORIALE COLORING BOOK:", "piccolo drago sorridente", "Biblioteca magica",
            "Line Art dettagliata", "Molto sottile — Extra Fine", "ESATTAMENTE DUE SOLI COLORI", "#000000", "#FFFFFF",
            "SPECIFICHE TECNICHE:", "Classe risoluzione / qualità immagine", "4K UHD", "3840", "Aspect ratio", "DPI", "Qualità rendering"
        })
            if (!richPrompt.Contains(required, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Prompt Coloring incompleto: manca '{required}'.");
        if (richPrompt.Length < 1500)
            throw new InvalidOperationException($"Prompt Coloring troppo breve ({richPrompt.Length} caratteri).");

        AssertTargetAiCatalog(promptPage);
    }

    private static async Task TestImageCollectionAsync(MainWindow window, object host, ContentControl pageHost, PreviewProject project, string path)
    {
        BookTypeProfileService.Set(project, BookTypeProfileService.ImageCollection);
        await ProjectFileStore.SaveAsync(path, project);
        SingleWindowEntryPointUi.Invoke(host, "OpenQuantity");
        SingleWindowVisualBookIdentityUi.Apply(window);
        SingleWindowImageCollectionProfileUi.EnsureCurrentPage(window);
        SingleWindowImageSpecsUi.EnsureCurrentPage(window);
        SingleWindowConsistencyCriteriaUi.EnsureCurrentPage(window);
        var page = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina Raccolta immagini assente.");

        AssertText(page, "Profilo della Raccolta immagini");
        var colorMode = NamedCombo(page, "ImageCollectionColorMode");
        foreach (var expected in new[] { "Colore pieno", "Scala di grigi — con sfumature", "Bianco e nero puro — 2 colori" })
            if (!Values(colorMode).Contains(expected, StringComparer.Ordinal))
                throw new InvalidOperationException($"Resa cromatica Raccolta immagini mancante: {expected}");
        AssertImageSpecsAndResolutionClass(page, "Full HD — lato lungo 1920 px", 1920);

        var consistent = FindConsistent(page);
        consistent.IsChecked = true;
        var criteriaPanel = NamedControl(page, "DiezConsistencyCriteriaPanel");
        var palette = NamedCombo(criteriaPanel, "ConsistencyLevel_palette");
        if (!palette.IsEnabled || Values(palette).Count != 3)
            throw new InvalidOperationException("Nella Raccolta immagini la resa cromatica Consistent deve poter variare sui tre livelli.");

        var technical = SingleWindowImageSpecsUi.BuildPromptBlock(project);
        if (technical.Contains("ESATTAMENTE due colori", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La Raccolta immagini non deve ereditare il vincolo binario del Coloring.");
    }

    private static async Task TestIllustratedBookAsync(MainWindow window, object host, ContentControl pageHost, PreviewProject project, string path)
    {
        BookTypeProfileService.Set(project, BookTypeProfileService.IllustratedBook);
        await ProjectFileStore.SaveAsync(path, project);
        SingleWindowEntryPointUi.Invoke(host, "OpenQuantity");
        SingleWindowVisualBookIdentityUi.Apply(window);
        SingleWindowImageCollectionProfileUi.EnsureCurrentPage(window);
        SingleWindowImageSpecsUi.EnsureCurrentPage(window);
        SingleWindowConsistencyCriteriaUi.EnsureCurrentPage(window);
        var page = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina Libro illustrato assente.");

        AssertText(page, "Profilo delle illustrazioni del Libro illustrato");
        var colorMode = NamedCombo(page, "ImageCollectionColorMode");
        foreach (var expected in new[] { "Colore pieno", "Scala di grigi — con sfumature", "Bianco e nero puro — 2 colori" })
            if (!Values(colorMode).Contains(expected, StringComparer.Ordinal))
                throw new InvalidOperationException($"Resa cromatica Libro illustrato mancante: {expected}");
        AssertImageSpecsAndResolutionClass(page, "2K — lato lungo 2560 px", 2560);
        AssertNoText(page, "Vincolo fisso Coloring: SOLO 2 COLORI");

        var technical = SingleWindowImageSpecsUi.BuildPromptBlock(project);
        if (!technical.Contains("Libro illustrato", StringComparison.OrdinalIgnoreCase) ||
            technical.Contains("ESATTAMENTE due colori", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Le specifiche del Libro illustrato devono essere proprie e non Coloring.");
    }

    private static void AssertImageSpecsAndResolutionClass(Control page, string selection, int expectedLongSide)
    {
        AssertText(page, "Specifiche immagine / stampa");
        var resolution = NamedCombo(page, "ImageSpecResolutionClass");
        var available = Values(resolution);
        foreach (var expected in ExpectedResolutionClasses)
            if (!available.Contains(expected, StringComparer.Ordinal))
                throw new InvalidOperationException($"Classe risoluzione mancante: {expected}");

        resolution.SelectedItem = available.Contains(selection, StringComparer.Ordinal)
            ? resolution.ItemsSource!.Cast<object>().First(x => x.ToString() == selection)
            : throw new InvalidOperationException($"Impossibile selezionare {selection}");

        var pxW = NamedTextBox(page, "ImageSpecPixelWidth");
        var pxH = NamedTextBox(page, "ImageSpecPixelHeight");
        if (!int.TryParse(pxW.Text, out var w) || !int.TryParse(pxH.Text, out var h) || Math.Max(w, h) != expectedLongSide)
            throw new InvalidOperationException($"{selection} non aggiorna il lato lungo a {expectedLongSide}px: {pxW.Text}×{pxH.Text}.");

        foreach (var name in new[] { "ImageSpecAspectRatio", "ImageSpecPixelWidth", "ImageSpecPixelHeight", "ImageSpecDpi", "ImageSpecSafeMargin" })
        {
            var field = NamedTextBox(page, name);
            if (!field.IsEnabled || field.IsReadOnly) throw new InvalidOperationException($"Specifica immagine non editabile: {name}");
        }
        _ = NamedCombo(page, "ImageSpecQuality");
        _ = NamedCombo(page, "ImageSpecLineDetail");
    }

    private static void AssertTargetAiCatalog(Control promptPage)
    {
        var targetAi = NamedCombo(promptPage, "PromptTargetAi");
        var targetNames = Values(targetAi);
        foreach (var expected in new[] { "Generico / nessuna AI specifica", "ChatGPT / OpenAI", "Gemini", "Altra / nuova AI" })
            if (!targetNames.Contains(expected, StringComparer.Ordinal)) throw new InvalidOperationException($"Provider prompt mancante: {expected}");
        AssertButton(promptPage, "Prepara prompt per AI scelta");
    }

    private static CheckBox FindConsistent(Control root) =>
        Descendants(root).OfType<CheckBox>().FirstOrDefault(c =>
            (c.Content?.ToString() ?? string.Empty).StartsWith("Consistent", StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("Il comando Consistent non è visibile.");

    private static ComboBox NamedCombo(Control root, string name) =>
        Descendants(root).OfType<ComboBox>().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"ComboBox mancante: {name}");

    private static TextBox NamedTextBox(Control root, string name) =>
        Descendants(root).OfType<TextBox>().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"TextBox mancante: {name}");

    private static Control NamedControl(Control root, string name) =>
        Descendants(root).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Control mancante: {name}");

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

    private static void AssertNoText(Control? root, string forbidden)
    {
        if (Descendants(root).OfType<TextBlock>().Any(t => (t.Text ?? string.Empty).Contains(forbidden, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Testo UI non ammesso: {forbidden}");
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
