using DiezPublishingStudio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DiezPublishingStudio.UnoSpike;

/// <summary>
/// Renders book-family controls directly from the shared Core definitions.
/// This is intentionally presentation-only: canonical option names live in Diez.Core.
/// </summary>
internal static class BookFamilyWorkspace
{
    public static UIElement Build(
        DiezProjectDocument document,
        string bookType,
        Func<Task> saveProject,
        Action<string> report,
        Action openAi,
        Action openMaster,
        Action openExport)
    {
        var type = BookTypeCatalog.Normalize(bookType);
        var root = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(28),
            MaxWidth = 1050,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        root.Children.Add(new TextBlock
        {
            Text = FriendlyTitle(type),
            FontSize = 28,
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new TextBlock
        {
            Text = FriendlyDescription(type),
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new Separator());

        var optionPanel = new StackPanel { Spacing = 10 };
        var controls = new List<(BookTypeAiOptionDefinition Definition, Control Control)>();
        foreach (var definition in BookTypeAiOptionsCoreService.DefinitionsFor(type))
        {
            var stored = document.GetUiString(StorageKey(type, definition.Key), definition.DefaultValue);
            Control control = definition.Kind switch
            {
                BookTypeAiOptionKind.Toggle => new CheckBox
                {
                    Content = definition.Label,
                    IsChecked = string.Equals(stored, "true", StringComparison.OrdinalIgnoreCase)
                },
                BookTypeAiOptionKind.Choice => BuildCombo(definition, stored),
                _ => new TextBox
                {
                    Text = stored,
                    PlaceholderText = definition.Help,
                    MinHeight = 42,
                    AcceptsReturn = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                }
            };

            controls.Add((definition, control));
            if (control is CheckBox)
            {
                optionPanel.Children.Add(control);
            }
            else
            {
                optionPanel.Children.Add(new StackPanel
                {
                    Spacing = 5,
                    Children =
                    {
                        new TextBlock { Text = definition.Label, TextWrapping = TextWrapping.Wrap },
                        control
                    }
                });
            }
        }

        root.Children.Add(Card("Impostazioni", optionPanel));

        var notes = new TextBox
        {
            Text = document.GetUiString(StorageKey(type, "Notes")),
            PlaceholderText = NotesPlaceholder(type),
            MinHeight = 180,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        root.Children.Add(Card(NotesTitle(type), notes));

        var save = new Button { Content = "Salva", Padding = new Thickness(14, 8) };
        save.Click += async (_, _) =>
        {
            foreach (var item in controls)
                document.SetUiString(StorageKey(type, item.Definition.Key), ReadValue(item.Control, item.Definition.DefaultValue));
            document.SetUiString(StorageKey(type, "Notes"), notes.Text);
            await saveProject();
            report($"Impostazioni {FriendlyTitle(type)} salvate.");
        };

        var ai = Button("Prompt / AI", openAi);
        var master = Button("Testo principale", openMaster);
        var export = Button("Esportazione", openExport);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        actions.Children.Add(save);
        actions.Children.Add(ai);
        actions.Children.Add(master);
        actions.Children.Add(export);
        root.Children.Add(actions);

        return root;
    }

    private static ComboBox BuildCombo(BookTypeAiOptionDefinition definition, string stored)
    {
        var choices = (definition.Choices ?? Array.Empty<string>()).ToList();
        if (choices.Count == 0 && !string.IsNullOrWhiteSpace(definition.DefaultValue)) choices.Add(definition.DefaultValue);
        var combo = new ComboBox
        {
            ItemsSource = choices,
            MinWidth = 230,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        combo.SelectedItem = choices.FirstOrDefault(x => string.Equals(x, stored, StringComparison.OrdinalIgnoreCase))
            ?? choices.FirstOrDefault();
        return combo;
    }

    private static string ReadValue(Control control, string fallback) => control switch
    {
        CheckBox check => check.IsChecked == true ? "true" : "false",
        ComboBox combo => combo.SelectedItem?.ToString() ?? fallback,
        TextBox text => text.Text ?? string.Empty,
        _ => fallback
    };

    private static Border Card(string title, UIElement content) => new()
    {
        Padding = new Thickness(16),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Child = new StackPanel
        {
            Spacing = 9,
            Children =
            {
                new TextBlock { Text = title, FontSize = 19, TextWrapping = TextWrapping.Wrap },
                content
            }
        }
    };

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8) };
        button.Click += (_, _) => action();
        return button;
    }

    private static string StorageKey(string type, string key) => $"BookOptions.{type}.{key}";

    private static string FriendlyTitle(string type) => type switch
    {
        BookTypeCatalog.Quiz => "Quiz / trivia",
        BookTypeCatalog.DataCollection => "Catalogo / raccolta dati",
        _ => "Altro tipo di libro"
    };

    private static string FriendlyDescription(string type) => type switch
    {
        BookTypeCatalog.Quiz => "Definisci quantità, difficoltà, categorie e regole del quiz. Prompt, materiali, controllo coerenza ed esportazione restano collegati al progetto.",
        BookTypeCatalog.DataCollection => "Definisci quanti elementi raccogliere, quali campi servono e come gestire doppioni, formati e provenienza dei dati.",
        _ => "Configura gli elementi essenziali del progetto senza forzarlo dentro una tipologia che non gli appartiene."
    };

    private static string NotesTitle(string type) => type switch
    {
        BookTypeCatalog.Quiz => "Argomenti e fonti",
        BookTypeCatalog.DataCollection => "Criteri della raccolta",
        _ => "Descrizione e regole"
    };

    private static string NotesPlaceholder(string type) => type switch
    {
        BookTypeCatalog.Quiz => "Argomenti, pubblico, fonti da usare, cose da evitare e altre indicazioni per le domande.",
        BookTypeCatalog.DataCollection => "Criteri di selezione, fonti, formato desiderato e regole particolari per i dati.",
        _ => "Descrivi il risultato che vuoi ottenere, la struttura e le regole da rispettare."
    };
}
