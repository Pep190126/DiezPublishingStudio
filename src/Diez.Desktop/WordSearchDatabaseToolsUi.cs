using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

internal static class WordSearchDatabaseToolsUi
{
    public static void Attach(MainWindow window)
    {
        if (window.Content is not Control root) return;
        var tabs = FindBookTabs(root);
        if (tabs?.ItemsSource is not IEnumerable<TabItem> source) return;
        var items = source.ToList();
        var databaseTab = items.FirstOrDefault(t => string.Equals(t.Header?.ToString(), "Database", StringComparison.Ordinal));
        var checksTab = items.FirstOrDefault(t => string.Equals(t.Header?.ToString(), "Controlli", StringComparison.Ordinal));
        var exportTab = items.FirstOrDefault(t => string.Equals(t.Header?.ToString(), "Esporta", StringComparison.Ordinal));
        if (databaseTab is null || checksTab is null) return;

        if (databaseTab.Content is Control oldDatabase && databaseTab.Content is not WordSearchDatabaseHost)
        {
            var host = new WordSearchDatabaseHost(window, oldDatabase);
            databaseTab.Content = host;
        }

        if (checksTab.Content is Control oldChecks && checksTab.Content is not WordSearchChecksHost)
        {
            var replacement = new WordSearchReplacementPanel(window);
            checksTab.Content = new WordSearchChecksHost(oldChecks, replacement);
            var issueList = Descendants(oldChecks).OfType<ListBox>().FirstOrDefault();
            if (issueList is not null)
            {
                issueList.SelectionChanged += (_, _) =>
                {
                    var text = issueList.SelectedItem?.ToString() ?? string.Empty;
                    replacement.SelectFromIssueText(text);
                };
            }
        }

        if (exportTab?.Content is Control exportContent)
            ReplaceDatabaseExportButton(window, exportContent);
    }

    private static void ReplaceDatabaseExportButton(MainWindow window, Control root)
    {
        foreach (var panel in Descendants(root).OfType<Panel>())
        {
            for (var i = 0; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is not Button button || !string.Equals(button.Content?.ToString(), "Esporta database", StringComparison.Ordinal))
                    continue;
                var replacement = new Button
                {
                    Content = "Esporta database",
                    Width = button.Width,
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };
                ToolTip.SetTip(replacement, "Salva in un unico XLSX il database di parole disponibile e i puzzle creati. Il file resta leggibile in Excel e reimportabile in Diez.");
                replacement.Click += async (_, _) => await ExportFullDatabaseAsync(window);
                panel.Children.RemoveAt(i);
                panel.Children.Insert(i, replacement);
                return;
            }
        }
    }

    private static async Task ExportFullDatabaseAsync(MainWindow window)
    {
        if (!TrySession(window, out var project, out _)) return;
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta database Word Search completo",
            SuggestedFileName = WordSearchFullDatabaseExportService.SuggestedName(project),
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("Database Word Search XLSX") { Patterns = ["*.xlsx"] }]
        });
        if (file is null) return;
        var result = await WordSearchFullDatabaseExportService.ExportAsync(project, file.Path.LocalPath);
        SetStatus(window, result.Message);
    }

    private static TabControl? FindBookTabs(Control root)
    {
        foreach (var control in Descendants(root))
        {
            if (control is not TabControl tabs || tabs.ItemsSource is not IEnumerable<TabItem> items) continue;
            var headers = items.Select(i => i.Header?.ToString()).ToList();
            if (headers.Contains("Database") && headers.Contains("Controlli") && headers.Contains("Esporta")) return tabs;
        }
        return null;
    }

    internal static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    internal static void SetStatus(MainWindow window, string message)
    {
        var status = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as TextBlock;
        if (status is not null) status.Text = message;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        if (root is Panel panel)
        {
            foreach (var child in panel.Children.OfType<Control>())
                foreach (var nested in Descendants(child)) yield return nested;
        }
        if (root is Border border && border.Child is Control borderChild)
            foreach (var nested in Descendants(borderChild)) yield return nested;
        if (root is ContentControl contentControl && contentControl.Content is Control contentChild)
            foreach (var nested in Descendants(contentChild)) yield return nested;
    }
}

internal sealed class WordSearchDatabaseHost : TabControl
{
    private readonly WordSearchSourceDatabasePanel _sourcePanel;

