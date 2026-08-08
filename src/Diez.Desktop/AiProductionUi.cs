using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

internal static class AiProductionUi
{
    private const string ProjectFieldName = "_project";
    private const string ProjectPathFieldName = "_currentProjectPath";
    private const string StatusFieldName = "_status";

    public static void Attach(MainWindow window)
    {
        if (window.Content is not Border border || border.Child is not StackPanel root) return;
        var projectButtons = root.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.Ordinal)));
        if (projectButtons is null) return;
        if (projectButtons.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Produzione AI", StringComparison.Ordinal))) return;

        var button = new Button
        {
            Content = "Produzione AI",
            Width = 145,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ToolTip.SetTip(button, "Gestisce prompt e risultati AI senza perdere il collegamento fra richiesta, file generato e decisione finale.");
        button.GotFocus += (_, _) => SetMainStatus(window, "Produzione AI: crea una coda di prompt, collega i risultati e approva o rigenera ogni elemento.");
        button.Click += async (_, _) =>
        {
            if (!TryGetSession(window, out var project, out var path))
            {
                SetMainStatus(window, "Prima crea o apri un progetto .diez per usare la Produzione AI.");
                return;
            }
            var dialog = new AiProductionWindow(project, path, message => SetMainStatus(window, message));
            await dialog.ShowDialog(window);
        };
        projectButtons.Children.Add(button);
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

internal sealed class AiProductionWindow : Window
{
    private readonly PreviewProject _project;
    private readonly string _projectPath;
    private readonly Action<string> _mainStatus;
    private readonly ListBox _jobs;
    private readonly TextBox _brief;
    private readonly TextBlock _selectedInfo;
    private readonly TextBox _request;
    private readonly TextBox _prompt;
    private readonly TextBox _result;
    private readonly TextBlock _status;
    private List<AiProductionJob> _orderedJobs = [];
    private bool _loading;

    public AiProductionWindow(PreviewProject project, string projectPath, Action<string> mainStatus)
    {
        _project = project;
        _projectPath = projectPath;
        _mainStatus = mainStatus;

        Title = $"Produzione AI — Diez {ProductInfo.DisplayVersion}";
        Width = 1180;
        Height = 760;
        MinWidth = 980;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _brief = new TextBox
        {
            Text = _project.AiProduction?.ProjectBrief ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Height = 82,
            Watermark = "Regole comuni del progetto: stile, pubblico, tono, vincoli, formato..."
        };
        ToolTip.SetTip(_brief, "Brief comune ereditato dai nuovi prompt. Per un coloring, qui puoi definire una volta stile, pubblico e vincoli grafici.");

        var saveBrief = MakeButton("Salva brief", 120);
        saveBrief.Click += async (_, _) => await SaveBriefAsync();
        var newJob = MakeButton("Nuovo job", 120);
        newJob.Click += async (_, _) => await NewJobAsync();
        var csv = MakeButton("Prompt CSV", 120);
        csv.Click += async (_, _) => await ExportPromptCsvAsync();
        var xlsx = MakeButton("Prompt XLSX", 120);
        xlsx.Click += async (_, _) => await ExportPromptXlsxAsync();

        _jobs = new ListBox { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        _jobs.SelectionChanged += (_, _) => LoadSelected();
        ToolTip.SetTip(_jobs, "Ogni riga è un lavoro AI tracciato. Selezionalo per vedere prompt, risultato e stato.");

        _selectedInfo = new TextBlock { Text = "Nessun job selezionato", FontSize = 17, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _request = new TextBox { AcceptsReturn = true, Height = 82, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _prompt = new TextBox { AcceptsReturn = true, Height = 170, IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _result = new TextBox
        {
            AcceptsReturn = true,
            Height = 115,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Per testo o dati puoi incollare qui il risultato dell'AI. Per immagini usa Collega file risultato."
        };

        var rebuild = MakeButton("Ricrea prompt", 130);
        rebuild.Click += async (_, _) => await RebuildPromptAsync();
        var copy = MakeButton("Copia prompt", 125);
        copy.Click += async (_, _) => await CopyPromptAsync();
        var saveResult = MakeButton("Salva risultato", 130);
        saveResult.Click += async (_, _) => await SaveTextResultAsync();
        var attachFile = MakeButton("Collega file risultato", 170);
        attachFile.Click += async (_, _) => await AttachFileAsync();

        var approve = MakeButton("Approva", 110);
        approve.Click += async (_, _) => await ChangeDecisionAsync("approve");
        var revise = MakeButton("Da rifare", 110);
        revise.Click += async (_, _) => await ChangeDecisionAsync("revise");
        var reject = MakeButton("Scarta", 110);
        reject.Click += async (_, _) => await ChangeDecisionAsync("reject");
        var apply = MakeButton("Applica al testo", 145);
        apply.Click += async (_, _) => await ApplyTextAsync();

        _status = new TextBlock
        {
            Text = "Crea un job per ogni immagine, testo o tabella da produrre. Diez conserva il collegamento fra prompt e risultato.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        AddHelp(rebuild, "Aggiorna la richiesta del job e ricostruisce il prompt usando il brief generale corrente.");
        AddHelp(copy, "Copia il prompt negli appunti: puoi incollarlo in qualunque chat o servizio AI.");
        AddHelp(saveResult, "Salva nel job il testo o i dati incollati qui sotto. Non li applica automaticamente al libro.");
        AddHelp(attachFile, "Associa al job un file generato dall'AI e lo incorpora nel .diez. È il percorso consigliato per le immagini.");
        AddHelp(approve, "Dichiara che hai controllato il risultato e lo approvi. Per il testo serve ancora Applica al testo.");
        AddHelp(revise, "Segna il risultato da rifare, mantenendo la storia del job.");
        AddHelp(reject, "Scarta il risultato ma mantiene il job nella cronologia.");
        AddHelp(apply, "Solo per job di testo approvati: sostituisce esplicitamente il contenuto collegato nel Testo di lavoro.");

        var left = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 8,
            Margin = new Thickness(0, 0, 10, 0),
            Children =
            {
                new TextBlock { Text = "Coda di produzione", FontSize = 19 },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { newJob, csv, xlsx }
                }.WithGridRow(1),
                _jobs.WithGridRow(2)
            }
        };

        var right = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,82,Auto,170,Auto,115,Auto,Auto"),
            RowSpacing = 6,
            Children =
            {
                _selectedInfo,
                new TextBlock { Text = "Richiesta specifica" }.WithGridRow(1),
                _request.WithGridRow(2),
                new TextBlock { Text = "Prompt da usare con l'AI" }.WithGridRow(3),
                _prompt.WithGridRow(4),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { rebuild, copy, attachFile }
                }.WithGridRow(5),
                _result.WithGridRow(6),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { saveResult, approve, revise, reject, apply }
                }.WithGridRow(7),
                _status.WithGridRow(8)
            }
        };

        Content = new Border
        {
            Padding = new Thickness(16),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*"),
                ColumnDefinitions = new ColumnDefinitions("2*,3*"),
                RowSpacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Brief comune del progetto — scrivilo una volta, poi ogni job eredita queste regole",
                        FontSize = 17
                    },
                    _brief.WithGridRow(1),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { saveBrief }
                    }.WithGridRow(1).WithGridColumn(1),
                    left.WithGridRow(2),
                    right.WithGridRow(2).WithGridColumn(1)
                }
            }
        };

        RefreshJobs();
    }

    private AiProductionJob? SelectedJob =>
        _jobs.SelectedIndex >= 0 && _jobs.SelectedIndex < _orderedJobs.Count ? _orderedJobs[_jobs.SelectedIndex] : null;

    private async Task SaveBriefAsync()
    {
        AiProductionService.SetProjectBrief(_project, _brief.Text);
        await SaveAsync("Brief generale salvato. I job esistenti non vengono cambiati automaticamente: usa Ricrea prompt solo dove vuoi aggiornarli.");
    }

    private async Task NewJobAsync()
    {
        var dialog = new AiJobEditorWindow(_project);
        var draft = await dialog.ShowDialog<AiJobDraft?>(this);
        if (draft is null) return;
        var job = AiProductionService.CreateJob(_project, draft.OutputType, draft.Title, draft.Request, draft.TargetContentId);
        await ProjectFileStore.SaveAsync(_projectPath, _project);
        RefreshJobs(job.JobId);
        Report($"Creato {job.Code}. Copia il prompt nella tua AI oppure esporta tutta la coda in CSV/XLSX.");
    }

    private async Task RebuildPromptAsync()
    {
        var job = SelectedJob;
        if (job is null) { Report("Seleziona prima un job."); return; }
        job.Request = (_request.Text ?? string.Empty).Trim();
        AiProductionService.RebuildPrompt(_project, job);
        await ProjectFileStore.SaveAsync(_projectPath, _project);
        LoadSelected();
        Report($"Prompt {job.Code} ricostruito con il brief corrente.");
    }

    private async Task CopyPromptAsync()
    {
        var job = SelectedJob;
        if (job is null) { Report("Seleziona prima un job."); return; }
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) { Report("Appunti di sistema non disponibili."); return; }
        await clipboard.SetTextAsync(job.Prompt);
        Report($"Prompt {job.Code} copiato. Incollalo nella chat/AI che preferisci; il codice resta il riferimento per riportare indietro il risultato.");
    }

    private async Task SaveTextResultAsync()
    {
        var job = SelectedJob;
        if (job is null) { Report("Seleziona prima un job."); return; }
        AiProductionService.SetTextResult(job, _result.Text);
        await ProjectFileStore.SaveAsync(_projectPath, _project);
        RefreshJobs(job.JobId);
        Report(string.IsNullOrWhiteSpace(job.ResultText) ? "Risultato svuotato." : $"Risultato testuale salvato per {job.Code}. Ora controllalo prima di approvarlo.");
    }

    private async Task AttachFileAsync()
    {
        var job = SelectedJob;
        if (job is null) { Report("Seleziona prima un job."); return; }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Collega risultato a {job.Code}",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Risultati supportati") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp", "*.txt", "*.md", "*.csv", "*.xlsx", "*.docx"] }
            ]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;
        var result = await AiProductionService.AttachResultFileAsync(_project, _projectPath, job, file.Path.LocalPath);
        RefreshJobs(job.JobId);
        Report(result.Message);
    }

    private async Task ChangeDecisionAsync(string action)
    {
        var job = SelectedJob;
        if (job is null) { Report("Seleziona prima un job."); return; }
        var result = action switch
        {
            "approve" => AiProductionService.Approve(_project, job),
            "revise" => AiProductionService.NeedsRevision(job),
            _ => AiProductionService.Reject(job)
        };
        if (result.Success) await ProjectFileStore.SaveAsync(_projectPath, _project);
        RefreshJobs(job.JobId);
        Report(result.Message);
    }

    private async Task ApplyTextAsync()
    {
        var job = SelectedJob;
        if (job is null) { Report("Seleziona prima un job."); return; }
        var result = AiProductionService.ApplyApprovedText(_project, job);
        if (result.Success) await ProjectFileStore.SaveAsync(_projectPath, _project);
        RefreshJobs(job.JobId);
        Report(result.Message);
    }

    private async Task ExportPromptCsvAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta coda prompt AI in CSV",
            SuggestedFileName = AiPromptPackExportService.SuggestedCsvFileName(_project),
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV UTF-8") { Patterns = ["*.csv"] }]
        });
        if (file is null) return;
        Report((await AiPromptPackExportService.ExportCsvAsync(_project, file.Path.LocalPath)).Message);
    }

    private async Task ExportPromptXlsxAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta coda prompt AI in XLSX",
            SuggestedFileName = AiPromptPackExportService.SuggestedXlsxFileName(_project),
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("Excel XLSX") { Patterns = ["*.xlsx"] }]
        });
        if (file is null) return;
        Report((await AiPromptPackExportService.ExportXlsxAsync(_project, file.Path.LocalPath)).Message);
    }

    private void RefreshJobs(Guid? selectId = null)
    {
        _orderedJobs = _project.AiProductionJobs.OrderBy(j => j.Code, StringComparer.OrdinalIgnoreCase).ToList();
        _loading = true;
        _jobs.ItemsSource = _orderedJobs.Select(j => $"{j.Code} · {AiProductionService.DisplayType(j.OutputType)} · {j.Title} · {AiProductionService.DisplayStatus(j.Status)}").ToList();
        if (_orderedJobs.Count == 0) _jobs.SelectedIndex = -1;
        else if (selectId.HasValue)
            _jobs.SelectedIndex = Math.Max(0, _orderedJobs.FindIndex(j => j.JobId == selectId.Value));
        else if (_jobs.SelectedIndex < 0) _jobs.SelectedIndex = 0;
        _loading = false;
        LoadSelected();
    }

    private void LoadSelected()
    {
        if (_loading) return;
        var job = SelectedJob;
        if (job is null)
        {
            _selectedInfo.Text = "Nessun job selezionato";
            _request.Text = string.Empty;
            _prompt.Text = string.Empty;
            _result.Text = string.Empty;
            return;
        }
        _selectedInfo.Text = $"{job.Code} · {AiProductionService.DisplayType(job.OutputType)} · {AiProductionService.DisplayStatus(job.Status)}\n{job.Title}";
        _request.Text = job.Request;
        _prompt.Text = job.Prompt;
        _result.Text = job.ResultText;
        var material = job.ResultMaterialId.HasValue ? _project.Materials.FirstOrDefault(m => m.MaterialId == job.ResultMaterialId.Value) : null;
        if (material is not null) Report($"Risultato file collegato: {material.FileName} · {material.Summary}");
    }

    private async Task SaveAsync(string message)
    {
        await ProjectFileStore.SaveAsync(_projectPath, _project);
        Report(message);
    }

    private void Report(string message)
    {
        _status.Text = message;
        _mainStatus(message);
    }

    private void AddHelp(Control control, string text)
    {
        ToolTip.SetTip(control, text);
        control.GotFocus += (_, _) => _status.Text = text;
        control.PointerEntered += (_, _) => _status.Text = text;
    }

    private static Button MakeButton(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };
}

