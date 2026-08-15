using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

/// <summary>
/// One physical MainWindow. Home controls stay in their original tree while the logical workflow surface is
/// constructed detached and receives its first visual parent from StableWorkflowRootUi. Navigation then only
/// changes active surface state; the initialized workflow subtree is never reparented at runtime.
/// </summary>
internal static class SingleWindowOverlayFlowUi
{
    private static readonly Dictionary<MainWindow, SingleWindowOverlayFlowHost> Hosts = [];

    public static void Attach(MainWindow window)
    {
        if (Hosts.ContainsKey(window)) return;
        if (!TryDesktop(window, out var desktop, out var commandRow)) return;
        var host = new SingleWindowOverlayFlowHost(window, desktop, commandRow);
        Hosts[window] = host;
        window.Closed += (_, _) => Hosts.Remove(window);
    }

    private static bool TryDesktop(MainWindow window, out Grid desktop, out StackPanel commandRow)
    {
        desktop = null!;
        commandRow = null!;
        if (window.Content is not Border border || border.Child is not Grid grid) return false;
        var header = grid.Children.OfType<Grid>().FirstOrDefault(c => Grid.GetRow(c) == 0);
        if (header is null) return false;
        var row = header.Children.OfType<StackPanel>().FirstOrDefault(p =>
            p.Orientation == Orientation.Horizontal &&
            p.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.OrdinalIgnoreCase)));
        if (row is null) return false;
        desktop = grid;
        commandRow = row;
        return true;
    }
}

