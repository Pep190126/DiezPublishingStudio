using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Provider-target UI for the canonical prompt-engineering pipeline.
/// The GUI collects parameters; PromptEngineeringEngine owns prompt quality and provider rendering.
/// No provider is implemented as a thin header/prepend over a weak generic prompt.
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

        // The historical generic builder is intentionally no longer part of the visible workflow.
        // Prompt quality now comes from one canonical compiler for Generic/OpenAI/Gemini/Other.
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
            Text = "Il motore Diez genera un brief tecnico completo in inglese. I parametri della GUI lo arricchiscono, ma il nucleo professionale e i quality gate restano sempre presenti.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 760
        };

        var preparedProvider = PromptMasterStateStore.LoadForCurrentBook(project)?.ProviderId ?? string.Empty;

        int Count()
        {
            var value = ReadHostString(host, "Count");
            return int.TryParse(value, out var parsed) ? Math.Clamp(parsed, 1, 500) : 1;
        }

        async Task PersistSettingsAsync()
        {
            var selected = provider.SelectedItem as TargetChoice ?? choices[0];
            settings.ProviderId = selected.Id;
            settings.PreferAdvancedModel = advanced.IsChecked == true;
            PromptPreparationSettingsStore.Save(project, settings);
            await ProjectFileStore.SaveAsync(path, project);
        }

        void StoreDraft()
        {
            PromptMasterStateStore.SaveDraft(project, Count(), mustDo.Text, mustNotDo.Text, prompt.Text);
        }

        void RefreshNext()
        {
            if (next is null) return;
            var selected = provider.SelectedItem as TargetChoice ?? choices[0];
            // A manually edited non-empty prompt may always continue. If the box is empty,
            // the selected provider must be explicitly prepared first.
            next.IsEnabled = !string.IsNullOrWhiteSpace(prompt.Text) ||
                             string.Equals(preparedProvider, selected.Id, StringComparison.OrdinalIgnoreCase);
        }

        provider.SelectionChanged += async (_, _) =>
        {
            var selected = provider.SelectedItem as TargetChoice ?? choices[0];
            advanced.IsVisible = !string.Equals(selected.Id, PromptEngineeringProviderIds.Generic, StringComparison.OrdinalIgnoreCase);
            preparedProvider = string.Empty;
            await PersistSettingsAsync();
            RefreshNext();
        };
        advanced.IsCheckedChanged += async (_, _) =>
        {
            preparedProvider = string.Empty;
            await PersistSettingsAsync();
            RefreshNext();
        };

        prompt.TextChanged += (_, _) =>
        {
            StoreDraft();
            RefreshNext();
        };
        mustDo.TextChanged += (_, _) => StoreDraft();
        mustNotDo.TextChanged += (_, _) => StoreDraft();

        prepare.Click += async (_, _) =>
        {
            var selected = provider.SelectedItem as TargetChoice ?? choices[0];
            settings.ProviderId = selected.Id;
            settings.PreferAdvancedModel = advanced.IsChecked == true;
            PromptPreparationSettingsStore.Save(project, settings);

            var engineered = PromptEngineeringEngine.BuildSeriesPrompt(
                project,
                Count(),
                mustDo.Text,
                mustNotDo.Text,
                selected.Id,
                settings.PreferAdvancedModel);
            prompt.Text = engineered;
            preparedProvider = selected.Id;
            PromptMasterStateStore.SaveDraft(project, Count(), mustDo.Text, mustNotDo.Text, engineered);
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

        if (string.IsNullOrWhiteSpace(prompt.Text))
        {
            var selected = provider.SelectedItem as TargetChoice ?? choices[0];
            var engineered = PromptEngineeringEngine.BuildSeriesPrompt(
                project,
                Count(),
                mustDo.Text,
                mustNotDo.Text,
                selected.Id,
                settings.PreferAdvancedModel);
            prompt.Text = engineered;
            preparedProvider = selected.Id;
            PromptMasterStateStore.SaveDraft(project, Count(), mustDo.Text, mustNotDo.Text, engineered);
        }
        else StoreDraft();
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
