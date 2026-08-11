using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Provider-target UI for the canonical prompt-engineering pipeline.
/// Legacy host text is never mistaken for a user override: a prompt is trusted only when it has
/// current engine metadata/fingerprint. Genuine manual edits are preserved explicitly.
/// </summary>
internal static class SingleWindowPromptTargetAiUi
{
    private const string PanelName = "DiezPromptTargetAiPanel";

    public static void Attach(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
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
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
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

        var oldPrepare = actionRow.Children.OfType<Button>().FirstOrDefault(b =>
            string.Equals(b.Content?.ToString(), "Prepara prompt", StringComparison.Ordinal));
        if (oldPrepare is not null) oldPrepare.IsVisible = false;

        var next = actionRow.Children.OfType<Button>().FirstOrDefault(b =>
            (b.Content?.ToString() ?? string.Empty).Contains("Prompt Pack", StringComparison.OrdinalIgnoreCase));

        var settings = PromptPreparationSettingsStore.Load(project);
        var choices = new List<TargetChoice>
        {
            new(PromptEngineeringProviderIds.Generic, "Generico tecnico / AI compatibile", null),
            new(PromptEngineeringProviderIds.OpenAi, "ChatGPT / OpenAI", AiProviderCatalog.FindById(AiProviderCatalog.OpenAiId)),
            new(PromptEngineeringProviderIds.Gemini, "Gemini", AiProviderCatalog.FindById(AiProviderCatalog.GeminiId)),
            new(PromptEngineeringProviderIds.Other, "Altra / nuova AI", AiProviderCatalog.FindById(AiProviderCatalog.OtherId))
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
        var info = new TextBlock
        {
            Text = "Diez compila un brief tecnico in inglese da un modello canonico. I parametri GUI possono cambiare senza impoverire il nucleo professionale; una modifica manuale del PROMPT viene invece marcata e preservata.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 760
        };

        var suppressPromptEvents = true;
        var initialized = false;
        var preparedProvider = string.Empty;

        int Count()
        {
            var value = ReadHostString(host, "Count");
            return int.TryParse(value, out var parsed) ? Math.Clamp(parsed, 1, 500) : 1;
        }

        TargetChoice Selected() => provider.SelectedItem as TargetChoice ?? choices[0];

        void SaveMaster(bool manual)
        {
            var selected = Selected();
            PromptMasterStateStore.SaveDraft(project, Count(), mustDo.Text, mustNotDo.Text, prompt.Text);
            if (manual)
                PromptMasterMetadataStore.MarkManual(project, Count(), mustDo.Text, mustNotDo.Text, selected.Id, advanced.IsChecked == true);
            else
                PromptMasterMetadataStore.MarkGenerated(project, Count(), mustDo.Text, mustNotDo.Text, selected.Id, advanced.IsChecked == true);
        }

        string CompileCurrent()
        {
            var selected = Selected();
            return PromptEngineeringEngine.BuildSeriesPrompt(
                project,
                Count(),
                mustDo.Text,
                mustNotDo.Text,
                selected.Id,
                advanced.IsChecked == true);
        }

        void SetCompiledPrompt(string value)
        {
            suppressPromptEvents = true;
            try { prompt.Text = value; }
            finally { suppressPromptEvents = false; }
            preparedProvider = Selected().Id;
            SaveMaster(manual: false);
        }

        async Task PersistSettingsAsync()
        {
            var selected = Selected();
            settings.ProviderId = selected.Id;
            settings.PreferAdvancedModel = advanced.IsChecked == true;
            PromptPreparationSettingsStore.Save(project, settings);
            await ProjectFileStore.SaveAsync(path, project);
        }

        void RefreshNext()
        {
            if (next is null) return;
            next.IsEnabled = !string.IsNullOrWhiteSpace(prompt.Text);
        }

        provider.SelectionChanged += async (_, _) =>
        {
            var selected = Selected();
            advanced.IsVisible = !string.Equals(selected.Id, PromptEngineeringProviderIds.Generic, StringComparison.OrdinalIgnoreCase);
            preparedProvider = string.Empty;
            if (!initialized) return;
            await PersistSettingsAsync();
            RefreshNext();
        };
        advanced.IsCheckedChanged += async (_, _) =>
        {
            preparedProvider = string.Empty;
            if (!initialized) return;
            await PersistSettingsAsync();
            RefreshNext();
        };

        prompt.TextChanged += (_, _) =>
        {
            if (!initialized || suppressPromptEvents) return;
            SaveMaster(manual: true);
            RefreshNext();
        };
        mustDo.TextChanged += (_, _) =>
        {
            if (!initialized) return;
            // Do not overwrite a manually edited prompt while the user types. The fingerprint
            // becomes stale and the explicit Prepare button recompiles when requested.
            RefreshNext();
        };
        mustNotDo.TextChanged += (_, _) =>
        {
            if (!initialized) return;
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

        var panel = new StackPanel
        {
            Name = PanelName,
            Spacing = 7,
            Children =
            {
                new TextBlock { Text = "Motore prompt / AI destinataria", FontSize = 17 },
                info,
                provider,
                advanced,
                prepare
            }
        };

        if (actionRow.Parent is StackPanel parent)
        {
            var actionIndex = parent.Children.IndexOf(actionRow);
            parent.Children.Insert(Math.Max(0, actionIndex), panel);
        }
        else actionRow.Children.Insert(0, panel);

        // At this point prompt.Text may contain the legacy host-generated prompt. It is trusted only
        // when Diez has current metadata proving that it came from this engine/parameter fingerprint.
        var existing = PromptMasterStateStore.LoadForCurrentBook(project);
        var metadata = PromptMasterMetadataStore.Load(project);
        var selectedNow = Selected();
        var currentMatches = existing is not null &&
                             PromptMasterMetadataStore.MatchesCurrent(
                                 project, metadata, Count(), existing.MustDo, existing.MustNotDo,
                                 selectedNow.Id, advanced.IsChecked == true);
        if (currentMatches && !string.IsNullOrWhiteSpace(existing!.Prompt))
        {
            mustDo.Text = existing.MustDo;
            mustNotDo.Text = existing.MustNotDo;
            suppressPromptEvents = true;
            try { prompt.Text = existing.Prompt; }
            finally { suppressPromptEvents = false; }
            preparedProvider = selectedNow.Id;
        }
        else
        {
            SetCompiledPrompt(CompileCurrent());
        }

        initialized = true;
        suppressPromptEvents = false;
        RefreshNext();
    }

    private static TextBox? NamedTextBox(Control root, string name) =>
        Descendants(root).OfType<TextBox>().FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    private static string ReadHostString(object host, string property)
    {
        var state = host.GetType().GetField("_coloring", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
        return state?.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)?.GetValue(state)?.ToString() ?? string.Empty;
    }

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
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

    private sealed record TargetChoice(string Id, string Label, AiProviderDescriptor? Descriptor)
    {
        public override string ToString() => Label;
    }
}
