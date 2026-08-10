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
        var quantity = Descendants(coloring).OfType<TextBox>().FirstOrDefault(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly && !t.AcceptsReturn)
            ?? throw new InvalidOperationException("Il campo numero immagini non è digitabile.");
        RequireRendered(quantity, 80, 32, "Numero immagini");
        AssertText(coloring, "Soggetto/i — scelta e descrizione");
        AssertText(coloring, "Ambiente / scenario — descrizione");
        AssertText(coloring, "SOLO 2 COLORI");
        var subject = RequireEditableTextBox(coloring, "ColoringSubjectDescription", true);
        var environment = RequireEditableTextBox(coloring, "ColoringEnvironmentDescription", true);
        RequireRendered(subject, 220, 60, "Soggetto/i");
        RequireRendered(environment, 220, 60, "Ambiente");
        RequireCombo(coloring, "ColoringStyle");
        RequireCombo(coloring, "ColoringLineWeight");
        await AssertImageSpecsAsync(window, coloring, true);
        AssertConsistentVisibility(coloring);

        SingleWindowEntryPointUi.Invoke(host, "OpenPrompt", 12);
        SingleWindowColoringProfileUi.EnsureCurrentPage(window);
        SingleWindowImageSpecsUi.EnsureCurrentPage(window);
        SingleWindowPromptTargetAiUi.EnsureCurrentPage(window);
        SingleWindowAiImageContextUi.EnsureCurrentPage(window);
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
        AssertText(prompt, "AI per cui preparare il prompt specifico");
        var provider = RequireCombo(prompt, "PromptTargetAi");
        foreach (var expected in new[] { "Generico / nessuna AI specifica", "ChatGPT / OpenAI", "Gemini", "Altra / nuova AI" })
            if (!Values(provider).Contains(expected, StringComparer.Ordinal))
                throw new InvalidOperationException($"Provider prompt mancante: {expected}");
        AssertButton(prompt, "Prepara prompt per AI scelta");

        BookTypeProfileService.Set(project, BookTypeProfileService.ImageCollection);
        OpenQuantity(window, host);
        await WaitForLayoutAsync();
        var collection = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina Raccolta immagini assente.");
        AssertText(collection, "Profilo della Raccolta immagini");
        var collectionColor = RequireCombo(collection, "ImageCollectionColorMode");
        foreach (var expected in new[] { "Colore pieno", "Scala di grigi — con sfumature", "Bianco e nero puro — 2 colori" })
            if (!Values(collectionColor).Contains(expected, StringComparer.Ordinal))
                throw new InvalidOperationException($"Resa cromatica Raccolta immagini mancante: {expected}");
        await AssertImageSpecsAsync(window, collection, false);
        AssertNoText(collection, "Vincolo fisso Coloring: SOLO 2 COLORI");

        BookTypeProfileService.Set(project, BookTypeProfileService.IllustratedBook);
        OpenQuantity(window, host);
        await WaitForLayoutAsync();
        var illustrated = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina Libro illustrato assente.");
        AssertText(illustrated, "Profilo delle illustrazioni del Libro illustrato");
        RequireCombo(illustrated, "ImageCollectionColorMode");
        await AssertImageSpecsAsync(window, illustrated, false);
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
        SingleWindowAiImageContextUi.EnsureCurrentPage(window);
        SingleWindowVisibleInputsUi.Apply(window);
    }

    private static async Task AssertImageSpecsAsync(MainWindow window, Control page, bool testCustom)
    {
        AssertText(page, "Specifiche immagine / stampa");
        var preset = RequireCombo(page, "ImageSpecPreset");
        var resolution = RequireCombo(page, "ImageSpecResolutionClass");
        var values = Values(resolution);
        foreach (var expected in ResolutionClasses)
            if (!values.Contains(expected, StringComparer.Ordinal))
                throw new InvalidOperationException($"Classe risoluzione mancante: {expected}");
        foreach (var name in new[] { "ImageSpecAspectRatio", "ImageSpecSafeMargin" })
            RequireEditableTextBox(page, name, false);
        RequireCombo(page, "ImageSpecQuality");
        RequireCombo(page, "ImageSpecLineDetail");

        if (!testCustom) return;

        SelectByText(preset, "Personalizzato");
        await WaitForLayoutAsync();
        SingleWindowCustomDimensionsUi.EnsureCurrentPage(window);
        SingleWindowVisibleInputsUi.Apply(window);
        var physical = Descendants(page).FirstOrDefault(c => string.Equals(c.Name, "CustomImageSpecPhysicalInputs", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Pannello dimensioni fisiche personalizzate mancante.");
        if (!physical.IsVisible) throw new InvalidOperationException("Le dimensioni fisiche personalizzate non compaiono scegliendo Personalizzato.");
        var customWidth = RequireNumeric(page, "CustomImageSpecWidth");
        var customHeight = RequireNumeric(page, "CustomImageSpecHeight");
        RequireRendered(customWidth, 100, 32, "Larghezza personalizzata");
        RequireRendered(customHeight, 100, 32, "Altezza personalizzata");

        SelectByText(resolution, "Personalizzata — usa i pixel indicati");
        await WaitForLayoutAsync();
        var pixels = Descendants(page).FirstOrDefault(c => string.Equals(c.Name, "CustomImageSpecPixelInputs", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Pannello pixel personalizzati mancante.");
        if (!pixels.IsVisible) throw new InvalidOperationException("I pixel personalizzati non compaiono scegliendo risoluzione Personalizzata.");
        var customPixelWidth = RequireNumeric(page, "CustomImageSpecPixelWidth");
        var customPixelHeight = RequireNumeric(page, "CustomImageSpecPixelHeight");
        var customDpi = RequireNumeric(page, "CustomImageSpecDpi");
        RequireRendered(customPixelWidth, 110, 32, "Larghezza px personalizzata");
        RequireRendered(customPixelHeight, 110, 32, "Altezza px personalizzata");
        RequireRendered(customDpi, 100, 32, "DPI");
    }

    private static void AssertConsistentVisibility(Control page)
    {
        var consistent = Descendants(page).OfType<CheckBox>().FirstOrDefault(c =>
            (c.Content?.ToString() ?? string.Empty).StartsWith("Consistent", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Consistent non è visibile.");
        var criteria = Descendants(page).FirstOrDefault(c => string.Equals(c.Name, "DiezConsistencyCriteriaPanel", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Pannello criteri Consistent mancante.");
        if (consistent.IsChecked != true && criteria.IsVisible)
            throw new InvalidOperationException("I criteri Consistent devono essere nascosti quando Consistent è OFF.");
    }

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
        await Task.Delay(80);
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