    public WordSearchDatabaseHost(MainWindow window, Control puzzleDatabase)
    {
        _sourcePanel = new WordSearchSourceDatabasePanel(window);
        ItemsSource = new[]
        {
            new TabItem { Header = "Parole disponibili", Content = _sourcePanel },
            new TabItem { Header = "Puzzle creati", Content = puzzleDatabase }
        };
        SelectedIndex = 0;
        SelectionChanged += async (_, _) =>
        {
            if (SelectedIndex == 0) await _sourcePanel.RefreshAsync(autoCollect: true);
        };
        AttachedToVisualTree += async (_, _) => await _sourcePanel.RefreshAsync(autoCollect: true);
    }
}

internal sealed class WordSearchSourceDatabasePanel : Grid
{
    private const int PageSize = 200;
    private readonly MainWindow _window;
    private readonly TextBox _search = new() { Watermark = "Cerca una parola o un valore...", Width = 260 };
    private readonly ComboBox _category = new() { Width = 170 };
    private readonly ComboBox _subcategory = new() { Width = 170 };
    private readonly ComboBox _decade = new() { Width = 125 };
    private readonly ComboBox _series = new() { Width = 150 };
    private readonly ListBox _list = new();
    private readonly TextBlock _summary = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _pageInfo = new();
    private readonly TextBox _newWord = new() { Width = 210, Watermark = "Nuova parola" };
    private readonly Button _previous = Button("←", 45);
    private readonly Button _next = Button("→", 45);
    private List<WordSearchLexiconEntry> _filtered = [];
    private List<WordSearchLexiconEntry> _pageEntries = [];
    private int _page;
    private bool _updatingFilters;
    private bool _collecting;

