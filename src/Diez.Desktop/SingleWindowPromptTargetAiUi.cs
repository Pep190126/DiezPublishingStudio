using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Provider-target page for the canonical prompt pipeline.
/// GUI fields remain inputs; PromptEngineeringCompiler produces the professional provider-specific
/// text. Manual edits are fingerprinted so legacy/generated text is never mistaken for user intent.
/// </summary>
internal static class SingleWindowPromptTargetAiUi
{
    private const string PanelName = "DiezPromptTargetAiPanel";

    public static void Attach(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost");
        if (pageHost is null) return;
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty) EnsureCurrentPage(window);
        };
        EnsureCurrentPage(window);
    }

    internal static void EnsureCurrentPage(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost");
        if (pageHost?.Content is not Control page) return;
        if (Descendants(page).Any(c => string.Equals(c.Name, PanelName, StringComparison.Ordinal))) return;
        if (!TrySession(window, out var project, out var path)) return;

        var mustDo = NamedTextBox(page, "MustDoEditor");
        var mustNotDo = NamedTextBox(page, "MustNotDoEditor");
        var prompt = NamedTextBox(page, "PromptEditor");
        if (mustDo is null || mustNotDo is null || prompt is null) return;

        var actionRow = Descendants(page).OfType<StackPanel>().FirstOrDefault(p =>
            p.Orientation == Orientation.Horizontal &&
            p.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Prepara prompt", StringComparison.Ordinal)));
        if (actionRow is null) return;

        foreach (var legacyPrepare in actionRow.Children.OfType<Button>().Where(b =>
                     string.Equals(b.Content?.ToString(), "Prepara prompt", StringComparison.Ordinal)).ToList())
            legacyPrepare.IsVisible = false;

        var next = actionRow.Children.OfType<Button>().FirstOrDefault(b =>
            (b.Content?.ToString() ?? string.Empty).Contains("Prompt Pack", StringComparison.OrdinalIgnoreCase));

        var settings = PromptPreparationSettingsStore.Load(project);
        var choices = new List<TargetChoice>
        {
            new(PromptEngineeringProviderIds.Generic, "Generico tecnico / AI compatibile"),
            new(PromptEngineeringProviderIds.OpenAi, "ChatGPT / OpenAI"),
            new(PromptEngineeringProviderIds.Gemini, "Gemini"),
            new(PromptEngineeringProviderIds.Other, "Altra / nuova AI")
        };

        var provider = new ComboBox
        {
            Name = "PromptTargetAi",
            ItemsSource = choices,
            Width = 330,
            HorizontalAlignment = HorizontalAlignment.Left,
            SelectedItem = choices.FirstOrDefault(c => string.Equals(c.Id, settings.ProviderId, StringComparison.OrdinalIgnoreCase)) ?? choices[0]
        };
        var advanced = new CheckBox
        {
            Name = "PromptTargetAdvancedModel",
            Content = "Preferisci il modello immagini più avanzato disponibile",
            IsChecked = settings.PreferAdvancedModel,
            IsVisible = !string.Equals(settings.ProviderId, PromptEngineeringProviderIds.Generic, StringComparison.OrdinalIgnoreCase)
        };
        var prepare = new Button
        {
            Name = "PrepareProviderSpecificPrompt",
            Content = "Prepara prompt tecnico per AI scelta",
            Width = 260,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var status = new TextBlock
        {
            Name = "PromptEngineeringStatus",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 760
        };
        var panel = new StackPanel
        {
            Name = PanelName,
            Spacing = 7,
            Children =
            {
                new TextBlock { Text = "Motore prompt / AI destinataria", FontSize = 17 },
                new TextBlock
                {
                    Text = "Diez compila il prompt provider-facing dal modello canonico. Le specifiche tecniche generate da Diez sono in inglese; i vecchi blocchi italiani non vengono più ripristinati nell'editor.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 760
                },
                provider,
                advanced,
                prepare,
                status
            }
        };

        if (actionRow.Parent is StackPanel parent)
        {
            var actionIndex = parent.Children.IndexOf(actionRow);
            parent.Children.Insert(Math.Max(0, actionIndex), panel);
        }
        else actionRow.Children.Insert(0, panel);

        var suppressPromptEvents = true;
        var initialized = false;

        int Count()
        {
            var state = Field<object>(host, "_coloring");
            var value = state?.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public)?.GetValue(state)?.ToString();
            return int.TryParse(value, out var parsed) ? Math.Clamp(parsed, 1, 500) : 1;
        }

        TargetChoice Selected() => provider.SelectedItem as TargetChoice ?? choices[0];

        string CompileCurrent() => PromptEngineeringCompiler.BuildSeriesPrompt(
            project,
            Count(),
            mustDo.Text,
            mustNotDo.Text,
            Selected().Id,
            advanced.IsChecked == true);

        void SaveMaster(bool manual)
        {
            PromptMasterStateStore.SaveDraft(project, Count(), mustDo.Text, mustNotDo.Text, prompt.Text);
            if (manual)
                PromptMasterMetadataStore.MarkManual(project, Count(), mustDo.Text, mustNotDo.Text, Selected().Id, advanced.IsChecked == true);
            else
                PromptMasterMetadataStore.MarkGenerated(project, Count(), mustDo.Text, mustNotDo.Text, Selected().Id, advanced.IsChecked == true);
        }

        void SaveParametersOnly() =>
            PromptMasterStateStore.SaveDraft(project, Count(), mustDo.Text, mustNotDo.Text, prompt.Text);

        void SetCompiledPrompt(string value)
        {
            suppressPromptEvents = true;
            try { prompt.Text = value; }
            finally { suppressPromptEvents = false; }
            SaveMaster(manual: false);
            status.Text = $"Prompt canonico compilato · engine v{PromptEngineeringEngine.EngineVersion} · renderer {Selected().Label} · specifiche tecniche Diez in inglese.";
        }

        void RefreshNext()
        {
            if (next is not null) next.IsEnabled = !string.IsNullOrWhiteSpace(prompt.Text);
        }

        async Task PersistSettingsAsync()
        {
            settings.ProviderId = Selected().Id;
            settings.PreferAdvancedModel = advanced.IsChecked == true;
            PromptPreparationSettingsStore.Save(project, settings);
            await ProjectFileStore.SaveAsync(path, project);
        }

        provider.SelectionChanged += async (_, _) =>
        {
            advanced.IsVisible = !string.Equals(Selected().Id, PromptEngineeringProviderIds.Generic, StringComparison.OrdinalIgnoreCase);
            if (!initialized) return;
            SaveParametersOnly();
            status.Text = "AI destinataria cambiata: premi ‘Prepara prompt tecnico per AI scelta’ per ricompilare la strategia provider-specific.";
            await PersistSettingsAsync();
            RefreshNext();
        };
        advanced.IsCheckedChanged += async (_, _) =>
        {
            if (!initialized) return;
            SaveParametersOnly();
            status.Text = "Preferenza modello cambiata: il prompt corrente resta visibile finché non scegli di ricompilarlo.";
            await PersistSettingsAsync();
            RefreshNext();
        };
        mustDo.TextChanged += (_, _) =>
        {
            if (!initialized) return;
            SaveParametersOnly();
            status.Text = "DEVE FARE modificato: il prompt corrente è ora precedente ai parametri; ricompilalo quando vuoi.";
            RefreshNext();
        };
        mustNotDo.TextChanged += (_, _) =>
        {
            if (!initialized) return;
            SaveParametersOnly();
            status.Text = "NON DEVE FARE modificato: il prompt corrente è ora precedente ai parametri; ricompilalo quando vuoi.";
            RefreshNext();
        };
        prompt.TextChanged += (_, _) =>
        {
            if (!initialized || suppressPromptEvents) return;
            SaveMaster(manual: true);
            status.Text = "PROMPT modificato manualmente: Diez preserverà il delta dell'utente; i vincoli strutturati correnti restano autoritativi nell'export.";
            RefreshNext();
        };
        prepare.Click += async (_, _) =>
        {
            settings.ProviderId = Selected().Id;
            settings.PreferAdvancedModel = advanced.IsChecked == true;
            PromptPreparationSettingsStore.Save(project, settings);
            SetCompiledPrompt(CompileCurrent());
            await ProjectFileStore.SaveAsync(path, project);
            RefreshNext();
        };

        var existing = PromptMasterStateStore.LoadForCurrentBook(project);
        var metadata = PromptMasterMetadataStore.Load(project);
        var currentMatches = existing is not null && PromptMasterMetadataStore.MatchesCurrent(
            project,
            metadata,
            Count(),
            mustDo.Text,
            mustNotDo.Text,
            Selected().Id,
            advanced.IsChecked == true);
        if (currentMatches && !string.IsNullOrWhiteSpace(existing!.Prompt) && !LooksLikeLegacyGeneratedPrompt(existing.Prompt))
        {
            suppressPromptEvents = true;
            try { prompt.Text = existing.Prompt; }
            finally { suppressPromptEvents = false; }
            status.Text = metadata?.ManualOverride == true
                ? "Prompt manuale corrente ripristinato."
                : $"Prompt engine v{PromptEngineeringEngine.EngineVersion} corrente ripristinato.";
        }
        else
        {
            SetCompiledPrompt(CompileCurrent());
            if (existing is not null && LooksLikeLegacyGeneratedPrompt(existing.Prompt))
                status.Text = "Vecchio prompt generato in italiano rilevato e sostituito con il compilatore canonico provider-facing.";
        }

        initialized = true;
        suppressPromptEvents = false;
        RefreshNext();
    }

    internal static bool LooksLikeLegacyGeneratedPrompt(string? value)
    {
        var text = value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Contains("SPECIFICHE TECNICHE:", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("REGOLE COMUNI DEL PROGETTO:", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("PROFILO EDITORIALE COLORING BOOK:", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("VINCOLO CROMATICO ASSOLUTO", StringComparison.OrdinalIgnoreCase)) return true;
        return !text.Contains("DIEZ PROVIDER COMPILER", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("DEVE FARE:", StringComparison.OrdinalIgnoreCase) || text.Contains("NON DEVE FARE:", StringComparison.OrdinalIgnoreCase));
    }

    private static TextBox? NamedTextBox(Control root, string name) =>
        Descendants(root).OfType<TextBox>().FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private static T? Field<T>(object owner, string name) where T : class =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) as T;

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

    private sealed record TargetChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
