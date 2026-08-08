using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

internal static class EditionWorkflowUi
{
    private const string ProjectFieldName = "_project";
    private const string ProjectPathFieldName = "_currentProjectPath";
    private const string StatusFieldName = "_status";

    public static void Attach(MainWindow window)
    {
        if (window.Content is not Border border || border.Child is not StackPanel root)
            return;

        var projectButtons = root.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Button>().Any(button =>
                                         string.Equals(button.Content?.ToString(), "Nuovo progetto", StringComparison.Ordinal)));
        if (projectButtons is null) return;
        if (projectButtons.Children.OfType<Button>().Any(button =>
                string.Equals(button.Content?.ToString(), "Edizione / Preflight", StringComparison.Ordinal)))
            return;

        var editionButton = new Button
        {
            Content = "Edizione / Preflight",
            Width = 160,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        editionButton.Click += async (_, _) =>
        {
            if (!TryGetSession(window, out var project, out var projectPath))
            {
                SetMainStatus(window, "Prima crea o apri un progetto .diez per usare metadati, Edition Freeze, preflight e Publication Candidate.");
                return;
            }

            var dialog = new EditionPreflightWindow(project, projectPath);
            await dialog.ShowDialog(window);

            var freezeCount = EditionFreezeService.FreezeCount(project);
            var candidateCount = PublicationCandidateService.Count(project);
            var freezeCurrent = freezeCount > 0 && EditionFreezeService.IsLatestFreezeCurrent(project);
            var candidateCurrent = candidateCount > 0 && PublicationCandidateService.IsLatestCandidateCurrent(project);
            var metadataTitle = string.IsNullOrWhiteSpace(project.EditionMetadata?.Title) ? "titolo mancante" : project.EditionMetadata.Title;
            SetMainStatus(window,
                $"Edizione: {metadataTitle} · {freezeCount} freeze · ultimo {(freezeCurrent ? "corrente" : freezeCount == 0 ? "assente" : "superato")} · " +
                $"{candidateCount} Publication Candidate · ultimo {(candidateCurrent ? "corrente" : candidateCount == 0 ? "assente" : "superato")}.");
        };

        projectButtons.Children.Add(editionButton);
    }

    private static bool TryGetSession(MainWindow window, out PreviewProject project, out string projectPath)
    {
        project = null!;
        projectPath = string.Empty;

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var projectField = typeof(MainWindow).GetField(ProjectFieldName, flags);
        var pathField = typeof(MainWindow).GetField(ProjectPathFieldName, flags);
        if (projectField?.GetValue(window) is not PreviewProject currentProject) return false;
        if (pathField?.GetValue(window) is not string currentPath || string.IsNullOrWhiteSpace(currentPath)) return false;

        project = currentProject;
        projectPath = currentPath;
        return true;
    }

    private static void SetMainStatus(MainWindow window, string message)
    {
        var statusField = typeof(MainWindow).GetField(StatusFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (statusField?.GetValue(window) is TextBlock status) status.Text = message;
    }
}

internal sealed class EditionPreflightWindow : Window
{
    private readonly PreviewProject _project;
    private readonly string _projectPath;
    private readonly TextBlock _metadataState;
    private readonly TextBlock _freezeState;
    private readonly TextBlock _candidateState;
    private readonly TextBlock _summary;
    private readonly ListBox _checks;

    public EditionPreflightWindow(PreviewProject project, string projectPath)
    {
        _project = project;
        _projectPath = projectPath;

        Title = "Edition Metadata / Freeze / Preflight / Publication Candidate";
        Width = 1020;
        Height = 780;
        MinWidth = 820;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var heading = new TextBlock
        {
            Text = "Controllo finale dell'edizione",
            FontSize = 24,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var explanation = new TextBlock
        {
            Text = "I metadati bibliografici fanno parte dell'edizione. Edition Freeze fotografa metadati, Master, Bible e piano illustrazioni; il preflight verifica che tutto sia pronto. Con preflight READY puoi creare il Publication Candidate immutabile. La consegna DOCX/CSV/XLSX, immagini e Production Package si effettua poi da Export / Handoff.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        _metadataState = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _freezeState = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _candidateState = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _summary = new TextBlock
        {
            FontSize = 17,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        _checks = new ListBox
        {
            Height = 300,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var metadataButton = new Button { Content = "Metadati edizione", Width = 165 };
        metadataButton.Click += async (_, _) => await EditMetadataAsync();
        var freezeButton = new Button { Content = "Crea Edition Freeze", Width = 170 };
        freezeButton.Click += async (_, _) => await CreateFreezeAsync();
        var preflightButton = new Button { Content = "Esegui preflight", Width = 145 };
        preflightButton.Click += (_, _) => RefreshPreflight();
        var publicationButton = new Button { Content = "Crea Publication Candidate", Width = 205 };
        publicationButton.Click += async (_, _) => await CreatePublicationCandidateAsync();
        var closeButton = new Button { Content = "Chiudi", Width = 100 };
        closeButton.Click += (_, _) => Close();

        var primaryButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { metadataButton, freezeButton, preflightButton }
        };
        var publicationButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { publicationButton, closeButton }
        };
        var buttons = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { primaryButtons, publicationButtons }
        };

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,*,Auto"),
                RowSpacing = 9,
                Children =
                {
                    heading,
                    explanation.WithGridRow(1),
                    _metadataState.WithGridRow(2),
                    _freezeState.WithGridRow(3),
                    _candidateState.WithGridRow(4),
                    _summary.WithGridRow(5),
                    _checks.WithGridRow(6),
                    buttons.WithGridRow(7)
                }
            }
        };