    public WordSearchSourceDatabasePanel(MainWindow window)
    {
        _window = window;
        RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto");
        RowSpacing = 8;
        Margin = new Thickness(8);

        var collect = Button("Raccogli database", 145);
        var clone = Button("Aggiungi con gli stessi dati", 205);
        collect.Click += async (_, _) => await CollectAsync();
        clone.Click += async (_, _) => await CloneSelectedAsync();
        _previous.Click += (_, _) => { if (_page > 0) { _page--; Render(); } };
        _next.Click += (_, _) => { if ((_page + 1) * PageSize < _filtered.Count) { _page++; Render(); } };
        _search.TextChanged += (_, _) => { _page = 0; ApplyFilters(); };
        _category.SelectionChanged += (_, _) =>
        {
            if (_updatingFilters) return;
            UpdateSubcategories();
            _page = 0;
            ApplyFilters();
        };
        _subcategory.SelectionChanged += (_, _) => { if (!_updatingFilters) { _page = 0; ApplyFilters(); } };
        _decade.SelectionChanged += (_, _) => { if (!_updatingFilters) { _page = 0; ApplyFilters(); } };
        _series.SelectionChanged += (_, _) => { if (!_updatingFilters) { _page = 0; ApplyFilters(); } };

        Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children =
            {
                collect,
                _search,
                Field("Categoria", _category),
                Field("Sottocategoria", _subcategory),
                Field("Serie", _series),
                Field("Decade", _decade)
            }
        });
        Children.Add(_summary.WithGridRow(1));
        Children.Add(_list.WithGridRow(2));
        Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                _previous, _next, _pageInfo,
                new TextBlock { Text = "   Aggiungi una parola ereditando i dati dalla riga selezionata:", VerticalAlignment = VerticalAlignment.Center },
                _newWord, clone
            }
        }.WithGridRow(3));
    }

    public async Task RefreshAsync(bool autoCollect)
    {
        if (!WordSearchDatabaseToolsUi.TrySession(_window, out var project, out var path))
        {
            _list.ItemsSource = null;
            _summary.Text = "Apri prima un progetto Word Search.";
            return;
        }

        if (autoCollect && !_collecting && WordSearchLexiconService.GetEntries(project).Count == 0)
        {
            _collecting = true;
            try
            {
                var result = await WordSearchLexiconService.CollectFromProjectAsync(project, path);
                if (result.Recognized) await ProjectFileStore.SaveAsync(path, project);
            }
            catch { }
            finally { _collecting = false; }
        }
        RefreshFiltersAndList();
    }

    private async Task CollectAsync()
    {
        if (!WordSearchDatabaseToolsUi.TrySession(_window, out var project, out var path)) return;
        _collecting = true;
        try
        {
            var result = await WordSearchLexiconService.CollectFromProjectAsync(project, path);
            if (result.Recognized) await ProjectFileStore.SaveAsync(path, project);
            WordSearchDatabaseToolsUi.SetStatus(_window, result.Message);
            RefreshFiltersAndList();
        }
        finally { _collecting = false; }
    }

    private async Task CloneSelectedAsync()
    {
        if (!WordSearchDatabaseToolsUi.TrySession(_window, out var project, out var path)) return;
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _pageEntries.Count)
        {
            WordSearchDatabaseToolsUi.SetStatus(_window, "Seleziona prima una parola del database da usare come modello.");
            return;
        }
        var word = (_newWord.Text ?? string.Empty).Trim();
        if (word.Length == 0)
        {
            WordSearchDatabaseToolsUi.SetStatus(_window, "Scrivi la nuova parola da aggiungere.");
            return;
        }
        var selected = _pageEntries[_list.SelectedIndex];
        var all = WordSearchLexiconService.GetEntries(project);
        if (all.Any(e => string.Equals(Normalize(e.Word), Normalize(word), StringComparison.OrdinalIgnoreCase)))
        {
            WordSearchDatabaseToolsUi.SetStatus(_window, $"“{word}” è già presente nel database.");
            return;
        }

        var clone = new WordSearchLexiconEntry
        {
            Word = word,
            Category = selected.Category,
            Subcategory = selected.Subcategory,
            Series = selected.Series,
            Decade = selected.Decade,
            Year = selected.Year,
            Relevance = selected.Relevance,
            KdpSafe = selected.KdpSafe,
            Origin = "Aggiunto in Diez",
            Fields = new Dictionary<string, string>(selected.Fields ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        };
        clone.Id = string.Empty;
        all.Add(clone);
        WordSearchLexiconService.SetEntries(project, all);
        await ProjectFileStore.SaveAsync(path, project);
        _newWord.Text = string.Empty;
        RefreshFiltersAndList();
        WordSearchDatabaseToolsUi.SetStatus(_window,
            $"Aggiunta “{word}” con categoria, sottocategoria, serie e periodo ereditati da “{selected.Word}”. La parola originale è rimasta nel database.");
    }

    private void RefreshFiltersAndList()
    {
        if (!WordSearchDatabaseToolsUi.TrySession(_window, out var project, out _)) return;
        var entries = WordSearchLexiconService.GetEntries(project);
        _updatingFilters = true;
        var currentCategory = _category.SelectedItem?.ToString();
        var currentSubcategory = _subcategory.SelectedItem?.ToString();
        var currentDecade = _decade.SelectedItem?.ToString();
        var currentSeries = _series.SelectedItem?.ToString();
        _category.ItemsSource = WithAll(entries.Select(e => e.Category));
        _decade.ItemsSource = WithAll(entries.Select(e => e.Decade));
        _series.ItemsSource = WithAll(entries.Select(e => e.Series));
        SelectValue(_category, currentCategory);
        SelectValue(_decade, currentDecade);
        SelectValue(_series, currentSeries);
        UpdateSubcategories(currentSubcategory);
        _updatingFilters = false;
        ApplyFilters();
    }

    private void UpdateSubcategories(string? preferred = null)
    {
        if (!WordSearchDatabaseToolsUi.TrySession(_window, out var project, out _)) return;
        var category = SelectedFilter(_category);
        var values = WordSearchLexiconService.GetEntries(project)
            .Where(e => category is null || string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Subcategory);
        _subcategory.ItemsSource = WithAll(values);
        SelectValue(_subcategory, preferred ?? _subcategory.SelectedItem?.ToString());
    }

    private void ApplyFilters()
    {
        if (!WordSearchDatabaseToolsUi.TrySession(_window, out var project, out _)) return;
        var entries = WordSearchLexiconService.GetEntries(project);
        var query = (_search.Text ?? string.Empty).Trim();
        var category = SelectedFilter(_category);
        var subcategory = SelectedFilter(_subcategory);
        var decade = SelectedFilter(_decade);
        var series = SelectedFilter(_series);

        IEnumerable<WordSearchLexiconEntry> selected = entries;
        if (category is not null) selected = selected.Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase));
        if (subcategory is not null) selected = selected.Where(e => string.Equals(e.Subcategory, subcategory, StringComparison.OrdinalIgnoreCase));
        if (decade is not null) selected = selected.Where(e => string.Equals(e.Decade, decade, StringComparison.OrdinalIgnoreCase));
        if (series is not null) selected = selected.Where(e => string.Equals(e.Series, series, StringComparison.OrdinalIgnoreCase));
        if (query.Length > 0)
        {
            selected = selected.Where(e =>
                e.Word.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Subcategory.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Series.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Decade.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Year.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (e.Fields?.Values.Any(v => v.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false));
        }
        _filtered = selected.OrderBy(e => e.Word, StringComparer.OrdinalIgnoreCase).ToList();
        if (_page * PageSize >= _filtered.Count) _page = Math.Max(0, (_filtered.Count - 1) / PageSize);
        Render();
        _summary.Text = $"{entries.Count} parole disponibili nel database · {_filtered.Count} corrispondono ai filtri. Vedi tutto il database, non soltanto un'anteprima.";
    }

    private void Render()
    {
        _pageEntries = _filtered.Skip(_page * PageSize).Take(PageSize).ToList();
        _list.ItemsSource = _pageEntries.Select(Display).ToList();
        var pages = Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));
        _pageInfo.Text = $"Pagina {Math.Min(_page + 1, pages)} di {pages} · fino a {PageSize} righe per pagina";
        _previous.IsEnabled = _page > 0;
        _next.IsEnabled = (_page + 1) * PageSize < _filtered.Count;
    }

    private static string Display(WordSearchLexiconEntry e)
    {
        var taxonomy = string.Join(" › ", new[] { e.Category, e.Subcategory, e.Series }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var period = string.Join(" / ", new[] { e.Decade, e.Year }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var safe = e.KdpSafe.HasValue ? (e.KdpSafe.Value ? "KDPSAFE ✓" : "KDPSAFE NO") : "KDPSAFE n/d";
        var relevance = e.Relevance.HasValue ? $"rilevanza {e.Relevance:0.##}" : "rilevanza n/d";
        return $"{e.Id}   {e.Word}   |   {taxonomy}   |   {period}   |   {relevance} · {safe}";
    }

    private static List<string> WithAll(IEnumerable<string> values) => new[] { "Tutte" }
        .Concat(values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
        .ToList();

    private static string? SelectedFilter(ComboBox box)
    {
        var value = box.SelectedItem?.ToString();
        return string.IsNullOrWhiteSpace(value) || value == "Tutte" ? null : value;
    }

    private static void SelectValue(ComboBox box, string? value)
    {
        if (box.ItemsSource is not IEnumerable<string> values) return;
        var list = values.ToList();
        var index = !string.IsNullOrWhiteSpace(value) ? list.FindIndex(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) : -1;
        box.SelectedIndex = index >= 0 ? index : 0;
    }

    private static string Normalize(string value) => string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static StackPanel Field(string label, Control control) => new() { Spacing = 2, Children = { new TextBlock { Text = label }, control } };
    private static Button Button(string text, double width) => new() { Content = text, Width = width, HorizontalContentAlignment = HorizontalAlignment.Center };
}

internal sealed class WordSearchChecksHost : Grid
{
    public WordSearchChecksHost(Control checks, WordSearchReplacementPanel replacement)
    {
        RowDefinitions = new RowDefinitions("*,255");
        RowSpacing = 8;
        Children.Add(checks);
        Children.Add(new Border { Padding = new Thickness(8, 6), Child = replacement }.WithGridRow(1));
    }
}

internal sealed class WordSearchReplacementPanel : Grid
{
    private static readonly Regex PuzzleRegex = new(@"PUZ-\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PositionRegex = new(@"Parol(?:a|e)\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly MainWindow _window;
    private readonly ComboBox _puzzle = new() { Width = 230 };
    private readonly ComboBox _word = new() { Width = 230 };
    private readonly TextBox _maxLength = new() { Width = 80, Watermark = "libera" };
    private readonly ListBox _suggestions = new() { Height = 105 };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private List<WordSearchRecord> _puzzles = [];
    private List<WordSearchReplacementCandidate> _candidates = [];

    public WordSearchReplacementPanel(MainWindow window)
    {
        _window = window;
        RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto");
        RowSpacing = 5;

        var suggest = Button("Trova alternative", 140);
        var replace = Button("Sostituisci", 110);
        suggest.Click += (_, _) => Suggest();
        replace.Click += async (_, _) => await ReplaceAsync();
        _puzzle.SelectionChanged += (_, _) => UpdateWords();

        Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Sostituzione contestuale", FontSize = 17, VerticalAlignment = VerticalAlignment.Center },
                Field("Puzzle", _puzzle), Field("Posizione reale", _word), Field("Lunghezza max", _maxLength), suggest, replace
            }
        });
        Children.Add(_status.WithGridRow(1));
        Children.Add(_suggestions.WithGridRow(2));
        Children.Add(new TextBlock
        {
            Text = "Diez propone parole del database ancora inutilizzate, privilegiando serie, sottocategoria, categoria e periodo compatibili. La sostituzione cambia solo la posizione scelta.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        }.WithGridRow(3));

        AttachedToVisualTree += (_, _) => Refresh();
    }

    public void SelectFromIssueText(string text)
    {
        Refresh();
        var puzzleId = PuzzleRegex.Match(text ?? string.Empty).Value.ToUpperInvariant();
        if (puzzleId.Length > 0)
        {
            var index = _puzzles.FindIndex(p => string.Equals(p.Id, puzzleId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) _puzzle.SelectedIndex = index;
        }
        var positionMatch = PositionRegex.Match(text ?? string.Empty);
        if (positionMatch.Success && int.TryParse(positionMatch.Groups[1].Value, out var position) && position > 0)
            _word.SelectedIndex = position - 1;
    }

    private void Refresh()
    {
        if (!WordSearchDatabaseToolsUi.TrySession(_window, out var project, out _)) return;
        var selectedId = CurrentPuzzle()?.Id;
        _puzzles = WordSearchWorkspaceService.GetRecords(project);
        _puzzle.ItemsSource = _puzzles.Select(p => $"{p.Id} — {p.Title}").ToList();
        var index = selectedId is null ? -1 : _puzzles.FindIndex(p => string.Equals(p.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        _puzzle.SelectedIndex = index >= 0 ? index : (_puzzles.Count > 0 ? 0 : -1);
        UpdateWords();
    }

    private void UpdateWords()
    {
        var puzzle = CurrentPuzzle();
        _word.ItemsSource = puzzle?.Words.Select((w, i) => $"Parola {i + 1:D2} — {w}").ToList() ?? [];
        if (puzzle is not null && puzzle.Words.Count > 0 && _word.SelectedIndex < 0) _word.SelectedIndex = 0;
        _candidates = [];
        _suggestions.ItemsSource = null;
        _status.Text = puzzle is null ? "Nessun puzzle disponibile." : $"Seleziona la parola da correggere in {puzzle.Id}.";
    }

    private void Suggest()
    {
        if (!WordSearchDatabaseToolsUi.TrySession(_window, out var project, out _)) return;
        var puzzle = CurrentPuzzle();
        var position = _word.SelectedIndex + 1;
        if (puzzle is null || position <= 0 || position > puzzle.Words.Count)
        {
            _status.Text = "Seleziona un puzzle e una parola.";
            return;
        }
        int? maxLength = int.TryParse((_maxLength.Text ?? string.Empty).Trim(), out var parsed) && parsed > 0 ? parsed : null;
        _candidates = WordSearchReplacementService.Suggest(project, puzzle, position, maxLength, 30).ToList();
        _suggestions.ItemsSource = _candidates.Select(c => $"{c.Word}   —   {c.Reason}").ToList();
        _suggestions.SelectedIndex = _candidates.Count > 0 ? 0 : -1;
        _status.Text = _candidates.Count > 0
            ? $"{puzzle.Id} → Parola {position:D2}: trovate {_candidates.Count} alternative compatibili e non ancora usate."
            : $"{puzzle.Id} → Parola {position:D2}: nessuna alternativa disponibile con i vincoli attuali. Controlla il database o allarga la lunghezza massima.";
    }

    private async Task ReplaceAsync()
    {
        if (!WordSearchDatabaseToolsUi.TrySession(_window, out var project, out var path)) return;
        var puzzle = CurrentPuzzle();
        var position = _word.SelectedIndex + 1;
        if (puzzle is null || position <= 0 || _suggestions.SelectedIndex < 0 || _suggestions.SelectedIndex >= _candidates.Count)
        {
            _status.Text = "Prima scegli una delle alternative proposte.";
            return;
        }
        var result = WordSearchReplacementService.Replace(project, puzzle, position, _candidates[_suggestions.SelectedIndex]);
        if (result.Success) await ProjectFileStore.SaveAsync(path, project);
        _status.Text = result.Message;
        WordSearchDatabaseToolsUi.SetStatus(_window, result.Message);
        Refresh();
    }

    private WordSearchRecord? CurrentPuzzle() => _puzzle.SelectedIndex >= 0 && _puzzle.SelectedIndex < _puzzles.Count ? _puzzles[_puzzle.SelectedIndex] : null;
    private static StackPanel Field(string label, Control control) => new() { Spacing = 2, Children = { new TextBlock { Text = label }, control } };
    private static Button Button(string text, double width) => new() { Content = text, Width = width, HorizontalContentAlignment = HorizontalAlignment.Center };
}
