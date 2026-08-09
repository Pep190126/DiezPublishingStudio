using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

internal static class FinalizedLibraryUi
{
    public static void Attach(MainWindow window)
    {
        if (window.Content is not Control root) return;
        var row = Descendants(root)
            .OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.Ordinal)));
        if (row is null) return;
        if (row.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Libri finalizzati", StringComparison.Ordinal))) return;

        var button = new Button
        {
            Content = "Libri finalizzati",
            Width = 150,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ToolTip.SetTip(button, "Apre la libreria delle versioni finalizzate: copia identica, rigenerazione dalla versione congelata e nuovo tentativo su Google.");
        button.Click += async (_, _) => await new FinalizedLibraryWindow().ShowDialog(window);
        row.Children.Add(button);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        if (root is Panel panel)
            foreach (var child in panel.Children.OfType<Control>())
                foreach (var nested in Descendants(child)) yield return nested;
        if (root is Border border && border.Child is Control borderChild)
            foreach (var nested in Descendants(borderChild)) yield return nested;
        if (root is ScrollViewer scroll && scroll.Content is Control scrollChild)
            foreach (var nested in Descendants(scrollChild)) yield return nested;
        if (root is ContentControl content && content.Content is Control contentChild)
            foreach (var nested in Descendants(contentChild)) yield return nested;
    }
}

internal sealed class FinalizedLibraryWindow : Window
{
    private readonly ListBox _books = new();
    private readonly ListBox _outputs = new();
    private readonly TextBlock _bookInfo = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _outputInfo = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private List<FinalizedBookRecord> _records = [];