internal sealed class SingleWindowOverlayFlowHost
{
    private const string BuildMarker = "SW-FLOW-2";
    private readonly MainWindow _window;
    private readonly Grid _desktop;
    private readonly List<Control> _homeControls;
    private readonly Grid _overlay;
    private readonly StableWorkflowPageHost _pageHost = new();
    private readonly ContentControl _previewHost = new();
    private readonly TextBlock _title = new() { FontSize = 23, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button _back;
    private readonly List<PageState> _history = [];
    private readonly ColoringState _coloring = new();
    private Bitmap? _bitmap;

    public SingleWindowOverlayFlowHost(MainWindow window, Grid desktop, StackPanel commandRow)
    {
        _window = window;
        _desktop = desktop;
        _homeControls = desktop.Children.OfType<Control>().ToList();
        _back = MakeButton("← Indietro", 120);
        var home = MakeButton("Home progetto", 135);
        _back.Click += (_, _) => Back();
        home.Click += (_, _) => ShowHome();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = 8,
            Children =
            {
                _back,
                home.WithGridColumn(1),
                _title.WithGridColumn(2),
                new TextBlock
                {
                    Text = $"Diez single-window · {BuildMarker}",
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                }.WithGridColumn(3)
            }
        };

        var preview = new Border
        {
            Padding = new Thickness(12),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                RowSpacing = 8,
                Children =
                {
                    new TextBlock { Text = "Anteprima", FontSize = 19 },
                    _previewHost.WithGridRow(1)
                }
            }
        };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3*,2*"),
            ColumnSpacing = 12,
            Children =
            {
                new Border { Padding = new Thickness(6), Child = _pageHost },
                preview.WithGridColumn(1)
            }
        };

        _overlay = new Grid
        {
            IsVisible = false,
            Margin = new Thickness(14, 10),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8,
            Children = { header, body.WithGridRow(1), _status.WithGridRow(2) }
        };
        Grid.SetRow(_overlay, 0);
        Grid.SetRowSpan(_overlay, Math.Max(1, _desktop.RowDefinitions.Count));
        SafeStartupTrace.Write("workflow-overlay-created | initialParent=<null> | first-parent=stable-root");

        foreach (var old in commandRow.Children.OfType<Button>())
        {
            var text = old.Content?.ToString() ?? string.Empty;
            if (text.Contains("Produzione AI", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Contenuti con AI", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Prompt Pack AI", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Serie immagini AI", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Correzione AI", StringComparison.OrdinalIgnoreCase))
                old.IsVisible = false;
        }

        var start = MakeButton("Percorso libro", 150);
        ToolTip.SetTip(start, "Schermate logiche nello stesso MainWindow, con Indietro e anteprima laterale.");
        start.Click += (_, _) => OpenCurrentBook();
        commandRow.Children.Add(start);
    }

    private void OpenCurrentBook()
    {
        if (!TrySession(out var project, out _))
        {
            SetMainStatus("Prima crea o apri un progetto .diez.");
            return;
        }

        var type = BookTypeProfileService.Get(project);
        if (string.IsNullOrWhiteSpace(type) || string.Equals(type, BookTypeProfileService.Other, StringComparison.OrdinalIgnoreCase))
        {
            OpenBookTypeChoice();
            return;
        }
        if (BookTypeProfileService.IsImageCollection(project))
        {
            OpenQuantity();
            return;
        }

        Push("Percorso libro", new StackPanel
        {
            Spacing = 11,
            Children =
            {
                new TextBlock { Text = $"Tipo libro: {type}", FontSize = 22 },
                new TextBlock
                {
                    Text = "La struttura a finestra unica è attiva. Il primo percorso completo disponibile è Coloring Book / Raccolta immagini; gli altri Tipi libro verranno inseriti nello stesso host.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                ChangeTypeButton()
            }
        }, Placeholder("Anteprima disponibile per le schermate specifiche del Tipo libro."), "Single-window attivo.");
    }

    private void OpenBookTypeChoice()
    {
        if (!TrySession(out var project, out var path)) return;
        var choices = new[]
        {
            BookTypeProfileService.ColoringBook,
            BookTypeProfileService.ImageCollection,
            BookTypeProfileService.WordSearch,
            BookTypeProfileService.Crossword,
            BookTypeProfileService.Quiz,
            BookTypeProfileService.Novel,
            BookTypeProfileService.IllustratedBook,
            BookTypeProfileService.DataCollection,
            BookTypeProfileService.Other
        };
        var combo = new ComboBox { ItemsSource = choices, SelectedIndex = 0, Width = 330 };
        var apply = MakeButton("Usa questo Tipo libro", 180);
        apply.Click += async (_, _) =>
        {
            var chosen = combo.SelectedItem?.ToString() ?? BookTypeProfileService.Other;
            BookTypeProfileService.Set(project, chosen);
            await ProjectFileStore.SaveAsync(path, project);
            _history.Clear();
            if (BookTypeProfileService.IsImageCollection(project)) OpenQuantity();
            else OpenCurrentBook();
        };
        Push("Tipo libro", new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Quale libro stai preparando?", FontSize = 24 },
                combo,
                apply
            }
        }, Placeholder("Il riquadro anteprima si adatta al Tipo libro scelto."), "La scelta viene salvata nel progetto .diez.");
    }

    private Button ChangeTypeButton()
    {
        var button = MakeButton("Cambia Tipo libro", 160);
        button.Click += (_, _) => OpenBookTypeChoice();
        return button;
    }

    private void OpenQuantity()
    {
        if (!TrySession(out var project, out _)) return;
        var existing = ImageJobs(project).Count;
        if (string.IsNullOrWhiteSpace(_coloring.Count)) _coloring.Count = Math.Max(1, existing).ToString();
        if (string.IsNullOrWhiteSpace(_coloring.Rules)) _coloring.Rules = ImageCollectionWorkspaceService.GetConsistencyRules(project);
        _coloring.Consistent = !string.IsNullOrWhiteSpace(_coloring.Rules);

        var count = Editor(_coloring.Count, 44, false);
        count.Width = 110;
        count.HorizontalAlignment = HorizontalAlignment.Left;
        var consistent = new CheckBox { Content = "Consistent — mantieni coerenti le immagini", IsChecked = _coloring.Consistent };
        var rules = Editor(_coloring.Rules, 120);
        rules.Watermark = "Es. stesso personaggio, stesso stile e tratto; ambientazioni libere.";
        rules.IsEnabled = consistent.IsChecked == true;
        consistent.IsCheckedChanged += (_, _) => { _coloring.Consistent = consistent.IsChecked == true; rules.IsEnabled = _coloring.Consistent; };
        count.TextChanged += (_, _) => _coloring.Count = count.Text ?? string.Empty;
        rules.TextChanged += (_, _) => _coloring.Rules = rules.Text ?? string.Empty;

        var next = MakeButton("Avanti → istruzioni", 175);
        next.Click += (_, _) =>
        {
            if (!TryCount(count.Text, out var n)) { Report("Inserisci il numero preciso di immagini, da 1 a 500."); count.Focus(); return; }
            _coloring.Count = n.ToString();
            OpenPrompt(n);
        };
        var changeType = ChangeTypeButton();

        Push("Coloring Book · 1/4 Quantità", new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 11,
                Children =
                {
                    new TextBlock { Text = "Coloring Book — quantità e coerenza", FontSize = 24 },
                    new TextBlock
                    {
                        Text = "Indica il numero esatto di immagini. In questo Tipo libro non compaiono scelte Testo o Lista/Tabella.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    Label("Quante immagini vuoi creare?"), count,
                    consistent, rules,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { next, changeType } }
                }
            }
        }, CollectionPreview(existing), existing == 0 ? "Nessuna immagine ancora preparata." : $"Immagini già presenti: {existing}.");
    }

    private void OpenPrompt(int count)
    {
        if (!TrySession(out var project, out _)) return;
        var mustDo = Editor(_coloring.MustDo, 125);
        mustDo.Watermark = "Cosa devono rappresentare e come devono essere le immagini.";
        var mustNotDo = Editor(_coloring.MustNotDo, 110);
        mustNotDo.Watermark = "Cosa deve essere evitato. Puoi lasciare vuoto.";
        var prompt = Editor(_coloring.Prompt, 235);
        prompt.Watermark = "Premi Prepara prompt, poi modificalo liberamente se vuoi.";
        mustDo.TextChanged += (_, _) => _coloring.MustDo = mustDo.Text ?? string.Empty;
        mustNotDo.TextChanged += (_, _) => _coloring.MustNotDo = mustNotDo.Text ?? string.Empty;
        prompt.TextChanged += (_, _) => _coloring.Prompt = prompt.Text ?? string.Empty;

        void Prepare()
        {
            _coloring.MustDo = mustDo.Text ?? string.Empty;
            _coloring.MustNotDo = mustNotDo.Text ?? string.Empty;
            _coloring.Prompt = BuildPrompt(project, count);
            prompt.Text = _coloring.Prompt;
            Report("Prompt preparato. I tre box sono editabili, copiabili e supportano Ctrl+Z.");
        }

        var prepare = MakeButton("Prepara prompt", 155);
        var copy = MakeButton("Copia prompt", 145);
        var next = MakeButton("Avanti → Prompt Pack", 190);
        prepare.Click += (_, _) => Prepare();
        copy.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(prompt.Text)) Prepare();
            var clipboard = TopLevel.GetTopLevel(_window)?.Clipboard;
            if (clipboard is null) { Report("Appunti di Windows non disponibili."); return; }
            await clipboard.SetTextAsync(prompt.Text ?? string.Empty);
            Report("Prompt copiato esattamente dal box modificabile.");
        };
        next.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(mustDo.Text)) { Report("Compila DEVE FARE prima di continuare."); mustDo.Focus(); return; }
            if (string.IsNullOrWhiteSpace(prompt.Text)) Prepare();
            if (await EnsureSeriesAsync(count)) OpenPromptPack();
        };

        Push($"Coloring Book · 2/4 Istruzioni · {count} immagini", new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 9,
                Children =
                {
                    Label("DEVE FARE"), mustDo,
                    Label("NON DEVE FARE"), mustNotDo,
                    Label("PROMPT — modificabile"), prompt,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { prepare, copy, next } }
                }
            }
        }, CollectionPreview(ImageJobs(project).Count), "DEVE FARE, NON DEVE FARE e PROMPT sono editor nativi con undo abilitato.");
    }

    private async Task<bool> EnsureSeriesAsync(int desired)
    {
        if (!TrySession(out var project, out var path)) return false;
        var images = ImageJobs(project);
        if (images.Count > desired)
        {
            Report($"Il progetto contiene già {images.Count} immagini. Non elimino automaticamente le eccedenti: indica almeno {images.Count}.");
            return false;
        }
        var request = HumanAiPromptService.Write(_coloring.MustDo, _coloring.MustNotDo);
        if (images.Count < desired) AiImageBatchService.CreateImageSeries(project, desired - images.Count, request, "Tavola");
        images = ImageJobs(project);
        for (var i = 0; i < desired; i++)
        {
            var job = images[i];
            job.Request = request;
            job.Prompt = new StringBuilder()
                .AppendLine(_coloring.Prompt.Trim()).AppendLine()
                .AppendLine($"ELEMENTO DIEZ: {job.Code}")
                .AppendLine($"Questa è l'immagine {i + 1} di {desired}.")
                .AppendLine("Genera un risultato distinto senza cambiare le regole comuni.")
                .ToString().Trim();
        }
        ImageCollectionWorkspaceService.SetConsistencyRules(project, _coloring.Consistent ? _coloring.Rules.Trim() : string.Empty);
        var state = AiExchangeStateStore.Load(project);
        AiExchangeStateStore.EnsureVisualConsistencyContext(project, state, _coloring.Consistent, _coloring.Rules);
        foreach (var unit in state.WorkUnits.Where(u => u.LegacyAiJobId.HasValue))
        {
            var legacy = project.AiProductionJobs.FirstOrDefault(j => j.JobId == unit.LegacyAiJobId!.Value);
            if (legacy is not null) unit.Instruction = legacy.Prompt;
        }
        AiExchangeStateStore.Save(project, state);
        await ProjectFileStore.SaveAsync(path, project);
        return true;
    }

    private void OpenPromptPack()
    {
        if (!TrySession(out var project, out var path)) return;
        var state = AiExchangeStateStore.Load(project);
        var units = state.WorkUnits.Where(u => string.Equals(u.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            .OrderBy(u => u.Position).ThenBy(u => u.Code).ToList();
        var choices = AiExchangeModes.All.Select(m => new ModeChoice(m, AiExchangeModes.UserLabel(m))).ToList();
        var mode = new ComboBox { ItemsSource = choices, SelectedIndex = 1, Width = 480 };
        var roles = Editor("character, style", 44, false);
        roles.Watermark = "Ruoli paradigma: personaggio, stile, palette...";
        var applyMode = MakeButton("Applica modalità", 155);
        var addParadigm = MakeButton("Aggiungi paradigma", 170);
        var export = MakeButton("Crea Prompt Pack ZIP", 190);
        var import = MakeButton("Importa risultati AI", 180);
        var review = MakeButton("Controlla risultati", 175);

        applyMode.Click += async (_, _) =>
        {
            if (mode.SelectedItem is not ModeChoice choice) return;
            foreach (var unit in units) unit.Mode = choice.Mode;
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(path, project);
            Report($"Modalità '{choice.Label}' applicata alle {units.Count} immagini.");
        };
        addParadigm.Click += async (_, _) =>
        {
            var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Scegli immagini paradigma", AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Immagini") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"] }]
            });
            if (files.Count == 0) return;
            var roleList = (roles.Text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (roleList.Count == 0) roleList.Add("reference");
            foreach (var file in files)
            {
                var material = await MaterialImporter.ImportAsync(file.Path.LocalPath);
                var stored = project.Materials.FirstOrDefault(m => string.Equals(m.Sha256, material.Sha256, StringComparison.OrdinalIgnoreCase));
                if (stored is null) { project.Materials.Add(material); stored = material; }
                var paradigm = new AiExchangeParadigm { MaterialId = stored.MaterialId, Scope = "COLLECTION", Roles = roleList.ToList(), Description = string.Join(", ", roleList) };
                state.Paradigms.Add(paradigm);
                foreach (var unit in units) if (!unit.ParadigmIds.Contains(paradigm.ParadigmId)) unit.ParadigmIds.Add(paradigm.ParadigmId);
                await PreviewMaterialAsync(project, path, stored.MaterialId, $"Paradigma · {paradigm.Description}");
            }
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(path, project);
            Report($"Paradigmi aggiunti. Totale: {state.Paradigms.Count}.");
        };
        export.Click += async (_, _) =>
        {
            var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva Prompt Pack Diez", SuggestedFileName = "diez-prompt-pack.zip", DefaultExtension = "zip",
                FileTypeChoices = [new FilePickerFileType("Prompt Pack Diez") { Patterns = ["*.zip"] }]
            });
            if (file is null) return;
            var result = await AiExchangePromptPackBuilder.BuildAsync(project, path, state, units.Select(u => u.WorkUnitId), file.Path.LocalPath);
            Report(result.Message);
        };
        import.Click += async (_, _) =>
        {
            var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importa uno o più ZIP restituiti dall'AI", AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Risultati AI Diez") { Patterns = ["*.zip"] }]
            });
            if (files.Count == 0) return;
            var result = await AiExchangeResponseImporter.ImportAsync(project, path, state, files.Select(f => f.Path.LocalPath));
            Report(result.Message);
            OpenReview();
        };
        review.Click += (_, _) => OpenReview();

        Push("Coloring Book · 3/4 Prompt Pack", new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = $"{units.Count} immagini / Work Unit", FontSize = 21 },
                    Label("Come usare input e AI"), mode, applyMode,
                    new Separator(), Label("Immagini paradigma"), roles, addParadigm,
                    new Separator(),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { export, import, review } },
                    new TextBlock { Text = "I dialoghi di sistema servono solo a scegliere/salvare file; tutte le schermate Diez restano in questo MainWindow.", TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                }
            }
        }, CollectionPreview(units.Count), "Esporta il Prompt Pack e importa uno o più ZIP, anche parziali.");
    }

    private void OpenReview()
    {
        if (!TrySession(out var project, out var path)) return;
        var state = AiExchangeStateStore.Load(project);
        var list = new ListBox { MinHeight = 350 };
        var description = Editor(string.Empty, 145);
        var info = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var save = MakeButton("Salva descrizione", 155);
        var approve = MakeButton("Approva", 115);
        var rows = state.WorkUnits.Where(u => string.Equals(u.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            .OrderBy(u => u.Position).ThenBy(u => u.Code).Select(u => new ReviewChoice(u, ReviewState(state, u))).ToList();
        list.ItemsSource = rows;

        AiExchangeWorkUnit? Unit() => (list.SelectedItem as ReviewChoice)?.Unit;
        AiExchangeVersion? Version()
        {
            var unit = Unit();
            return unit is null ? null : state.Versions.Where(v => v.WorkUnitId == unit.WorkUnitId && v.Status != AiExchangeVersionStatuses.Rejected)
                .OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        }
        async Task Load()
        {
            var unit = Unit(); var version = Version();
            if (unit is null || version is null) { description.Text = string.Empty; info.Text = "Risultato non ancora ricevuto."; SetPreview(Placeholder("Risultato non ancora disponibile.")); return; }
            description.Text = version.Description;
            info.Text = $"{unit.Code} · v{version.VersionNumber} · {version.Status} · descrizione {version.DescriptionStatus}";
            if (version.MaterialId is Guid materialId) await PreviewMaterialAsync(project, path, materialId, $"{unit.Code} · v{version.VersionNumber}");
            else SetPreview(Placeholder("Questa versione non contiene ancora un'immagine."));
        }
        list.SelectionChanged += async (_, _) => await Load();
        save.Click += async (_, _) =>
        {
            var version = Version(); if (version is null) return;
            version.Description = (description.Text ?? string.Empty).Trim();
            version.DescriptionStatus = string.IsNullOrWhiteSpace(version.Description) ? AiExchangeDescriptionStatuses.Missing : AiExchangeDescriptionStatuses.Valid;
            if (version.MaterialId.HasValue && version.DescriptionStatus == AiExchangeDescriptionStatuses.Valid && version.Status == AiExchangeVersionStatuses.Incomplete) version.Status = AiExchangeVersionStatuses.Candidate;
            AiExchangeStateStore.Save(project, state); await ProjectFileStore.SaveAsync(path, project); info.Text = "Descrizione salvata.";
        };
        approve.Click += async (_, _) =>
        {
            var version = Version(); if (version is null) return;
            if (!AiExchangeResultIngestor.Approve(project, state, version.VersionId, out var message)) { info.Text = message; return; }
            AiExchangeStateStore.Save(project, state); await ProjectFileStore.SaveAsync(path, project); info.Text = message;
        };

        Push("Coloring Book · 4/4 Revisione", new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"), RowSpacing = 8,
            Children =
            {
                new TextBlock { Text = "Seleziona un'immagine: l'anteprima appare a destra.", FontSize = 20 },
                list.WithGridRow(1), info.WithGridRow(2),
                new StackPanel { Spacing = 4, Children = { Label("Descrizione associata"), description } }.WithGridRow(3),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, approve } }.WithGridRow(4)
            }
        }, Placeholder("Seleziona un risultato per visualizzarlo."), "La Candidate resta separata dall'approvata fino alla tua decisione.");
        if (rows.Count > 0) list.SelectedIndex = 0;
    }

    private async Task PreviewMaterialAsync(PreviewProject project, string path, Guid materialId, string caption)
    {
        var material = project.Materials.FirstOrDefault(m => m.MaterialId == materialId);
        if (material is null) { SetPreview(Placeholder("Materiale non trovato.")); return; }
        var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(path, material);
        if (bytes is null || bytes.Length == 0) { SetPreview(Placeholder("Anteprima non disponibile.")); return; }
        try
        {
            _bitmap?.Dispose();
            using var memory = new MemoryStream(bytes);
            _bitmap = new Bitmap(memory);
            SetPreview(new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 6,
                Children =
                {
                    new Image { Source = _bitmap, Stretch = Avalonia.Media.Stretch.Uniform },
                    new TextBlock { Text = caption, TextWrapping = Avalonia.Media.TextWrapping.Wrap }.WithGridRow(1)
                }
            });
        }
        catch { SetPreview(new TextBox { Text = material.Preview, IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap }); }
    }

    private string BuildPrompt(PreviewProject project, int count)
    {
        var sb = new StringBuilder();
        var common = (project.AiProduction?.ProjectBrief ?? string.Empty).Trim();
        if (common.Length > 0) sb.AppendLine("REGOLE COMUNI DEL PROGETTO:").AppendLine(common).AppendLine();
        sb.AppendLine($"Crea {count} {(count == 1 ? "immagine" : "immagini")} per un Coloring Book.").AppendLine();
        sb.AppendLine("DEVE FARE:").AppendLine(_coloring.MustDo.Trim()).AppendLine();
        sb.AppendLine("NON DEVE FARE:").AppendLine(_coloring.MustNotDo.Trim());
        if (_coloring.Consistent) sb.AppendLine().AppendLine("CONSISTENT:").AppendLine(string.IsNullOrWhiteSpace(_coloring.Rules) ? "Mantieni coerenti personaggi, stile e tratto salvo eccezioni esplicite." : _coloring.Rules.Trim());
        sb.AppendLine().AppendLine("Ogni immagine deve essere distinta e non deve contenere ID, numeri o nomi file dentro l'immagine.");
        return sb.ToString().Trim();
    }

    private void Push(string title, Control content, Control preview, string status)
    {
        var page = new PageState(title, content, preview, status);
        _history.Add(page);
        Show(page);
    }

    private void Show(PageState page)
    {
        foreach (var control in _homeControls) control.IsVisible = false;
        _overlay.IsVisible = true;
        _title.Text = page.Title;
        _pageHost.Show(page.Content);
        SetPreview(page.Preview);
        _status.Text = page.Status;
        _back.IsEnabled = _history.Count > 1;
    }

    private void Back()
    {
        if (_history.Count <= 1) { ShowHome(); return; }
        _history.RemoveAt(_history.Count - 1);
        Show(_history[^1]);
    }

    private void ShowHome()
    {
        _history.Clear();
        _pageHost.Show(null);
        _previewHost.Content = null;
        _overlay.IsVisible = false;
        foreach (var control in _homeControls) control.IsVisible = true;
        SetMainStatus("Tornato alla schermata principale del progetto.");
    }

    private void SetPreview(Control control) => _previewHost.Content = control;
    private void Report(string text) { _status.Text = text; SetMainStatus(text); }

    private bool TrySession(out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(_window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(_window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private void SetMainStatus(string text)
    {
        var block = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_window) as TextBlock;
        if (block is not null) block.Text = text;
    }

    private Control CollectionPreview(int count) => new StackPanel
    {
        Spacing = 10,
        Children =
        {
            new TextBlock { Text = "Coloring Book", FontSize = 22 },
            new TextBlock { Text = $"Posizioni immagine: {count}" },
            new TextBlock { Text = _coloring.Consistent ? "Consistent: ON" : "Consistent: OFF" },
            new TextBlock { Text = "Paradigmi e risultati selezionati compariranno qui senza aprire un'altra finestra Diez.", TextWrapping = Avalonia.Media.TextWrapping.Wrap }
        }
    };

    private static List<AiProductionJob> ImageJobs(PreviewProject project) => project.AiProductionJobs
        .Where(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase))
        .OrderBy(j => j.Code, StringComparer.OrdinalIgnoreCase).ToList();
    private static bool TryCount(string? text, out int count) => int.TryParse((text ?? string.Empty).Trim(), out count) && count is >= 1 and <= 500;
    private static string ReviewState(AiExchangeState state, AiExchangeWorkUnit unit)
    {
        var version = state.Versions.Where(v => v.WorkUnitId == unit.WorkUnitId).OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        if (version is null) return "mancante";
        return version.Status switch { AiExchangeVersionStatuses.Approved => "✓ approvata", AiExchangeVersionStatuses.Incomplete => "⚠ incompleta", AiExchangeVersionStatuses.Stale => "⚠ da verificare", _ => "● nuova proposta" };
    }

    private static Control Placeholder(string text) => new Border { Padding = new Thickness(18), Child = new TextBlock { Text = text, FontSize = 17, TextWrapping = Avalonia.Media.TextWrapping.Wrap } };
    private static TextBox Editor(string text, double height, bool acceptsReturn = true) => new()
    {
        Text = text, Height = height, AcceptsReturn = acceptsReturn, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        IsReadOnly = false, IsEnabled = true, IsHitTestVisible = true, Focusable = true, IsUndoEnabled = true
    };
    private static TextBlock Label(string text) => new() { Text = text, FontSize = 16 };
    private static Button MakeButton(string text, double width) => new() { Content = text, Width = width, HorizontalContentAlignment = HorizontalAlignment.Center };

    private sealed class StableWorkflowPageHost : ContentControl
    {
        private readonly Grid _stack = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true
        };
        private Control? _activePage;

        public StableWorkflowPageHost()
        {
            Content = _stack;
        }

        public void Show(Control? page)
        {
            if (ReferenceEquals(_activePage, page))
            {
                RaisePropertyChanged(ContentProperty, _stack, _stack);
                return;
            }

            if (_activePage is not null && _stack.Children.Contains(_activePage))
                _stack.Children.Remove(_activePage);

            _activePage = page;
            if (page is not null)
            {
                page.IsVisible = true;
                page.IsEnabled = true;
                page.IsHitTestVisible = true;
                page.HorizontalAlignment = HorizontalAlignment.Stretch;
                page.VerticalAlignment = VerticalAlignment.Stretch;
                Panel.SetZIndex(page, 1);
                _stack.Children.Add(page);
            }

            _stack.InvalidateMeasure();
            _stack.InvalidateArrange();
            _stack.InvalidateVisual();
            if (page is not null)
            {
                page.InvalidateMeasure();
                page.InvalidateArrange();
                page.InvalidateVisual();
            }

            // Keep the ContentPresenter child stable for the lifetime of the window while preserving the existing
            // ContentProperty notification contract used by layout/title/quantity observers.
            RaisePropertyChanged(ContentProperty, _stack, _stack);
            SafeStartupTrace.Write(
                "workflow-page-stack | contentPresenterChild=stable" +
                " | active=" + (page?.GetType().Name ?? "<none>") +
                " | children=" + _stack.Children.Count +
                " | contentSame=True");
        }
    }

    private sealed class ColoringState
    {
        public string Count { get; set; } = string.Empty;
        public bool Consistent { get; set; }
        public string Rules { get; set; } = string.Empty;
        public string MustDo { get; set; } = string.Empty;
        public string MustNotDo { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
    }
    private sealed record PageState(string Title, Control Content, Control Preview, string Status);
    private sealed record ModeChoice(string Mode, string Label) { public override string ToString() => Label; }
    private sealed record ReviewChoice(AiExchangeWorkUnit Unit, string State) { public override string ToString() => $"{Unit.Code} · {State}"; }
}