internal sealed record AiJobDraft(string OutputType, string Title, string Request, Guid? TargetContentId);

internal sealed class AiJobEditorWindow : Window
{
    private readonly ComboBox _type;
    private readonly TextBox _title;
    private readonly TextBox _request;
    private readonly ComboBox _target;
    private readonly List<ContentChoice> _targets;

    public AiJobEditorWindow(PreviewProject project)
    {
        Title = "Nuovo lavoro AI";
        Width = 650;
        Height = 500;
        MinWidth = 560;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _type = new ComboBox
        {
            ItemsSource = new[] { "Immagine", "Testo", "Dati / tabella" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _title = new TextBox { Watermark = "Es. Jukebox anni '50" };
        _request = new TextBox { AcceptsReturn = true, Height = 150, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Watermark = "Descrivi cosa deve produrre l'AI per questo singolo elemento." };
        _targets = [new ContentChoice(null, "Nessun collegamento al Testo di lavoro")];
        _targets.AddRange(project.ContentNodes
            .Where(n => EditableMasterService.CanEdit(project, n))
            .OrderBy(n => n.Ordinal)
            .Select(n => new ContentChoice(n.ContentId, string.IsNullOrWhiteSpace(n.Title) ? n.Kind : n.Title)));
        _target = new ComboBox { ItemsSource = _targets, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };

        var cancel = new Button { Content = "Annulla", Width = 100 };
        cancel.Click += (_, _) => Close(null);
        var create = new Button { Content = "Crea job e prompt", Width = 160 };
        create.Click += (_, _) =>
        {
            var output = _type.SelectedIndex switch
            {
                0 => AiProductionService.TypeImage,
                2 => AiProductionService.TypeData,
                _ => AiProductionService.TypeText
            };
            var target = _target.SelectedItem as ContentChoice;
            Close(new AiJobDraft(output, (_title.Text ?? string.Empty).Trim(), (_request.Text ?? string.Empty).Trim(), target?.ContentId));
        };

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 9,
                Children =
                {
                    new TextBlock { Text = "Che cosa deve produrre l'AI?", FontSize = 21 },
                    Field("Tipo di risultato", _type),
                    Field("Titolo / soggetto", _title),
                    Field("Richiesta specifica", _request),
                    Field("Collega a un capitolo/sezione (utile per il testo)", _target),
                    new TextBlock { Text = "Il prompt verrà costruito unendo questa richiesta al brief generale del progetto.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancel, create }
                    }
                }
            }
        };
    }

    private static StackPanel Field(string label, Control input) => new()
    {
        Spacing = 3,
        Children = { new TextBlock { Text = label }, input }
    };
}

internal sealed record ContentChoice(Guid? ContentId, string Label)
{
    public override string ToString() => Label;
}