        Opened += (_, _) => RefreshPreflight();
    }

    private async Task EditMetadataAsync()
    {
        var dialog = new EditionMetadataWindow(_project, _projectPath);
        await dialog.ShowDialog(this);
        RefreshPreflight();
    }

    private async Task CreateFreezeAsync()
    {
        var before = EditionFreezeService.FreezeCount(_project);
        var result = EditionFreezeService.CreateFreeze(_project);
        var after = EditionFreezeService.FreezeCount(_project);

        if (result.Freeze is null)
        {
            _summary.Text = result.Message;
            UpdateEditionState();
            return;
        }

        try
        {
            if (after > before)
                await ProjectFileStore.SaveAsync(_projectPath, _project);
            _summary.Text = result.Message;
            RefreshPreflight();
        }
        catch (Exception ex)
        {
            _summary.Text = $"Edition Freeze creato in memoria, ma il salvataggio del .diez è fallito: {ex.Message}. Riapri il progetto prima di continuare.";
            UpdateEditionState();
        }
    }

    private async Task CreatePublicationCandidateAsync()
    {
        var before = PublicationCandidateService.Count(_project);
        var result = PublicationCandidateService.Create(_project);
        var after = PublicationCandidateService.Count(_project);

        if (result.Candidate is null)
        {
            _summary.Text = result.Message;
            RefreshPreflight();
            return;
        }

        try
        {
            if (after > before)
                await ProjectFileStore.SaveAsync(_projectPath, _project);
            _summary.Text = result.Message;
            RefreshPreflight();
        }
        catch (Exception ex)
        {
            _summary.Text = $"Publication Candidate creato in memoria, ma il salvataggio del .diez è fallito: {ex.Message}. Riapri il progetto prima di continuare.";
            UpdateEditionState();
        }
    }

    private void RefreshPreflight(bool preserveSummary = false)
    {
        UpdateEditionState();
        var result = EditionFreezeService.RunPreflight(_project);
        if (!preserveSummary) _summary.Text = result.Summary;
        _checks.ItemsSource = result.Checks
            .Select(check =>
            {
                var symbol = check.Passed ? "✓" : check.Severity == "Warning" ? "!" : "✕";
                var level = check.Severity == "Warning" ? "ATTENZIONE" : "BLOCCANTE";
                return $"{symbol}  [{level}]  {check.Code} — {check.Message}";
            })
            .ToList();
    }

    private void UpdateEditionState()
    {
        var metadata = _project.EditionMetadata ?? new EditionMetadata();
        var title = string.IsNullOrWhiteSpace(metadata.Title) ? "MANCANTE" : metadata.Title;
        var creator = string.IsNullOrWhiteSpace(metadata.Creator) ? "autore non indicato" : metadata.Creator;
        var language = string.IsNullOrWhiteSpace(metadata.Language) ? "lingua mancante" : metadata.Language;
        var isbn = string.IsNullOrWhiteSpace(metadata.Isbn) ? "senza ISBN" : $"ISBN {metadata.Isbn}";
        _metadataState.Text = $"Metadati: {title} · {creator} · {language} · {isbn}.";

        var freezeCount = EditionFreezeService.FreezeCount(_project);
        var latestFreeze = EditionFreezeService.GetLatestFreeze(_project);
        if (latestFreeze is null)
        {
            _freezeState.Text = "Edition Freeze: nessuno snapshot creato.";
        }
        else
        {
            var sequence = string.IsNullOrWhiteSpace(latestFreeze.ProposedValue) ? freezeCount.ToString() : latestFreeze.ProposedValue;
            var current = EditionFreezeService.IsLatestFreezeCurrent(_project);
            _freezeState.Text = $"Edition Freeze #{sequence} · {latestFreeze.CreatedAtLocal} · totale {freezeCount} · stato: {(current ? "CORRENTE" : "SUPERATO")}.";
        }

        var candidateCount = PublicationCandidateService.Count(_project);
        var latestCandidate = PublicationCandidateService.GetLatest(_project);
        if (latestCandidate is null)
        {
            _candidateState.Text = "Publication Candidate: nessuna copia editoriale finale creata.";
        }
        else
        {
            var sequence = string.IsNullOrWhiteSpace(latestCandidate.ProposedValue) ? candidateCount.ToString() : latestCandidate.ProposedValue;
            var current = PublicationCandidateService.IsLatestCandidateCurrent(_project);
            _candidateState.Text = $"Publication Candidate #{sequence} · {latestCandidate.CreatedAtLocal} · totale {candidateCount} · stato: {(current ? "CORRENTE / PRONTO PER HANDOFF" : "SUPERATO")}.";
        }
    }
}

