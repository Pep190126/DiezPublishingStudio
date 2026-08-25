using DiezPublishingStudio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DiezPublishingStudio.UnoSpike;

/// <summary>
/// User-facing theme and local subject-proposal UI. It deliberately hides planner prompts,
/// JSON, AI Exchange versions and other transport details from the ordinary visual workflow.
/// </summary>
internal static class VisualThemeWorkspace
{
    public static UIElement Build(
        DiezProjectDocument document,
        Func<Task> save,
        Action<string> report,
        Action refresh,
        Func<Task<bool>> saveSetup)
    {
        var state = document.ReadVisualTheme();
        var panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };

        panel.Children.Add(new TextBlock
        {
            Text = "Scegli un tema. Diez usa la libreria locale per proporti i soggetti senza Prompt intermedi, JSON o copia/incolla. Prima del Prompt Pack puoi sempre controllare e modificare l'elenco.",
            TextWrapping = TextWrapping.Wrap
        });

        var themeLabels = state.Themes.Select(x => x.DisplayName).ToList();
        var theme = new ComboBox
        {
            ItemsSource = themeLabels,
            MinWidth = 300,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var selectedIndex = state.Themes.ToList().FindIndex(x => string.Equals(x.ThemeId, state.SelectedThemeId, StringComparison.OrdinalIgnoreCase));
        theme.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;

        DiezVisualThemeOptionDto SelectedTheme()
        {
            var index = Math.Clamp(theme.SelectedIndex, 0, Math.Max(0, state.Themes.Count - 1));
            return state.Themes.Count == 0
                ? new DiezVisualThemeOptionDto("custom", "Custom", "USER_AUTHORED", true)
                : state.Themes[index];
        }

        var customName = Editor(state.CustomThemeName, "Nome del tema personalizzato", 54);
        var customProjectOnly = new RadioButton
        {
            GroupName = "VisualThemeCustomStorage",
            Content = "Usa solo in questo progetto",
            IsChecked = !state.ArchiveCustomTheme
        };
        var customLibrary = new RadioButton
        {
            GroupName = "VisualThemeCustomStorage",
            Content = "Salva anche nella libreria temi",
            IsChecked = state.ArchiveCustomTheme
        };
        var apiExpand = new Button
        {
            Content = state.ApiExpansionAvailable
                ? "Espandi tema con AI · executor da collegare"
                : "Espandi tema con AI · richiede API configurata",
            Padding = new Thickness(12, 7),
            IsEnabled = false
        };
        var customPanel = Vertical(
            Labeled("Nome tema Custom", customName),
            new TextBlock
            {
                Text = "L'espansione AI resta prevista come funzione API opzionale. In questa candidata puoi creare il tema e alimentarne manualmente il pool senza usare alcun Prompt intermedio.",
                TextWrapping = TextWrapping.Wrap
            },
            Wrap(customProjectOnly, customLibrary),
            apiExpand);

        void RefreshCustomVisibility()
        {
            var current = SelectedTheme();
            customPanel.Visibility = string.Equals(current.ThemeId, "custom", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        theme.SelectionChanged += (_, _) => RefreshCustomVisibility();
        RefreshCustomVisibility();

        panel.Children.Add(Labeled("Tema", theme));
        panel.Children.Add(customPanel);

        var poolInfo = new TextBlock
        {
            Text = state.Pool.Count == 0
                ? "Pool del tema: nessun soggetto ancora disponibile. Aggiungi almeno le parole/soggetti necessari."
                : "Parole / soggetti disponibili nel tema:\n" + string.Join(" · ", state.Pool.Select(x => x.DisplayName)),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(poolInfo);

        panel.Children.Add(new Separator());
        panel.Children.Add(new TextBlock
        {
            Text = "Aggiungi una parola o un soggetto al tema",
            FontSize = 17,
            TextWrapping = TextWrapping.Wrap
        });
        var addName = Editor(string.Empty, "Es. Zebra, Robot vintage, Lanterna…", 54);
        var addDescription = Editor(string.Empty, "Descrizione opzionale", 70);
        var addProjectOnly = new RadioButton
        {
            GroupName = "VisualThemeSubjectStorage",
            Content = "Solo in questo progetto",
            IsChecked = true
        };
        var addLibrary = new RadioButton
        {
            GroupName = "VisualThemeSubjectStorage",
            Content = "Salva nella libreria di questo tema",
            IsChecked = false
        };
        panel.Children.Add(Labeled("Soggetto / parola personalizzata", addName));
        panel.Children.Add(Labeled("Descrizione", addDescription));
        panel.Children.Add(Wrap(addProjectOnly, addLibrary));
        panel.Children.Add(AsyncButton("Aggiungi al tema", async () =>
        {
            if (!await saveSetup()) return;
            var selected = SelectedTheme();
            var result = document.AddVisualThemeCustomSubject(
                selected.ThemeId,
                customName.Text,
                customLibrary.IsChecked == true,
                addName.Text,
                addDescription.Text,
                addLibrary.IsChecked == true);
            await save();
            report(result.Message);
            if (result.Status == "SUBJECT_ADDED") refresh();
        }));

        panel.Children.Add(new Separator());
        var propose = AsyncButton("Proponi soggetti", async () =>
        {
            if (!await saveSetup()) return;
            var selected = SelectedTheme();
            var result = document.ProposeVisualThemeSubjects(
                selected.ThemeId,
                customName.Text,
                customLibrary.IsChecked == true,
                regenerate: false,
                document.GetUiString("Prompt.MustDo"),
                document.GetUiString("Prompt.MustNotDo"));
            await save();
            report(result.Message);
            refresh();
        });
        var regenerate = AsyncButton("Rigenera proposta", async () =>
        {
            if (!await saveSetup()) return;
            var selected = SelectedTheme();
            var result = document.ProposeVisualThemeSubjects(
                selected.ThemeId,
                customName.Text,
                customLibrary.IsChecked == true,
                regenerate: true,
                document.GetUiString("Prompt.MustDo"),
                document.GetUiString("Prompt.MustNotDo"));
            await save();
            report(result.Message);
            refresh();
        });
        regenerate.IsEnabled = state.Proposal.Count > 0;
        panel.Children.Add(Wrap(propose, regenerate));

        var proposalEditors = new List<(TextBox Name, TextBox Description)>();
        Button? accept = null;
        if (state.Proposal.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Proposta Diez · controlla e modifica prima di accettare",
                FontSize = 17,
                TextWrapping = TextWrapping.Wrap
            });
            foreach (var pair in state.Proposal.Select((value, index) => (value, index)))
            {
                var name = Editor(pair.value.DisplayName, $"Nome soggetto {pair.index + 1}", 54);
                var description = Editor(pair.value.Description, $"Descrizione soggetto {pair.index + 1}", 72);
                proposalEditors.Add((name, description));
                panel.Children.Add(Card($"Soggetto {pair.index + 1}", Vertical(
                    Labeled("Nome", name),
                    Labeled("Descrizione", description))));
            }

            var editInfo = new TextBlock
            {
                Text = "Puoi usare la proposta così com'è oppure modificarla. Se tocchi un campo, salva prima le modifiche: Diez non ignora mai una tua correzione.",
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(editInfo);
            var saveEdits = AsyncButton("Salva modifiche alla proposta", async () =>
            {
                var edits = proposalEditors
                    .Select(x => new DiezVisualThemeProposalEditDto(x.Name.Text ?? string.Empty, x.Description.Text ?? string.Empty))
                    .ToList();
                var result = document.SaveVisualThemeProposalEdits(edits);
                await save();
                report(result.Message);
                if (result.Status == "EDITED") refresh();
            });
            accept = AsyncButton("Accetta e congela i soggetti", async () =>
            {
                var result = document.AcceptVisualThemeProposal();
                await save();
                report(result.Message);
                if (result.Status == "ACCEPTED") refresh();
            });
            bool HasActualUnsavedProposalEdits()
            {
                if (proposalEditors.Count != state.Proposal.Count) return true;
                for (var i = 0; i < proposalEditors.Count; i++)
                {
                    var currentName = (proposalEditors[i].Name.Text ?? string.Empty).Trim();
                    var currentDescription = (proposalEditors[i].Description.Text ?? string.Empty).Trim();
                    var savedName = (state.Proposal[i].DisplayName ?? string.Empty).Trim();
                    var savedDescription = (state.Proposal[i].Description ?? string.Empty).Trim();
                    if (!string.Equals(currentName, savedName, StringComparison.Ordinal) ||
                        !string.Equals(currentDescription, savedDescription, StringComparison.Ordinal))
                        return true;
                }
                return false;
            }

            void RefreshAcceptState()
            {
                var persistedReady = DiezVisualThemeAcceptancePolicy.IsAcceptableProposal(
                    state.RequestedCount, state.Proposal);
                var dirty = HasActualUnsavedProposalEdits();
                if (accept is not null) accept.IsEnabled = persistedReady && !dirty;
                editInfo.Text = dirty
                    ? "Hai modifiche non salvate. Salva la proposta prima di accettarla."
                    : persistedReady
                        ? "Proposta completa e salvata: puoi accettare e congelare i soggetti."
                        : "La proposta non è ancora completa o semanticamente accettabile.";
            }

            foreach (var editor in proposalEditors.SelectMany(x => new[] { x.Name, x.Description }))
                editor.TextChanged += (_, _) => RefreshAcceptState();
            RefreshAcceptState();
            panel.Children.Add(Wrap(saveEdits, accept));
        }

        panel.Children.Add(new TextBlock
        {
            Text = state.Resolved
                ? "Piano soggetti: RISOLTO. Prompt e Prompt Pack possono usare i soggetti congelati."
                : "Piano soggetti: da completare. Il Prompt Pack resta bloccato finché non accetti una proposta completa.",
            TextWrapping = TextWrapping.Wrap
        });

        return Card("Tema e soggetti · prima del Prompt", panel);
    }

    private static TextBox Editor(string? text, string placeholder, double minHeight) => new()
    {
        Text = text ?? string.Empty,
        PlaceholderText = placeholder,
        MinHeight = minHeight,
        AcceptsReturn = minHeight > 60,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static StackPanel Labeled(string label, UIElement control) =>
        Vertical(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, control);

    private static StackPanel Vertical(params UIElement[] items)
    {
        var panel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static StackPanel Wrap(params UIElement[] items)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static Border Card(string title, UIElement body)
    {
        var content = Vertical(
            new TextBlock { Text = title, FontSize = 18, TextWrapping = TextWrapping.Wrap },
            body);
        return new Border
        {
            Padding = new Thickness(14),
            Margin = new Thickness(0, 4, 0, 4),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = content,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static Button AsyncButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 7) };
        button.Click += async (_, _) => await action();
        return button;
    }
}