    public FinalizedLibraryWindow()
    {
        Title = "Libri finalizzati — Diez Publishing Studio";
        Width = 1080;
        Height = 700;
        MinWidth = 900;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _books.SelectionChanged += (_, _) => LoadBook();
        _outputs.SelectionChanged += (_, _) => LoadOutput();

        var identical = Button("Copia identica…", 155);
        var regenerate = Button("Rigenera output…", 165);
        var google = Button("Riprova su Google", 165);
        var open = Button("Apri copia archiviata", 175);
        var refresh = Button("Aggiorna", 110);
        identical.Click += async (_, _) => await CopyIdenticalAsync();
        regenerate.Click += async (_, _) => await RegenerateAsync();
        google.Click += async (_, _) => await RetryGoogleAsync();
        open.Click += (_, _) => OpenArchived();
        refresh.Click += (_, _) => Refresh();

        ToolTip.SetTip(identical, "Copia il file finalizzato conservato da Diez e verifica che sia identico byte per byte.");
        ToolTip.SetTip(regenerate, "Rifà l'output dalla versione .diez congelata e dalla stessa ricetta. Il contenuto resta quello della finalizzazione, ma i byte possono differire.");
        ToolTip.SetTip(google, "Invia di nuovo la copia identica archiviata a Google Drive e prova ad aprirla nel browser. Non rigenera il libro.");

        var left = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8,
            Children =
            {
                new TextBlock { Text = "Tutti i libri finalizzati", FontSize = 20 },
                _books,
                _bookInfo
            }
        };
        Grid.SetRow(_books, 1);
        Grid.SetRow(_bookInfo, 2);

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { identical, regenerate, google, open }
        };
        var right = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"),
            RowSpacing = 8,
            Children =
            {
                new TextBlock { Text = "Output conservati per la finalizzazione selezionata", FontSize = 20 },
                _outputs,
                _outputInfo,
                actionRow,
                _status
            }
        };
        Grid.SetRow(_outputs, 1);
        Grid.SetRow(_outputInfo, 2);
        Grid.SetRow(actionRow, 3);
        Grid.SetRow(_status, 4);

        var contentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("4*,6*"),
            ColumnSpacing = 16,
            Children = { left, right }
        };
        Grid.SetColumn(right, 1);

        Content = new Border
        {
            Padding = new Thickness(16),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Libreria dei libri finalizzati",
                        FontSize = 25,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    contentGrid,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children =
                        {
                            refresh,
                            new Button
                            {
                                Content = "Chiudi",
                                Width = 110,
                                HorizontalContentAlignment = HorizontalAlignment.Center
                            }
                        }
                    }
                }
            }
        };
        Grid.SetRow(contentGrid, 1);
        if (((Grid)((Border)Content).Child!).Children[2] is StackPanel bottom && bottom.Children[1] is Button close)
            close.Click += (_, _) => Close();
        Grid.SetRow(((Grid)((Border)Content).Child!).Children[2], 2);

        Opened += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var selectedId = SelectedBook()?.FinalizationId;
        _records = FinalizedLibraryService.LoadAll().ToList();
        _books.ItemsSource = _records.Select(DisplayBook).ToList();
        if (_records.Count == 0)
        {
            _outputs.ItemsSource = null;
            _bookInfo.Text = "Qui compariranno i libri quando Diez conserverà un output finalizzato.";
            _outputInfo.Text = string.Empty;
            _status.Text = "Nessun libro finalizzato archiviato.";
            return;
        }
        var index = selectedId is null ? 0 : _records.FindIndex(r => r.FinalizationId == selectedId.Value);
        _books.SelectedIndex = index < 0 ? 0 : index;
        LoadBook();
    }

    private void LoadBook()
    {
        var book = SelectedBook();
        if (book is null)
        {
            _outputs.ItemsSource = null;
            _bookInfo.Text = "Seleziona un libro.";
            return;
        }
        _bookInfo.Text = $"{book.Title}\nTipo libro: {Fallback(book.BookType, "non indicato")}\nFinalizzato: {FriendlyDate(book.FinalizedAtLocal)}" +
                         (book.PublicationCandidateSequence > 0 ? $" · versione {book.PublicationCandidateSequence}" : string.Empty);
        _outputs.ItemsSource = book.Outputs.Select(DisplayOutput).ToList();
        _outputs.SelectedIndex = book.Outputs.Count > 0 ? 0 : -1;
        LoadOutput();
    }

    private void LoadOutput()
    {
        var selected = SelectedOutput();
        if (selected.Output is null)
        {
            _outputInfo.Text = "Nessun output archiviato per questa finalizzazione.";
            return;
        }
        var output = selected.Output;
        var google = string.IsNullOrWhiteSpace(output.GoogleUrl) ? "non aperto / non confermato" : "collegamento disponibile";
        _outputInfo.Text = $"{output.Label} · {output.FileName}\nDimensione: {FormatBytes(output.SizeBytes)} · impronta: {ShortHash(output.Sha256)}\nGoogle: {google}";
    }

    private async Task CopyIdenticalAsync()
    {
        var selected = SelectedOutput();
        if (selected.Book is null || selected.Output is null) { _status.Text = "Seleziona prima un output."; return; }
        var path = await PickSavePathAsync("Crea una copia identica", selected.Output.FileName);
        if (string.IsNullOrWhiteSpace(path)) return;
        var result = await FinalizedLibraryService.CopyIdenticalAsync(selected.Book.FinalizationId, selected.Output.OutputId, path);
        _status.Text = result.Message;
    }

    private async Task RegenerateAsync()
    {
        var selected = SelectedOutput();
        if (selected.Book is null || selected.Output is null) { _status.Text = "Seleziona prima un output."; return; }
        var path = await PickSavePathAsync("Rigenera dalla versione finalizzata", selected.Output.FileName);
        if (string.IsNullOrWhiteSpace(path)) return;
        _status.Text = "Rigenerazione dalla versione congelata in corso…";
        var result = await FinalizedLibraryService.RegenerateAsync(selected.Book.FinalizationId, selected.Output.OutputId, path);
        _status.Text = result.Message;
    }

    private async Task RetryGoogleAsync()
    {
        var selected = SelectedOutput();
        if (selected.Book is null || selected.Output is null) { _status.Text = "Seleziona prima un output."; return; }
        _status.Text = "Invio della copia archiviata a Google Drive…";
        var result = await FinalizedLibraryService.RetryGoogleAsync(selected.Book.FinalizationId, selected.Output.OutputId);
        _status.Text = result.Message;
        Refresh();
    }

    private void OpenArchived()
    {
        var selected = SelectedOutput();
        if (selected.Book is null || selected.Output is null) { _status.Text = "Seleziona prima un output."; return; }
        var result = FinalizedLibraryService.OpenArchived(selected.Book.FinalizationId, selected.Output.OutputId);
        _status.Text = result.Message;
    }

    private async Task<string?> PickSavePathAsync(string title, string fileName)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.');
        var pattern = string.IsNullOrWhiteSpace(extension) ? "*.*" : "*." + extension;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = fileName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(string.IsNullOrWhiteSpace(extension) ? "File" : extension.ToUpperInvariant()) { Patterns = [pattern] }]
        });
        return file?.Path.LocalPath;
    }

    private FinalizedBookRecord? SelectedBook() =>
        _books.SelectedIndex >= 0 && _books.SelectedIndex < _records.Count ? _records[_books.SelectedIndex] : null;

    private (FinalizedBookRecord? Book, FinalizedOutputRecord? Output) SelectedOutput()
    {
        var book = SelectedBook();
        if (book is null || _outputs.SelectedIndex < 0 || _outputs.SelectedIndex >= book.Outputs.Count) return (book, null);
        return (book, book.Outputs[_outputs.SelectedIndex]);
    }

    private static string DisplayBook(FinalizedBookRecord book) =>
        $"{book.Title}  ·  {Fallback(book.BookType, "Tipo non indicato")}  ·  {FriendlyDate(book.FinalizedAtLocal)}" +
        (book.PublicationCandidateSequence > 0 ? $"  ·  v{book.PublicationCandidateSequence}" : string.Empty);

    private static string DisplayOutput(FinalizedOutputRecord output) =>
        $"{output.Label}  ·  {output.FileName}  ·  {FormatBytes(output.SizeBytes)}";

    private static Button Button(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static string Fallback(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private static string FriendlyDate(string value) => DateTimeOffset.TryParse(value, out var date) ? date.LocalDateTime.ToString("dd/MM/yyyy HH:mm") : value;
    private static string ShortHash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value[..Math.Min(16, value.Length)];
    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024d).ToString("0.0") + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / 1024d / 1024d).ToString("0.0") + " MB";
        return (bytes / 1024d / 1024d / 1024d).ToString("0.00") + " GB";
    }
}
