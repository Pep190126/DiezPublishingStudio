using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

internal static class EditionWorkflowUi
{
    private const string ProjectFieldName = "_project";
    private const string ProjectPathFieldName = "_currentProjectPath";
    private const string StatusFieldName = "_status";

    public static void Attach(MainWindow window)
    {
        window.Title = "Diez Publishing Studio — 0.9 Preview";

        if (window.Content is not Border border || border.Child is not StackPanel root)
            return;

        var subtitle = root.Children
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text?.StartsWith("Preview 0.8", StringComparison.Ordinal) == true ||
                                 t.Text?.StartsWith("Preview 0.9", StringComparison.Ordinal) == true);
        if (subtitle is not null)
            subtitle.Text = "Preview 0.9 — Editable Master + Edition Freeze + preflight + Publication Candidate";

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
                SetMainStatus(window, "Prima crea o apri un progetto .diez per usare Edition Freeze, preflight e Publication Candidate.");
                return;
            }

            var dialog = new EditionPreflightWindow(project, projectPath);
            await dialog.ShowDialog(window);

            var freezeCount = EditionFreezeService.FreezeCount(project);
            var candidateCount = PublicationCandidateService.Count(project);
            var freezeCurrent = freezeCount > 0 && EditionFreezeService.IsLatestFreezeCurrent(project);
            var candidateCurrent = candidateCount > 0 && PublicationCandidateService.IsLatestCandidateCurrent(project);
            SetMainStatus(window,
                $"Edizione: {freezeCount} freeze · ultimo {(freezeCurrent ? "corrente" : freezeCount == 0 ? "assente" : "superato")} · " +
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
    private readonly TextBlock _freezeState;
    private readonly TextBlock _candidateState;
    private readonly TextBlock _summary;
    private readonly ListBox _checks;

    public EditionPreflightWindow(PreviewProject project, string projectPath)
    {
        _project = project;
        _projectPath = projectPath;

        Title = "Edition Freeze / Preflight / Publication Candidate";
        Width = 1020;
        Height = 720;
        MinWidth = 820;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var heading = new TextBlock
        {
            Text = "Controllo finale dell'edizione",
            FontSize = 24,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var explanation = new TextBlock
        {
            Text = "Edition Freeze fotografa Master e Bible. Il preflight verifica che l'edizione sia pronta. Solo con preflight READY puoi creare un Publication Candidate immutabile ed esportare il pacchetto editoriale ZIP.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        _freezeState = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _candidateState = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _summary = new TextBlock
        {
            FontSize = 17,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        _checks = new ListBox
        {
            Height = 330,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var freezeButton = new Button { Content = "Crea Edition Freeze", Width = 170 };
        freezeButton.Click += async (_, _) => await CreateFreezeAsync();
        var preflightButton = new Button { Content = "Esegui preflight", Width = 145 };
        preflightButton.Click += (_, _) => RefreshPreflight();
        var publicationButton = new Button { Content = "Crea Publication Candidate", Width = 205 };
        publicationButton.Click += async (_, _) => await CreatePublicationCandidateAsync();
        var exportButton = new Button { Content = "Esporta pacchetto ZIP", Width = 185 };
        exportButton.Click += async (_, _) => await ExportPublicationPackageAsync();
        var closeButton = new Button { Content = "Chiudi", Width = 100 };
        closeButton.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { freezeButton, preflightButton, publicationButton, exportButton, closeButton }
        };

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto"),
                RowSpacing = 10,
                Children =
                {
                    heading,
                    explanation.WithGridRow(1),
                    _freezeState.WithGridRow(2),
                    _candidateState.WithGridRow(3),
                    _summary.WithGridRow(4),
                    _checks.WithGridRow(5),
                    buttons.WithGridRow(6)
                }
            }
        };

        Opened += (_, _) => RefreshPreflight();
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

    private async Task ExportPublicationPackageAsync()
    {
        var latest = PublicationCandidateService.GetLatest(_project);
        if (latest is null || !PublicationCandidateService.IsLatestCandidateCurrent(_project))
        {
            _summary.Text = "Prima crea un Publication Candidate corrente da un preflight READY.";
            RefreshPreflight();
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta pacchetto editoriale Diez",
            SuggestedFileName = PublicationCandidateService.SuggestedPackageName(_project),
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("Pacchetto editoriale ZIP") { Patterns = ["*.zip"] }]
        });
        if (file is null) return;

        try
        {
            var result = await PublicationCandidateService.ExportPackageAsync(_project, file.Path.LocalPath);
            _summary.Text = result.Message;
            RefreshPreflight(preserveSummary: true);
        }
        catch (Exception ex)
        {
            _summary.Text = $"Esportazione del pacchetto editoriale fallita: {ex.Message}";
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
            _candidateState.Text = $"Publication Candidate #{sequence} · {latestCandidate.CreatedAtLocal} · totale {candidateCount} · stato: {(current ? "CORRENTE / ESPORTABILE" : "SUPERATO")}.";
        }
    }
}
