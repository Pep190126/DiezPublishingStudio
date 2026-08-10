using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class SingleWindowInstallerUiProbe
{
    private static readonly string[] ResolutionClasses =
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
        SetSession(window, null, null);
        SingleWindowV5StartupUi.ShowStart(window);
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = PageHost(host);
        await WaitForLayoutAsync();
        AssertText(pageHost.Content as Control, "Diez Publishing Studio");
        AssertButton(pageHost.Content as Control, "Nuovo progetto");
        AssertButton(pageHost.Content as Control, "Apri progetto .diez");

        var tempPath = Path.Combine(Path.GetTempPath(), "diez-installer-ui-contract-" + Guid.NewGuid().ToString("N") + ".diez");
        var project = ProjectFileStore.Create("Installer UI Contract");
        await ProjectFileStore.SaveAsync(tempPath, project);
        SetSession(window, project, tempPath);
        SingleWindowV5StartupUi.ShowStart(window);
        await WaitForLayoutAsync();
        AssertText(pageHost.Content as Control, "Quale libro stai preparando?");
        var typeChoice = Descendants(pageHost.Content as Control).OfType<ComboBox>().FirstOrDefault()
            ?? throw new InvalidOperationException("La scelta del Tipo libro non è visibile.");
        var types = Values(typeChoice);
        foreach (var expected in new[] { BookTypeProfileService.ColoringBook, BookTypeProfileService.ImageCollection, BookTypeProfileService.IllustratedBook })
            if (!types.Contains(expected, StringComparer.Ordinal))
                throw new InvalidOperationException($"Tipo libro mancante: {expected}");

        BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
        OpenQuantity(window, host);
        await WaitForLayoutAsync();
        var coloring = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina Coloring assente.");
        AssertText(coloring, "Quante immagini vuoi creare?");
        if (Descendants(coloring).OfType<RadioButton>().Any())
            throw new InvalidOperationException("La pagina Coloring contiene radio legacy.");

        var quantity = RequireNumeric(coloring, "ExactImageCount");
        RequireRendered(quantity, 100, 32, "Numero immagini");
        quantity.Value = 12;
        await WaitForLayoutAsync();

        var subject = RequireEditableTextBox(coloring, "VisualSubjectInstructions", true);
        var environment = RequireEditableTextBox(coloring, "VisualEnvironmentInstructions", true);
        RequireRendered(subject, 220, 60, "Soggetto/i");
        RequireRendered(environment, 220, 60, "Ambientazione");
        subject.Text = "Bambina con cappello. Immagine 3: aggiungi un gatto.";
        environment.Text = "Parco. Immagine 3: cucina.";

        AssertText(coloring, "SOLO 2 COLORI");
        RequireCombo(coloring, "ColoringStyle");
        RequireCombo(coloring, "ColoringLineWeight");
        AssertImageSpecs(coloring, project);
        AssertConsistencyStrategies(coloring);

        SingleWindowEntryPointUi.Invoke(host, "OpenPrompt", 12);
        SingleWindowColoringProfileUi.EnsureCurrentPage(window);
        SingleWindowImageSpecsUi.EnsureCurrentPage(window);
        SingleWindowPromptTargetAiUi.EnsureCurrentPage(window);
        SingleWindowAiImageContextUi.EnsureCurrentPage(window);
        SingleWindowPersistentImageCountUi.Refresh(window);
        SingleWindowVisibleInputsUi.Apply(window);
        await WaitForLayoutAsync();
        var prompt = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina prompt assente.");
        AssertText(prompt, "DEVE FARE");
        AssertText(prompt, "NON DEVE FARE");
        AssertText(prompt, "PROMPT — modificabile");
        var editors = Descendants(prompt).OfType<TextBox>().Where(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly).ToList();
        if (editors.Count < 3) throw new InvalidOperationException("I tre box editabili non sono visibili.");
        if (editors.Take(3).Any(t => !t.IsUndoEnabled)) throw new InvalidOperationException("Undo/Redo non è abilitato sui tre box.");
        foreach (var pair in editors.Take(3).Zip(new[] { "DEVE FARE", "NON DEVE FARE", "PROMPT" }))
        {
            RequireRendered(pair.First, 220, 60, pair.Second);
            if (pair.First.BorderThickness.Left < 1 || pair.First.Background is null)
                throw new InvalidOperationException($"Il box {pair.Second} non ha un bordo/sfondo visibile.");
        }

        var title = HostTitle(host);
        if (!(title.Text ?? string.Empty).Contains("12 immagini", StringComparison.Ordinal))
            throw new InvalidOperationException("Il numero esatto di immagini non resta visibile nell'intestazione dopo il passo 1/4.");

        AssertText(prompt, "AI per cui preparare il prompt specifico");
        var provider = RequireCombo(prompt, "PromptTargetAi");
        foreach (var expected in new[] { "Generico / nessuna AI specifica", "ChatGPT / OpenAI", "Gemini", "Altra / nuova AI" })
            if (!Values(provider).Contains(expected, StringComparer.Ordinal))
                throw new InvalidOperationException($"Provider prompt mancante: {expected}");
        AssertButton(prompt, "Prepara prompt per AI scelta");

        var technicalPrompt = SingleWindowImageSpecsUi.BuildPromptBlock(project);
        if (technicalPrompt.Contains("bleed", StringComparison.OrdinalIgnoreCase) || technicalPrompt.Contains("abbondanza", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Il bleed è ricomparso nelle istruzioni creative AI.");
        if (!technicalPrompt.Contains("KDP", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Il trim KDP non viene riportato nelle specifiche AI.");

        BookTypeProfileService.Set(project, BookTypeProfileService.ImageCollection);
        OpenQuantity(window, host);
        await WaitForLayoutAsync();
        var collection = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina Raccolta immagini assente.");
        AssertText(collection, "Profilo della Raccolta immagini");
        var collectionColor = RequireCombo(collection, "ImageCollectionColorMode");
        foreach (var expected in new[] { "Colore pieno", "Scala di grigi — con sfumature", "Bianco e nero puro — 2 colori" })
            if (!Values(collectionColor).Contains(expected, StringComparer.Ordinal))
                throw new InvalidOperationException($"Resa cromatica Raccolta immagini mancante: {expected}");
        AssertImageSpecs(collection, project);
        AssertNoText(collection, "Vincolo fisso Coloring: SOLO 2 COLORI");

        BookTypeProfileService.Set(project, BookTypeProfileService.IllustratedBook);
        OpenQuantity(window, host);
        await WaitForLayoutAsync();
        var illustrated = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina Libro illustrato assente.");
        AssertText(illustrated, "Profilo delle illustrazioni del Libro illustrato");
        RequireCombo(illustrated, "ImageCollectionColorMode");
        AssertImageSpecs(illustrated, project);
        AssertNoText(illustrated, "Vincolo fisso Coloring: SOLO 2 COLORI");

        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
    }

    private static void OpenQuantity(MainWindow window, object host)
    {
        SingleWindowEntryPointUi.Invoke(host, "OpenQuantity");
        SingleWindowVisualBookIdentityUi.Apply(window);
        SingleWindowColoringProfileUi.EnsureCurrentPage(window);
        SingleWindowImageCollectionProfileUi.EnsureCurrentPage(window);
        SingleWindowImageSpecsUi.EnsureCurrentPage(window);
        SingleWindowCustomDimensionsUi.EnsureCurrentPage(window);
        SingleWindowConsistencyCriteriaUi.EnsureCurrentPage(window);
        SingleWindowVisualEssentialsUi.EnsureCurrentPage(window);
        SingleWindowAiImageContextUi.EnsureCurrentPage(window);
        SingleWindowPersistentImageCountUi.Refresh(window);
        SingleWindowVisibleInputsUi.Apply(window);
    }

    private static void AssertImageSpecs(Control page, PreviewProject project)
    {
        AssertText(page, "Specifiche immagine / stampa");
        var preset = RequireCombo(page, "ImageSpecPreset");
        var presetValues = Values(preset);
        foreach (var expected in new[] { "KDP — 5 × 8 in", "KDP — 6 × 9 in", "KDP — 7 × 10 in", "KDP — 8.5 × 11 in", "KDP — 8.27 × 11.69 in / A4", "Personalizzato" })
            if (!presetValues.Contains(expected, StringComparer.Ordinal))
                throw new InvalidOperationException($"Preset KDP mancante: {expected}");

        var resolution = RequireCombo(page, "ImageSpecResolutionClass");
        var values = Values(resolution);
        foreach (var expected in ResolutionClasses)
            if (!values.Contains(expected, StringComparer.Ordinal))
                throw new InvalidOperationException($"Classe risoluzione mancante: {expected}");
        foreach (var name in new[] { "ImageSpecAspectRatio", "ImageSpecSafeMargin" })
            RequireEditableTextBox(page, name, false);
        RequireCombo(page, "ImageSpecQuality");
        RequireCombo(page, "ImageSpecLineDetail");
        if (Descendants(page).Any(c => string.Equals(c.Name, "ImageSpecBleed", StringComparison.Ordinal) && c.IsVisible))
            throw new InvalidOperationException("Il controllo bleed è ancora visibile nel flusso di generazione immagini.");

        var prompt = SingleWindowImageSpecsUi.BuildPromptBlock(project);
        if (prompt.Contains("bleed", StringComparison.OrdinalIgnoreCase) || prompt.Contains("abbondanza", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Le specifiche AI contengono ancora bleed/abbondanza.");
    }

    private static void AssertConsistencyStrategies(Control page)
    {
        var consistent = Descendants(page).OfType<CheckBox>().FirstOrDefault(c =>
            (c.Content?.ToString() ?? string.Empty).StartsWith("Consistent", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Consistent non è visibile.");
        var criteria = Descendants(page).FirstOrDefault(c => string.Equals(c.Name, "DiezConsistencyCriteriaPanel", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Pannello criteri Consistent mancante.");

        consistent.IsChecked = true;
        if (!criteria.IsVisible) throw new InvalidOperationException("I criteri Consistent non compaiono quando Consistent è ON.");

        var level = RequireCombo(page, "ConsistencyLevel_character");
        SelectByText(level, "Può variare");
        var strategy = RequireCombo(page, "ConsistencyVariationStrategy_character");
        if (!strategy.IsVisible) throw new InvalidOperationException("La scelta di chi decide la variazione non compare con 'Può variare'.");
        foreach (var expected in new[] { "La definisco io", "La decide l’AI", "Mista: do indicazioni e l’AI completa" })
            if (!Values(strategy).Contains(expected, StringComparer.Ordinal))
                throw new InvalidOperationException($"Strategia 'Può variare' mancante: {expected}");

        var variation = RequireEditableTextBox(page, "ConsistencyVariation_character", true);
        var next = Descendants(page).OfType<Button>().FirstOrDefault(b =>
            (b.Content?.ToString() ?? string.Empty).Contains("Avanti", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Pulsante Avanti mancante nella pagina quantità.");

        variation.Text = string.Empty;
        SelectByText(strategy, "La decide l’AI");
        if (!next.IsEnabled) throw new InvalidOperationException("Con 'La decide l’AI' la descrizione deve essere facoltativa.");

        SelectByText(strategy, "La definisco io");
        if (next.IsEnabled) throw new InvalidOperationException("Con 'La definisco io' la descrizione della variazione deve essere obbligatoria.");
        variation.Text = "Lo sfondo cambia a ogni tavola.";
        if (!next.IsEnabled) throw new InvalidOperationException("La descrizione utente valida non riabilita Avanti.");

        variation.Text = string.Empty;
        SelectByText(strategy, "Mista: do indicazioni e l’AI completa");
        if (next.IsEnabled) throw new InvalidOperationException("In modalità mista le indicazioni utente devono essere obbligatorie.");
        variation.Text = "Cambia l'ambientazione; l'AI decide i dettagli secondari.";
        if (!next.IsEnabled) throw new InvalidOperationException("Le indicazioni miste valide non riabilitano Avanti.");
    }

    private static TextBlock HostTitle(object host) =>
        host.GetType().GetField("_title", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as TextBlock
        ?? throw new InvalidOperationException("Titolo del percorso single-window non disponibile.");

    private static TextBox RequireEditableTextBox(Control root, string name, bool multiline)
    {
        var box = Descendants(root).OfType<TextBox>().FirstOrDefault(x => x.Name == name)
            ?? throw new InvalidOperationException($"TextBox mancante: {name}");
        if (!box.IsEnabled || box.IsReadOnly || !box.IsUndoEnabled)
            throw new InvalidOperationException($"TextBox non editabile/undo: {name}");
        if (multiline && !box.AcceptsReturn)
            throw new InvalidOperationException($"TextBox non multilinea: {name}");
        return box;
    }

    private static NumericUpDown RequireNumeric(Control root, string name)
    {
        var number = Descendants(root).OfType<NumericUpDown>().FirstOrDefault(x => x.Name == name)
            ?? throw new InvalidOperationException($"Campo numerico mancante: {name}");
        if (!number.IsEnabled) throw new InvalidOperationException($"Campo numerico disabilitato: {name}");
        return number;
    }

    private static ComboBox RequireCombo(Control root, string name) =>
        Descendants(root).OfType<ComboBox>().FirstOrDefault(x => x.Name == name)
        ?? throw new InvalidOperationException($"ComboBox mancante: {name}");

    private static void SelectByText(ComboBox combo, string text)
    {
        if (combo.ItemsSource is not IEnumerable source)
            throw new InvalidOperationException($"Combo senza ItemsSource: {combo.Name}");
        var item = source.Cast<object>().FirstOrDefault(x => string.Equals(x.ToString(), text, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Voce combo mancante '{text}' in {combo.Name}");
        combo.SelectedItem = item;
    }

    private static List<string> Values(ComboBox combo) =>
        combo.ItemsSource is IEnumerable source ? source.Cast<object>().Select(x => x.ToString() ?? string.Empty).ToList() : [];

    private static void RequireRendered(Control control, double minWidth, double minHeight, string label)
    {
        if (!control.IsVisible || control.Opacity < 0.5)
            throw new InvalidOperationException($"Il controllo '{label}' non è visibile a video.");
        if (control.Bounds.Width < minWidth || control.Bounds.Height < minHeight)
            throw new InvalidOperationException($"Il controllo '{label}' ha dimensioni renderizzate insufficienti: {control.Bounds.Width:0.#} × {control.Bounds.Height:0.#}.");
    }

    private static async Task WaitForLayoutAsync()
    {
        await Task.Yield();
        await Task.Delay(100);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static void SetSession(MainWindow window, PreviewProject? project, string? path)
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
