using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class SingleWindowV11ContractProbe
{
    public static async Task RunAsync(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost")
            ?? throw new InvalidOperationException("PageHost V11 non disponibile.");
        var tempPath = Path.Combine(Path.GetTempPath(), "diez-v12-ui-contract-" + Guid.NewGuid().ToString("N") + ".diez");
        try
        {
            var project = ProjectFileStore.Create("V12 Native UI Contract");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            await ProjectFileStore.SaveAsync(tempPath, project);
            SetSession(window, project, tempPath);

            SingleWindowNativeV11Ui.ShowStart(window);
            await WaitForLayoutAsync();

            var typePage = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina Tipo libro assente.");
            AssertText(typePage, "Quale libro stai preparando?");
            var back = Field<Button>(host, "_back") ?? throw new InvalidOperationException("Pulsante Indietro host assente.");
            if (!back.IsEnabled) throw new InvalidOperationException("Indietro è disabilitato nella pagina Tipo libro.");

            SingleWindowEntryPointUi.Invoke(host, "Back");
            await WaitForLayoutAsync();
            AssertText(pageHost.Content as Control, "Progetto aperto");

            SingleWindowNativeV11Ui.ShowBookType(window, host);
            await WaitForLayoutAsync();
            AssertText(pageHost.Content as Control, "Quale libro stai preparando?");

            SingleWindowNativeV11Ui.ShowQuantity(window, host);
            await WaitForLayoutAsync();
            var quantity = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina Quantità assente.");
            AssertText(quantity, "Quante immagini vuoi creare?");
            AssertNoButton(quantity, "Cambia Tipo libro");

            var exact = RequireNumeric(quantity, "ExactImageCount");
            RequireRendered(exact, 100, 32, "Numero immagini");
            exact.Value = 12;
            await WaitForLayoutAsync();

            var subject = RequireEditor(quantity, "VisualSubjectInstructions");
            var environment = RequireEditor(quantity, "VisualEnvironmentInstructions");
            RequireNativeEditor(subject, "Personaggio / soggetto");
            RequireNativeEditor(environment, "Ambientazione");
            subject.Text = "Bambina con cappello. Immagine 3: compare anche un gatto.";
            environment.Text = "Parco. Immagine 3: cucina.";

            var consistent = Descendants(quantity).OfType<CheckBox>().FirstOrDefault(c =>
                (c.Content?.ToString() ?? string.Empty).StartsWith("Consistent", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Consistent non visibile.");
            consistent.IsChecked = true;
            await WaitForLayoutAsync();
            var criteria = Descendants(quantity).FirstOrDefault(c => c.Name == "DiezConsistencyCriteriaPanel")
                ?? throw new InvalidOperationException("Pannello Consistent nativo assente.");
            if (!criteria.IsVisible) throw new InvalidOperationException("Pannello Consistent non visibile con Consistent ON.");

            var notes = RequireEditor(quantity, "ConsistencyNotes");
            RequireNativeEditor(notes, "Note Consistent");
            var level = RequireCombo(quantity, "ConsistencyLevel_character");
            SelectByText(level, "Può variare");
            await WaitForLayoutAsync();
            var strategy = RequireCombo(quantity, "ConsistencyVariationStrategy_character");
            foreach (var expected in new[] { "La definisco io", "La decide l’AI", "Mista: do indicazioni e l’AI completa" })
                if (!Values(strategy).Contains(expected, StringComparer.Ordinal))
                    throw new InvalidOperationException("Strategia Può variare mancante: " + expected);

            var variation = RequireEditor(quantity, "ConsistencyVariation_character");
            RequireNativeEditor(variation, "Descrizione Può variare");
            variation.Text = string.Empty;
            SelectByText(strategy, "La decide l’AI");
            await WaitForLayoutAsync();
            var next = Descendants(quantity).OfType<Button>().FirstOrDefault(b =>
                (b.Content?.ToString() ?? string.Empty).Contains("Avanti", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Avanti assente.");
            if (!next.IsEnabled) throw new InvalidOperationException("La decide l’AI deve permettere descrizione vuota.");

            SelectByText(strategy, "La definisco io");
            await WaitForLayoutAsync();
            if (next.IsEnabled) throw new InvalidOperationException("La definisco io deve richiedere la descrizione della variazione.");
            variation.Text = "Lo sfondo cambia a ogni tavola.";
            await WaitForLayoutAsync();
            if (!next.IsEnabled) throw new InvalidOperationException("La descrizione della variazione non riabilita Avanti.");

            SingleWindowImageSpecsUi.EnsureCurrentPage(window);
            SingleWindowCustomDimensionsUi.EnsureCurrentPage(window);
            await WaitForLayoutAsync();
            if (Descendants(quantity).OfType<ComboBox>().All(c => c.Name != "ImageSpecPreset"))
                throw new InvalidOperationException("Specifiche immagine non collegate alla pagina.");

            if (Descendants(quantity).Any(c => c.Name == "ImageSpecOrientation"))
                throw new InvalidOperationException("Orientamento è ancora presente come controllo indipendente.");
            if (Descendants(quantity).Any(c => c.Name == "ImageSpecSafeMargin"))
                throw new InvalidOperationException("Margine di sicurezza è ancora presente nel flusso AI.");

            var ratio = RequireCombo(quantity, "ImageSpecAspectRatio");
            var ratioValues = Values(ratio);
            foreach (var expectedPrefix in new[] { "1:1", "2:3", "3:2", "9:16", "16:9", "17:22" })
                if (!ratioValues.Any(x => x.StartsWith(expectedPrefix, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Aspect ratio guidato mancante: " + expectedPrefix);

            var coherence = Descendants(quantity).OfType<TextBlock>().FirstOrDefault(x => x.Name == "ImageSpecAspectCoherence")
                ?? throw new InvalidOperationException("Controllo coerenza trim/aspect ratio mancante.");
            SelectByPrefix(ratio, "16:9");
            await WaitForLayoutAsync();
            if (!(coherence.Text ?? string.Empty).Contains("Molto diverso", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("16:9 su trim Letter non produce l'avviso di forte incoerenza.");

            SelectByPrefix(ratio, "17:22");
            await WaitForLayoutAsync();
            if (!(coherence.Text ?? string.Empty).Contains("Coerente", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("17:22 su trim Letter non risulta coerente.");

            var technicalPrompt = SingleWindowImageSpecsUi.BuildPromptBlock(project);
            if (technicalPrompt.Contains("- Orientamento:", StringComparison.OrdinalIgnoreCase) ||
                technicalPrompt.Contains("Margine di sicurezza creativo", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Il prompt contiene ancora Orientamento o Margine di sicurezza.");
            if (!technicalPrompt.Contains("Coerenza trim/aspect ratio", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Il prompt non contiene il controllo di coerenza trim/aspect ratio.");

            SingleWindowNativeV11Ui.ShowPrompt(window, host, 12);
            await WaitForLayoutAsync();
            var promptPage = pageHost.Content as Control ?? throw new InvalidOperationException("Pagina Istruzioni assente.");
            AssertText(promptPage, "DEVE FARE");
            AssertText(promptPage, "NON DEVE FARE");
            AssertText(promptPage, "PROMPT — modificabile");
            RequireNativeEditor(RequireEditor(promptPage, "MustDoEditor"), "DEVE FARE");
            RequireNativeEditor(RequireEditor(promptPage, "MustNotDoEditor"), "NON DEVE FARE");
            var promptEditor = RequireEditor(promptPage, "PromptEditor");
            RequireNativeEditor(promptEditor, "PROMPT");

            // Exercise the actual provider UI path, not only the compiler self-test.
            SingleWindowPromptTargetAiUi.EnsureCurrentPage(window);
            await WaitForLayoutAsync();
            var provider = RequireCombo(promptPage, "PromptTargetAi");
            var prepare = Descendants(promptPage).OfType<Button>().FirstOrDefault(b => b.Name == "PrepareProviderSpecificPrompt")
                ?? throw new InvalidOperationException("Pulsante compiler provider-specific assente.");

            SelectByText(provider, "ChatGPT / OpenAI");
            await WaitForLayoutAsync();
            prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForLayoutAsync(260);
            var openAiPrompt = promptEditor.Text ?? string.Empty;
            foreach (var required in new[]
                     {
                         $"DIEZ PROVIDER COMPILER v{PromptEngineeringCompiler.Version}",
                         "PROVIDER EXECUTION PROFILE — OPENAI IMAGE GENERATION",
                         "COMMERCIAL COLORING BOOK",
                         "PROFESSIONAL QUALITY GATE",
                         "pure black #000000",
                         "pure white #FFFFFF"
                     })
                if (!openAiPrompt.Contains(required, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Prompt OpenAI GUI incompleto: " + required);

            SelectByText(provider, "Gemini");
            await WaitForLayoutAsync();
            prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForLayoutAsync(260);
            var geminiPrompt = promptEditor.Text ?? string.Empty;
            foreach (var required in new[]
                     {
                         "PROVIDER EXECUTION PROFILE — GEMINI NATIVE IMAGE GENERATION",
                         "ONE coherent scene concept",
                         "COMMERCIAL COLORING BOOK",
                         "PROFESSIONAL QUALITY GATE"
                     })
                if (!geminiPrompt.Contains(required, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Prompt Gemini GUI incompleto: " + required);
            if (string.Equals(openAiPrompt, geminiPrompt, StringComparison.Ordinal))
                throw new InvalidOperationException("La GUI produce lo stesso prompt per OpenAI e Gemini.");

            var title = Field<TextBlock>(host, "_title")?.Text ?? string.Empty;
            if (!title.Contains("12 immagini", StringComparison.Ordinal))
                throw new InvalidOperationException("Il numero immagini non resta visibile nel titolo del passo 2/4.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static void RequireNativeEditor(TextBox box, string label)
    {
        if (!box.IsVisible || !box.IsEnabled || box.IsReadOnly || !box.IsUndoEnabled)
            throw new InvalidOperationException($"Editor '{label}' non visibile/editabile/undo.");
        if (box.Background is null || box.BorderBrush is null || box.BorderThickness.Left < 1)
            throw new InvalidOperationException($"Editor '{label}' non ha bordo e sfondo nativi.");
        RequireRendered(box, 220, 60, label);
    }

    private static TextBox RequireEditor(Control root, string name) =>
        Descendants(root).OfType<TextBox>().FirstOrDefault(x => x.Name == name)
        ?? throw new InvalidOperationException("TextBox nativo mancante: " + name);

    private static NumericUpDown RequireNumeric(Control root, string name) =>
        Descendants(root).OfType<NumericUpDown>().FirstOrDefault(x => x.Name == name)
        ?? throw new InvalidOperationException("NumericUpDown nativo mancante: " + name);

    private static ComboBox RequireCombo(Control root, string name) =>
        Descendants(root).OfType<ComboBox>().FirstOrDefault(x => x.Name == name)
        ?? throw new InvalidOperationException("ComboBox mancante: " + name);

    private static void SelectByText(ComboBox combo, string text)
    {
        if (combo.ItemsSource is not IEnumerable source)
            throw new InvalidOperationException("Combo senza ItemsSource: " + combo.Name);
        combo.SelectedItem = source.Cast<object>().FirstOrDefault(x => string.Equals(x.ToString(), text, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Voce '{text}' mancante in {combo.Name}.");
    }

    private static void SelectByPrefix(ComboBox combo, string prefix)
    {
        if (combo.ItemsSource is not IEnumerable source)
            throw new InvalidOperationException("Combo senza ItemsSource: " + combo.Name);
        combo.SelectedItem = source.Cast<object>().FirstOrDefault(x => (x.ToString() ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Voce '{prefix}…' mancante in {combo.Name}.");
    }

    private static List<string> Values(ComboBox combo) =>
        combo.ItemsSource is IEnumerable source ? source.Cast<object>().Select(x => x.ToString() ?? string.Empty).ToList() : [];

    private static void RequireRendered(Control control, double minWidth, double minHeight, string label)
    {
        if (!control.IsVisible || control.Opacity < 0.5)
            throw new InvalidOperationException($"Il controllo '{label}' non è visibile a video.");
        if (control.Bounds.Width < minWidth || control.Bounds.Height < minHeight)
            throw new InvalidOperationException($"Il controllo '{label}' ha dimensioni insufficienti: {control.Bounds.Width:0.#} × {control.Bounds.Height:0.#}.");
    }

    private static void AssertText(Control? root, string expected)
    {
        if (root is null || !Descendants(root).OfType<TextBlock>().Any(t => (t.Text ?? string.Empty).Contains(expected, StringComparison.Ordinal)))
            throw new InvalidOperationException("Testo UI mancante: " + expected);
    }

    private static void AssertNoButton(Control root, string forbidden)
    {
        if (Descendants(root).OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), forbidden, StringComparison.Ordinal)))
            throw new InvalidOperationException("Pulsante non ammesso nella pagina: " + forbidden);
    }

    private static async Task WaitForLayoutAsync(int delayMs = 120)
    {
        await Task.Yield();
        await Task.Delay(delayMs);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static void SetSession(MainWindow window, PreviewProject project, string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
        typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, path);
    }

    private static T? Field<T>(object host, string name) where T : class =>
        host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as T;

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
