using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Adds a provider-target choice to the human prompt page. Diez first builds the
/// ordinary editable prompt, then can specialize it for the selected AI provider.
/// This is intentionally separate from the provider used later for transport/API.
/// </summary>
internal static class SingleWindowPromptTargetAiUi
{
    private const string PanelName = "DiezPromptTargetAiPanel";
    private const string EntityKind = "DiezPromptPreparationSettings";
    private const string GenericId = "generic";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static void Attach(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost is null) return;

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty)
                EnsureCurrentPage(window);
        };
        EnsureCurrentPage(window);
    }

    internal static void EnsureCurrentPage(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page) return;

        var labels = Descendants(page).OfType<TextBlock>().Select(t => t.Text ?? string.Empty).ToList();
        if (!labels.Any(t => string.Equals(t, "DEVE FARE", StringComparison.Ordinal)) ||
            !labels.Any(t => string.Equals(t, "NON DEVE FARE", StringComparison.Ordinal)) ||
            !labels.Any(t => string.Equals(t, "PROMPT — modificabile", StringComparison.Ordinal))) return;
        if (Descendants(page).Any(c => string.Equals(c.Name, PanelName, StringComparison.Ordinal))) return;
        if (!TrySession(window, out var project, out var path)) return;

        var stack = Descendants(page).OfType<StackPanel>().FirstOrDefault(p =>
            p.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Prepara prompt", StringComparison.Ordinal)));
        if (stack is null) return;

        var editors = Descendants(page).OfType<TextBox>().Where(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly).ToList();
        if (editors.Count < 3) return;
        var mustDo = editors[0];
        var mustNotDo = editors[1];
        var prompt = editors[2];

        var prepareGeneric = stack.Children.OfType<Button>().FirstOrDefault(b => string.Equals(b.Content?.ToString(), "Prepara prompt", StringComparison.Ordinal));
        var next = stack.Children.OfType<Button>().FirstOrDefault(b => (b.Content?.ToString() ?? string.Empty).Contains("Prompt Pack", StringComparison.OrdinalIgnoreCase));
        if (prepareGeneric is not null) prepareGeneric.Content = "Prepara prompt generico";

        var settings = LoadSettings(project);
        var choices = new List<TargetChoice>
        {
            new(GenericId, "Generico / nessuna AI specifica", null)
        };
        choices.AddRange(AiProviderCatalog.ForOutputType(AiProductionService.TypeImage)
            .Select(p => new TargetChoice(p.Id, p.DisplayName, p)));

        var provider = new ComboBox
        {
            Name = "PromptTargetAi",
            ItemsSource = choices,
            Width = 310,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        provider.SelectedItem = choices.FirstOrDefault(c => string.Equals(c.Id, settings.ProviderId, StringComparison.OrdinalIgnoreCase)) ?? choices[0];

        var advanced = new CheckBox
        {
            Name = "PromptTargetAdvancedModel",
            Content = "Preferisci il modello immagini più avanzato disponibile",
            IsChecked = settings.PreferAdvancedModel,
            IsVisible = (provider.SelectedItem as TargetChoice)?.Descriptor is not null
        };

        var prepareSpecific = new Button
        {
            Name = "PrepareProviderSpecificPrompt",
            Content = "Prepara prompt per AI scelta",
            Width = 220,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        var state = new PageState
        {
            PreparedProviderId = string.Equals(settings.ProviderId, GenericId, StringComparison.OrdinalIgnoreCase) ? GenericId : string.Empty
        };

        void RefreshNext()
        {
            if (next is null) return;
            var selected = provider.SelectedItem as TargetChoice ?? choices[0];
            next.IsEnabled = string.Equals(selected.Id, GenericId, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(state.PreparedProviderId, selected.Id, StringComparison.OrdinalIgnoreCase);
        }

        async Task PersistAsync()
        {
            if (provider.SelectedItem is not TargetChoice selected) return;
            SaveSettings(project, new PromptPreparationSettings
            {
                ProviderId = selected.Id,
                PreferAdvancedModel = advanced.IsChecked == true
            });
            await ProjectFileStore.SaveAsync(path, project);
        }

        provider.SelectionChanged += async (_, _) =>
        {
            var selected = provider.SelectedItem as TargetChoice ?? choices[0];
            advanced.IsVisible = selected.Descriptor is not null;
            state.PreparedProviderId = string.Equals(selected.Id, GenericId, StringComparison.OrdinalIgnoreCase) ? GenericId : string.Empty;
            RefreshNext();
            await PersistAsync();
        };
        advanced.IsCheckedChanged += async (_, _) =>
        {
            state.PreparedProviderId = string.Empty;
            RefreshNext();
            await PersistAsync();
        };

        if (prepareGeneric is not null)
        {
            prepareGeneric.Click += (_, _) =>
            {
                state.PreparedProviderId = GenericId;
                provider.SelectedItem = choices[0];
                RefreshNext();
            };
        }

        prepareSpecific.Click += async (_, _) =>
        {
            var selected = provider.SelectedItem as TargetChoice ?? choices[0];
            if (selected.Descriptor is null)
            {
                if (prepareGeneric is not null)
                {
                    state.PreparedProviderId = GenericId;
                    RefreshNext();
                }
                return;
            }

            var generic = BuildGenericFallback(project, host, mustDo.Text ?? string.Empty, mustNotDo.Text ?? string.Empty);
            var basePrompt = string.IsNullOrWhiteSpace(prompt.Text) ? generic : prompt.Text!.Trim();
            if (!basePrompt.Contains("SPECIFICHE TECNICHE:", StringComparison.Ordinal))
                basePrompt += Environment.NewLine + Environment.NewLine + SingleWindowImageSpecsUi.BuildPromptBlock(project);
            prompt.Text = BuildSpecific(basePrompt, selected.Descriptor, advanced.IsChecked == true);
            state.PreparedProviderId = selected.Id;
            RefreshNext();
            await PersistAsync();
        };

        var panel = new StackPanel
        {
            Name = PanelName,
            Spacing = 7,
            Children =
            {
                new TextBlock { Text = "AI per cui preparare il prompt specifico", FontSize = 16 },
                new TextBlock
                {
                    Text = "Scegli l'AI destinataria: Diez adatta il prompt alle sue caratteristiche. La scelta è indipendente dall'eventuale provider/API usato dopo.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                provider,
                advanced,
                prepareSpecific
            }
        };

        if (stack.Parent is StackPanel parent)
        {
            var actionIndex = parent.Children.IndexOf(stack);
            if (actionIndex >= 0) parent.Children.Insert(actionIndex, panel);
        }
        else
        {
            stack.Children.Insert(0, panel);
        }

        RefreshNext();
    }

    private static string BuildGenericFallback(PreviewProject project, object host, string mustDo, string mustNotDo)
    {
        var count = ReadColoringProperty(host, "Count");
        var rules = ReadColoringProperty(host, "Rules");
        var consistent = bool.TryParse(ReadColoringProperty(host, "Consistent"), out var enabled) && enabled;
        var sb = new StringBuilder();
        sb.AppendLine($"Crea {count} immagini per un Coloring Book.").AppendLine();
        sb.AppendLine("DEVE FARE:").AppendLine(mustDo.Trim()).AppendLine();
        sb.AppendLine("NON DEVE FARE:").AppendLine(mustNotDo.Trim());
        if (consistent && !string.IsNullOrWhiteSpace(rules))
            sb.AppendLine().AppendLine("CONSISTENT:").AppendLine(rules.Trim());
        sb.AppendLine().AppendLine(SingleWindowImageSpecsUi.BuildPromptBlock(project));
        sb.AppendLine().AppendLine("Ogni immagine deve essere distinta e non deve contenere ID, numeri o nomi file dentro l'immagine.");
        return sb.ToString().Trim();
    }

    private static string BuildSpecific(string basePrompt, AiProviderDescriptor provider, bool advanced)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PROMPT SPECIFICO PER {provider.DisplayName.ToUpperInvariant()}").AppendLine();
        sb.AppendLine(AiProviderCatalog.ImageModelInstruction(provider.DisplayName, advanced)).AppendLine();
        sb.AppendLine("Interpreta i vincoli seguenti in modo letterale. Non eliminare requisiti, divieti, specifiche tecniche o regole di coerenza per rendere il prompt più creativo.").AppendLine();
        sb.AppendLine(basePrompt.Trim());
        return sb.ToString().Trim();
    }

    private static string ReadColoringProperty(object host, string property)
    {
        var coloring = host.GetType().GetField("_coloring", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
        return coloring?.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)?.GetValue(coloring)?.ToString() ?? string.Empty;
    }

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private static PromptPreparationSettings LoadSettings(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return new PromptPreparationSettings();
        try { return JsonSerializer.Deserialize<PromptPreparationSettings>(entity.Notes, JsonOptions) ?? new PromptPreparationSettings(); }
        catch { return new PromptPreparationSettings(); }
    }

    private static void SaveSettings(PreviewProject project, PromptPreparationSettings settings)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity { Kind = EntityKind, Name = "Preparazione prompt AI", IsCandidate = false };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(settings, JsonOptions);
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
                case Border b when b.Child is Control child:
                    stack.Push(child);
                    break;
                case ScrollViewer s when s.Content is Control child:
                    stack.Push(child);
                    break;
                case ContentControl c when c.Content is Control child:
                    stack.Push(child);
                    break;
            }
        }
    }

    private sealed class PromptPreparationSettings
    {
        public string ProviderId { get; set; } = GenericId;
        public bool PreferAdvancedModel { get; set; } = true;
    }

    private sealed class PageState
    {
        public string PreparedProviderId { get; set; } = string.Empty;
    }

    private sealed record TargetChoice(string Id, string Label, AiProviderDescriptor? Descriptor)
    {
        public override string ToString() => Label;
    }
}