internal sealed class EditionMetadataWindow : Window
{
    private readonly PreviewProject _project;
    private readonly string _projectPath;
    private readonly TextBox _title;
    private readonly TextBox _subtitle;
    private readonly TextBox _creator;
    private readonly TextBox _language;
    private readonly TextBox _publisher;
    private readonly TextBox _isbn;
    private readonly TextBox _description;
    private readonly TextBlock _status;

    public EditionMetadataWindow(PreviewProject project, string projectPath)
    {
        _project = project;
        _projectPath = projectPath;
        var metadata = project.EditionMetadata ?? new EditionMetadata();

        Title = "Metadati edizione";
        Width = 720;
        Height = 720;
        MinWidth = 600;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _title = MakeTextBox(metadata.Title);
        _subtitle = MakeTextBox(metadata.Subtitle);
        _creator = MakeTextBox(metadata.Creator);
        _language = MakeTextBox(metadata.Language);
        _publisher = MakeTextBox(metadata.Publisher);
        _isbn = MakeTextBox(metadata.Isbn);
        _description = MakeTextBox(metadata.Description);
        _description.AcceptsReturn = true;
        _description.Height = 110;
        _description.TextWrapping = Avalonia.Media.TextWrapping.Wrap;

        _status = new TextBlock
        {
            Text = "Titolo e lingua sono richiesti dal preflight. Autore, editore, ISBN e descrizione possono essere completati quando disponibili.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var cancel = new Button { Content = "Annulla", Width = 120 };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = "Salva metadati", Width = 160 };
        save.Click += async (_, _) => await SaveAsync();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, save }
        };

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 9,
                Children =
                {
                    new TextBlock { Text = "Metadati bibliografici dell'edizione", FontSize = 23 },
                    Field("Titolo *", _title),
                    Field("Sottotitolo", _subtitle),
                    Field("Autore / creatore", _creator),
                    Field("Lingua * (es. it, en, fr)", _language),
                    Field("Editore / imprint", _publisher),
                    Field("ISBN-10 / ISBN-13", _isbn),
                    Field("Descrizione", _description),
                    _status,
                    buttons
                }
            }
        };
    }

    private async Task SaveAsync()
    {
        var normalizedIsbn = EditionMetadataService.NormalizeIsbn(_isbn.Text);
        if (!string.IsNullOrWhiteSpace(normalizedIsbn) && !EditionMetadataService.IsValidIsbn(normalizedIsbn))
        {
            _status.Text = "ISBN non valido. Correggilo oppure lascia il campo vuoto.";
            return;
        }

        var current = _project.EditionMetadata ?? new EditionMetadata();
        var backup = new EditionMetadata
        {
            Title = current.Title,
            Subtitle = current.Subtitle,
            Creator = current.Creator,
            Language = current.Language,
            Publisher = current.Publisher,
            Isbn = current.Isbn,
            Description = current.Description
        };

        var result = EditionMetadataService.Update(
            _project,
            _title.Text,
            _subtitle.Text,
            _creator.Text,
            _language.Text,
            _publisher.Text,
            _isbn.Text,
            _description.Text);

        if (!result.Changed)
        {
            _status.Text = result.Message;
            return;
        }

        try
        {
            await ProjectFileStore.SaveAsync(_projectPath, _project);
            Close();
        }
        catch (Exception ex)
        {
            _project.EditionMetadata = backup;
            _status.Text = $"Metadati non salvati: {ex.Message}";
        }
    }

    private static TextBox MakeTextBox(string? value) => new()
    {
        Text = value ?? string.Empty,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static StackPanel Field(string label, Control input) => new()
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label },
            input
        }
    };
}