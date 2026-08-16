using DiezPublishingStudio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace DiezPublishingStudio.UnoSpike;

internal static class CrosswordWorkspace
{
    public static UIElement Build(
        DiezProjectDocument document,
        Func<Task> saveProject,
        Action<string> report,
        Action refresh,
        Func<string, string, Task> exportText)
    {
        var snapshot = document.CrosswordWorkspace();
        var root = PageRoot(
            "Cruciverba · vocabolario canonico",
            "Parole, definizioni, note e approvazioni sono GraphEntity + BibleEntry del Core. Il testo Uno precedente resta solo come bozza di recupero.");

        var theme = Editor(snapshot.Theme, "Tema del cruciverba", 42, false);
        var language = Editor(snapshot.PrimaryLanguage, "Italiano", 42, false);
        var adaptive = new CheckBox
        {
            Content = "Tipo adattivo: adatta definizioni e difficoltà al pubblico",
            IsChecked = snapshot.Adaptive
        };
        var saveSettings = AsyncButton("Salva impostazioni", async () =>
        {
            var result = document.SaveCrosswordSettings(theme.Text, language.Text, adaptive.IsChecked == true);
            if (result.Changed) await saveProject();
            report(result.Message);
            if (result.Changed) refresh();
        });
        root.Children.Add(Card("Impostazioni Core", Vertical(
            Labeled("Tema", theme),
            Labeled("Lingua principale", language),
            adaptive,
            saveSettings)));

        Guid? selectedId = null;
        var list = new ListView
        {
            Height = 240,
            ItemsSource = snapshot.Entries.Select(DisplayEntry).ToList()
        };
        var word = Editor("", "PAROLA", 42, false);
        var definition1 = Editor("", "Definizione 1", 68);
        var definition2 = Editor("", "Definizione 2", 68);
        var definition3 = Editor("", "Definizione 3", 68);
        var definition4 = Editor("", "Definizione 4", 68);
        var notes = Editor("", "Note / incertezza / controllo", 82);
        var approved = Editor("", "Definizione approvata o binding editoriale", 68);

        void LoadEntry(DiezCrosswordEntryDto entry)
        {
            selectedId = entry.EntityId;
            word.Text = entry.Word;
            definition1.Text = entry.Definition1;
            definition2.Text = entry.Definition2;
            definition3.Text = entry.Definition3;
            definition4.Text = entry.Definition4;
            notes.Text = entry.Notes;
            approved.Text = entry.Approved;
        }

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= snapshot.Entries.Count) return;
            LoadEntry(snapshot.Entries[list.SelectedIndex]);
        };

        var newEntry = Button("+ Nuova parola", () =>
        {
            selectedId = null;
            list.SelectedIndex = -1;
            word.Text = string.Empty;
            definition1.Text = string.Empty;
            definition2.Text = string.Empty;
            definition3.Text = string.Empty;
            definition4.Text = string.Empty;
            notes.Text = string.Empty;
            approved.Text = string.Empty;
            word.Focus(FocusState.Programmatic);
        });

        var saveEntry = AsyncButton("Salva parola e definizioni", async () =>
        {
            var result = document.SaveCrosswordEntry(
                selectedId,
                word.Text,
                definition1.Text,
                definition2.Text,
                definition3.Text,
                definition4.Text,
                notes.Text,
                approved.Text);
            if (result.Status is "INVALID" or "CONFLICT")
            {
                report(result.Message);
                return;
            }
            if (result.Changed) await saveProject();
            report(result.Message);
            if (result.Changed) refresh();
        });

        var deleteEntry = AsyncButton("Elimina parola", async () =>
        {
            if (!selectedId.HasValue)
            {
                report("Seleziona prima una parola da eliminare.");
                return;
            }
            var result = document.DeleteCrosswordEntry(selectedId.Value);
            if (result.Changed) await saveProject();
            report(result.Message);
            if (result.Changed) refresh();
        });

        root.Children.Add(Card("Vocabolario Cruciverba", Vertical(
            new TextBlock
            {
                Text = $"{snapshot.Entries.Count} parole canoniche · {snapshot.MissingDefinitions} senza definizioni. I dati AI promossi compaiono qui automaticamente.",
                TextWrapping = TextWrapping.Wrap
            },
            list,
            Horizontal(newEntry, saveEntry, deleteEntry))));

        root.Children.Add(Card("Scheda parola", Vertical(
            Labeled("Parola / soluzione", word),
            Labeled("Definizione 1", definition1),
            Labeled("Definizione 2", definition2),
            Labeled("Definizione 3", definition3),
            Labeled("Definizione 4", definition4),
            Labeled("Note", notes),
            Labeled("Definizione approvata / binding", approved))));

        var qxw = ReadOnlyEditor(document.CrosswordQxwText(), "Nessuna parola nel vocabolario canonico.", 150);
        root.Children.Add(Card("Qxw / handoff", Vertical(
            new TextBlock
            {
                Text = "La lista viene rigenerata dal vocabolario canonico: non esiste più una seconda copia editabile in UnoUiState.",
                TextWrapping = TextWrapping.Wrap
            },
            qxw,
            Horizontal(
                Button("Copia Qxw", () => CopyText(document.CrosswordQxwText())),
                AsyncButton("Esporta lista Qxw", async () =>
                    await exportText("crossword-qxw.txt", document.CrosswordQxwText()))))));

        if (!string.IsNullOrWhiteSpace(snapshot.LegacyWordsDraft) ||
            !string.IsNullOrWhiteSpace(snapshot.LegacyQxwDraft))
        {
            root.Children.Add(Card("Recupero bozza Uno precedente — sola lettura", Vertical(
                new TextBlock
                {
                    Text = "Queste copie sono conservate durante la migrazione ma non vengono più salvate da questo workspace.",
                    TextWrapping = TextWrapping.Wrap
                },
                ReadOnlyEditor(snapshot.LegacyWordsDraft, "Vecchia bozza parole/definizioni", 100),
                ReadOnlyEditor(snapshot.LegacyQxwDraft, "Vecchia bozza Qxw", 90))));
        }

        return root;
    }

    private static string DisplayEntry(DiezCrosswordEntryDto entry)
    {
        var defined = new[] { entry.Definition1, entry.Definition2, entry.Definition3, entry.Definition4 }
            .Count(value => !string.IsNullOrWhiteSpace(value));
        var approved = string.IsNullOrWhiteSpace(entry.Approved) ? "da approvare" : "approvata";
        return $"{entry.Word} · {defined}/4 definizioni · {approved}";
    }

    private static StackPanel PageRoot(string title, string description)
    {
        var root = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(28),
            MaxWidth = 1050,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        root.Children.Add(new TextBlock { Text = title, FontSize = 28, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new Separator());
        return root;
    }

    private static Border Card(string title, UIElement content) => new()
    {
        Padding = new Thickness(16),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Child = Vertical(new TextBlock { Text = title, FontSize = 19, TextWrapping = TextWrapping.Wrap }, content)
    };

    private static StackPanel Vertical(params UIElement[] items)
    {
        var panel = new StackPanel { Spacing = 9 };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static StackPanel Horizontal(params UIElement[] items)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static StackPanel Labeled(string label, UIElement control) =>
        Vertical(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, control);

    private static TextBox Editor(string text, string placeholder, double minHeight, bool multiline = true) => new()
    {
        Text = text ?? string.Empty,
        PlaceholderText = placeholder,
        MinHeight = minHeight,
        AcceptsReturn = multiline,
        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static TextBox ReadOnlyEditor(string text, string placeholder, double minHeight) => new()
    {
        Text = text ?? string.Empty,
        PlaceholderText = placeholder,
        MinHeight = minHeight,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        IsReadOnly = true,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8) };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button AsyncButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text ?? string.Empty);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}
