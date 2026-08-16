using DiezPublishingStudio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace DiezPublishingStudio.UnoSpike;

internal static class WordSearchWorkspace
{
    public static UIElement Build(
        DiezProjectDocument document,
        Func<Task> saveProject,
        Action<string> report,
        Action refresh,
        Func<string, string, Task> exportText)
    {
        var snapshot = document.WordSearchWorkspace();
        var root = PageRoot(
            "Word Search · database canonico",
            "Puzzle, lessico e risultati AI promossi leggono e scrivono direttamente i ContentNodes del Core. UnoUiState non è più la fonte editoriale di questo workspace.");

        Guid? selectedId = null;
        var list = new ListView
        {
            Height = 230,
            ItemsSource = snapshot.Puzzles.Select(DisplayPuzzle).ToList()
        };
        var puzzleId = Editor("", "PUZ-001", 42, false);
        var title = Editor("", "Titolo puzzle", 42, false);
        var theme = Editor("", "Tema", 42, false);
        var words = Editor("", "Una parola per riga", 190);
        var status = Combo(
            ["Da controllare", "Approvato", "Da rifare"],
            "Da controllare");
        var notes = Editor("", "Note editoriali / provenienza", 90);
        var issues = new TextBlock
        {
            Text = "Seleziona un puzzle per vedere la validazione Core.",
            TextWrapping = TextWrapping.Wrap
        };

        void LoadPuzzle(DiezWordSearchPuzzleDto puzzle)
        {
            selectedId = puzzle.ContentId;
            puzzleId.Text = puzzle.PuzzleId;
            title.Text = puzzle.Title;
            theme.Text = puzzle.Theme;
            words.Text = string.Join(Environment.NewLine, puzzle.Words);
            status.SelectedItem = new[] { "Da controllare", "Approvato", "Da rifare" }
                .FirstOrDefault(value => string.Equals(value, puzzle.Status, StringComparison.OrdinalIgnoreCase))
                ?? "Da controllare";
            notes.Text = puzzle.Notes;
            issues.Text = string.Join(Environment.NewLine, puzzle.Issues.Select(message => "• " + message));
        }

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= snapshot.Puzzles.Count) return;
            LoadPuzzle(snapshot.Puzzles[list.SelectedIndex]);
        };

        var newPuzzle = Button("+ Nuovo puzzle", () =>
        {
            selectedId = null;
            list.SelectedIndex = -1;
            puzzleId.Text = string.Empty;
            title.Text = string.Empty;
            theme.Text = string.Empty;
            words.Text = string.Empty;
            status.SelectedItem = "Da controllare";
            notes.Text = string.Empty;
            issues.Text = "Nuovo puzzle: il Core assegnerà un ID se il campo ID viene lasciato vuoto.";
            puzzleId.Focus(FocusState.Programmatic);
        });

        var savePuzzle = AsyncButton("Salva puzzle", async () =>
        {
            var result = document.SaveWordSearchPuzzle(
                selectedId,
                puzzleId.Text,
                title.Text,
                theme.Text,
                SplitLines(words.Text),
                status.SelectedItem?.ToString(),
                notes.Text);
            if (!result.Changed && result.Status is not "SAVED")
            {
                report(result.Message);
                return;
            }
            await saveProject();
            report(result.Message);
            refresh();
        });

        var deletePuzzle = AsyncButton("Elimina puzzle", async () =>
        {
            if (!selectedId.HasValue)
            {
                report("Seleziona prima un puzzle da eliminare.");
                return;
            }
            var result = document.DeleteWordSearchPuzzle(selectedId.Value);
            if (result.Changed) await saveProject();
            report(result.Message);
            if (result.Changed) refresh();
        });

        root.Children.Add(Card("Puzzle nel Core", Vertical(
            new TextBlock
            {
                Text = $"{snapshot.Puzzles.Count} puzzle canonici. Le versioni AI portate nel libro compaiono qui automaticamente.",
                TextWrapping = TextWrapping.Wrap
            },
            list,
            Horizontal(newPuzzle, savePuzzle, deletePuzzle))));

        root.Children.Add(Card("Scheda puzzle", Vertical(
            Horizontal(Labeled("ID stabile", puzzleId), Labeled("Stato", status)),
            Labeled("Titolo", title),
            Labeled("Tema", theme),
            Labeled("Parole", words),
            Labeled("Note", notes),
            new TextBlock { Text = "Validazione Core", FontSize = 16 },
            issues)));

        var lexiconList = new ListView
        {
            Height = 190,
            ItemsSource = snapshot.Lexicon
                .Select(entry => $"{entry.Word} · {entry.Category}" +
                                 (string.IsNullOrWhiteSpace(entry.Subcategory) ? "" : $" / {entry.Subcategory}") +
                                 (string.IsNullOrWhiteSpace(entry.Year) ? "" : $" · {entry.Year}"))
                .ToList()
        };
        var lexiconImport = Editor("", "Word;Category;Subcategory;Year\nONDA;Mare;Natura;2020", 130);
        var importLexicon = AsyncButton("Importa testo classificato", async () =>
        {
            var result = document.ImportWordSearchLexiconText(lexiconImport.Text);
            if (result.Changed) await saveProject();
            report(result.Message);
            if (result.Status == "IMPORTED") refresh();
        });
        root.Children.Add(Card("Lessico canonico", Vertical(
            new TextBlock { Text = $"{snapshot.Lexicon.Count} voci nel database parole.", TextWrapping = TextWrapping.Wrap },
            lexiconList,
            Labeled("Incolla CSV/TSV classificato", lexiconImport),
            importLexicon)));

        var export = Horizontal(
            Button("Copia CSV canonico", () => CopyText(document.WordSearchCsv())),
            AsyncButton("Esporta CSV canonico", async () =>
                await exportText("word-search-database.csv", document.WordSearchCsv())));
        root.Children.Add(Card("Export", Vertical(
            new TextBlock
            {
                Text = "L'export nasce dai record canonici attuali, non dalla vecchia casella testuale Uno.",
                TextWrapping = TextWrapping.Wrap
            },
            export)));

        if (!string.IsNullOrWhiteSpace(snapshot.LegacyDatabaseDraft) ||
            !string.IsNullOrWhiteSpace(snapshot.LegacyLexiconDraft))
        {
            root.Children.Add(Card("Recupero bozza Uno precedente — sola lettura", Vertical(
                new TextBlock
                {
                    Text = "Questi valori sono conservati per migrazione ma non vengono più modificati dal workspace. Copia manualmente ciò che serve prima di eliminarli in una futura migrazione esplicita.",
                    TextWrapping = TextWrapping.Wrap
                },
                ReadOnlyEditor(snapshot.LegacyDatabaseDraft, "Vecchia bozza database"),
                ReadOnlyEditor(snapshot.LegacyLexiconDraft, "Vecchia bozza lessico"))));
        }

        return root;
    }

    private static string DisplayPuzzle(DiezWordSearchPuzzleDto puzzle) =>
        $"{puzzle.PuzzleId} · {puzzle.Title} · {puzzle.Theme} · {puzzle.Status} · {puzzle.Words.Count} parole";

    private static IReadOnlyList<string> SplitLines(string? text) =>
        (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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

    private static TextBox ReadOnlyEditor(string text, string placeholder) => new()
    {
        Text = text ?? string.Empty,
        PlaceholderText = placeholder,
        MinHeight = 90,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        IsReadOnly = true,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static ComboBox Combo(IEnumerable<string> values, string selected)
    {
        var items = values.ToList();
        var combo = new ComboBox { ItemsSource = items, MinWidth = 230, HorizontalAlignment = HorizontalAlignment.Left };
        combo.SelectedItem = items.FirstOrDefault(x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
        return combo;
    }

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
