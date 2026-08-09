using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

/// <summary>
/// Single physical MainWindow host. Book workflow screens are logical views:
/// only the current view is visible, Back restores the previous one and Home
/// returns to the original project workspace. A stable preview pane is available
/// on all book-flow screens and becomes an image preview during review.
/// </summary>
internal static class SingleWindowBookFlowUi
{
    private static readonly Dictionary<MainWindow, SingleWindowBookFlowHost> Hosts = [];

    public static void Attach(MainWindow window)
    {
        if (Hosts.ContainsKey(window) || window.Content is not Control original) return;
        window.Content = null;
        var host = new SingleWindowBookFlowHost(window, original);
        Hosts[window] = host;
        window.Content = host;
        window.Closed += (_, _) => Hosts.Remove(window);
        host.InstallEntryPoint();
    }
}

internal sealed class SingleWindowBookFlowHost : Grid
{
    private const string BuildMarker = "SW-FLOW-1";
    private readonly MainWindow _window;
    private readonly Control _mainView;
    private readonly Grid _logicalShell;
    private readonly ContentControl _logicalContent;
    private readonly ContentControl _previewContent;
    private readonly Border _previewPane;
    private readonly TextBlock _screenTitle;
    private readonly TextBlock _screenStatus;
    private readonly Button _back;
    private readonly Button _home;
    private readonly List<LogicalPage> _history = [];
    private readonly ColoringFlowState _coloring = new();
    private Bitmap? _previewBitmap;

    public SingleWindowBookFlowHost(MainWindow window, Control mainView)
    {
        _window = window;
        _mainView = mainView;

        _screenTitle = new TextBlock { FontSize = 23, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _screenStatus = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _back = Button("← Indietro", 120);
        _home = Button("Home progetto", 135);
        _back.Click += (_, _) => Back();
        _home.Click += (_, _) => ShowMain();

        var build = new TextBlock
        {
            Text = $"Diez single-window · {BuildMarker}",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        var nav = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = 8,
            Children =
            {
                _back,
                _home.WithGridColumn(1),
                _screenTitle.WithGridColumn(2),
                build.WithGridColumn(3)
            }
        };

        _logicalContent = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        _previewContent = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        _previewPane = new Border
        {
            Padding = new Thickness(12),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                RowSpacing = 8,
                Children =
                {
                    new TextBlock { Text = "Anteprima", FontSize = 19 },
                    _previewContent.WithGridRow(1)
                }
            }
        };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3*,2*"),
            ColumnSpacing = 12,
            Children =
            {
                new Border { Padding = new Thickness(6, 8), Child = _logicalContent },
                _previewPane.WithGridColumn(1)
            }
        };

        _logicalShell = new Grid
        {
            IsVisible = false,
            Margin = new Thickness(14, 10),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8,
            Children =
            {
                nav,
                body.WithGridRow(1),
                _screenStatus.WithGridRow(2)
            }
        };

        Children.Add(_mainView);
        Children.Add(_logicalShell);
    }

    public void InstallEntryPoint()
    {
        var row = Descendants(_mainView).OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Button>().Any(b =>
                                         string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.OrdinalIgnoreCase)));
        if (row is null) return;

        // Hide the old popup-oriented entry points. They remain in code for
        // compatibility, but the normal book workflow now has one physical window.
        var popupNames = new[]
        {
            "Contenuti con AI", "Produzione AI", "Prompt Pack AI", "Serie immagini AI", "Correzione AI"
        };
        foreach (var button in row.Children.OfType<Button>())
        {
            var text = button.Content?.ToString() ?? string.Empty;
            if (popupNames.Any(name => text.Contains(name, StringComparison.OrdinalIgnoreCase)))
                button.IsVisible = false;
        }

        if (row.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Percorso libro", StringComparison.Ordinal))) return;
        var start = Button("Percorso libro", 150);
        ToolTip.SetTip(start, "Continua il lavoro nel libro usando una sola finestra: avanti, indietro e anteprima.");
        start.Click += (_, _) => OpenForCurrentProject();
        row.Children.Add(start);
    }

    private void OpenForCurrentProject()
    {
        if (!TrySession(out var project, out _))
        {
            SetMainStatus("Prima crea o apri un progetto .diez.");
            return;
        }

        var type = BookTypeProfileService.Get(project);
        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase) ||
            BookTypeProfileService.IsImageCollection(project))
        {
            OpenColoringQuantity();
            return;
        }

        Push(new LogicalPage(
            "Percorso del libro",
            new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = $"Tipo libro: {type}", FontSize = 21 },
                    new TextBlock
                    {
                        Text = "Il nuovo host a finestra unica è attivo. Il vertical slice completo è disponibile ora per Coloring Book e Raccolta immagini; gli altri Tipi libro verranno portati nello stesso host senza cambiare il modello dati.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                }
            },
            PlaceholderPreview("Anteprima disponibile nelle schermate specifiche del Tipo libro."),
            "Host a finestra unica attivo."));
    }

    private void OpenColoringQuantity()
    {
        if (!TrySession(out var project, out _)) return;
        var existingImages = project.AiProductionJobs.Count(j =>
            string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(_coloring.CountText))
            _coloring.CountText = Math.Max(1, existingImages).ToString();
        if (string.IsNullOrWhiteSpace(_coloring.ConsistencyRules))
            _coloring.ConsistencyRules = ImageCollectionWorkspaceService.GetConsistencyRules(project);
        _coloring.Consistent = !string.IsNullOrWhiteSpace(_coloring.ConsistencyRules);

        var count = Editor(_coloring.CountText, 44, acceptsReturn: false);
        count.Width = 110;
        count.HorizontalAlignment = HorizontalAlignment.Left;
        var consistent = new CheckBox
        {
            Content = "Consistent — mantieni coerenti le immagini",
            IsChecked = _coloring.Consistent
        };
        var rules = Editor(_coloring.ConsistencyRules, 120);
        rules.Watermark = "Es. stesso personaggio, stesso stile e tratto; ambientazioni libere.";
        rules.IsEnabled = consistent.IsChecked == true;
        consistent.IsCheckedChanged += (_, _) => rules.IsEnabled = consistent.IsChecked == true;
        count.TextChanged += (_, _) => _coloring.CountText = count.Text ?? string.Empty;
        rules.TextChanged += (_, _) => _coloring.ConsistencyRules = rules.Text ?? string.Empty;
        consistent.IsCheckedChanged += (_, _) => _coloring.Consistent = consistent.IsChecked == true;

        var next = Button("Avanti → istruzioni", 175);
        next.Click += (_, _) =>
        {
            if (!ColoringAiCreationUi.TryCount(count.Text, out var n, out var error))
            {
                Report(error);
                count.Focus();
                return;
            }
            _coloring.CountText = n.ToString();
            _coloring.Consistent = consistent.IsChecked == true;
            _coloring.ConsistencyRules = rules.Text ?? string.Empty;
            OpenColoringPrompt(n);
        };

        var page = new ScrollViewer
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
                        Text = "Indica il numero esatto di immagini finali da preparare. Non ci sono scelte Testo o Lista/Tabella perché questo Tipo libro lavora sulle immagini.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    Label("Quante immagini vuoi creare?"),
                    count,
                    consistent,
                    rules,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { next } }
                }
            }
        };

        Push(new LogicalPage(
            "Coloring Book · 1/4 Quantità",
            page,
            BuildCollectionPreview(project, existingImages),
            existingImages > 0
                ? $"Il progetto contiene già {existingImages} immagini. Il numero indicato è il totale desiderato; Diez aggiungerà soltanto quelle mancanti."
                : "Nessuna immagine ancora preparata."));
    }

    private void OpenColoringPrompt(int count)
    {
        if (!TrySession(out var project, out _)) return;
        var mustDo = Editor(_coloring.MustDo, 125);
        mustDo.Watermark = "Es. Line art pulita, soggetti distinti, sfondo bianco, nessun testo.";
        var mustNotDo = Editor(_coloring.MustNotDo, 110);
        mustNotDo.Watermark = "Es. Niente ombreggiature, grigi, cornici o dettagli troppo fitti.";
        var prompt = Editor(_coloring.Prompt, 235);
        prompt.Watermark = "Premi Prepara prompt: la bozza resta completamente modificabile.";

        mustDo.TextChanged += (_, _) => _coloring.MustDo = mustDo.Text ?? string.Empty;
        mustNotDo.TextChanged += (_, _) => _coloring.MustNotDo = mustNotDo.Text ?? string.Empty;
        prompt.TextChanged += (_, _) => _coloring.Prompt = prompt.Text ?? string.Empty;

        void Prepare()
        {
            _coloring.MustDo = mustDo.Text ?? string.Empty;
            _coloring.MustNotDo = mustNotDo.Text ?? string.Empty;
            _coloring.Prompt = BuildColoringPrompt(project, count, _coloring);
            prompt.Text = _coloring.Prompt;
            Report("Prompt preparato. È una bozza: puoi modificarla, selezionarla, copiarla e usare Ctrl+Z.");
        }

        var prepare = Button("Prepara prompt", 155);
        var copy = Button("Copia prompt", 145);
        var next = Button("Avanti → AI / Prompt Pack", 210);
        prepare.Click += (_, _) => Prepare();
        copy.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(prompt.Text)) Prepare();
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) { Report("Appunti di Windows non disponibili."); return; }
            await clipboard.SetTextAsync(prompt.Text ?? string.Empty);
            Report("Prompt copiato dagli esatti contenuti del box.");
        };
        next.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(mustDo.Text))
            {
                Report("Compila DEVE FARE prima di continuare.");
                mustDo.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(prompt.Text)) Prepare();
            if (!await EnsureColoringSeriesAsync(count)) return;
            OpenTransport();
        };

        Push(new LogicalPage(
            $"Coloring Book · 2/4 Istruzioni · {count} immagini",
            new ScrollViewer
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
            },
            BuildCollectionPreview(project, project.AiProductionJobs.Count(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase))),
            "I tre box sono controlli nativi editabili con undo abilitato."));
    }

    private async Task<bool> EnsureColoringSeriesAsync(int desiredCount)
    {
        if (!TrySession(out var project, out var path)) return false;
        var images = project.AiProductionJobs
            .Where(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase))
            .OrderBy(j => j.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (images.Count > desiredCount)
        {
            Report($"Il progetto contiene già {images.Count} immagini: non ne elimino automaticamente {images.Count - desiredCount}. Indica almeno {images.Count} oppure rimuovi esplicitamente quelle che non vuoi.");
            return false;
        }

        var request = HumanAiPromptService.Write(_coloring.MustDo, _coloring.MustNotDo);
        var missing = desiredCount - images.Count;
        if (missing > 0)
        {
            AiImageBatchService.CreateImageSeries(project, missing, request, "Tavola");
            images = project.AiProductionJobs
                .Where(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase))
                .OrderBy(j => j.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var selected = images.Take(desiredCount).ToList();
        for (var i = 0; i < selected.Count; i++)
        {
            var job = selected[i];
            job.Request = request;
            job.Prompt = new StringBuilder()
                .AppendLine(_coloring.Prompt.Trim())
                .AppendLine()
                .AppendLine($"ELEMENTO DIEZ: {job.Code}")
                .AppendLine($"Questa è l'immagine {i + 1} di {desiredCount}.")
                .AppendLine("Genera un risultato distinto dagli altri senza cambiare le regole comuni.")
                .ToString().Trim();
        }

        ImageCollectionWorkspaceService.SetConsistencyRules(project,
            _coloring.Consistent ? _coloring.ConsistencyRules.Trim() : string.Empty);
        var state = AiExchangeStateStore.Load(project);
        AiExchangeStateStore.EnsureVisualConsistencyContext(project, state,
            _coloring.Consistent, _coloring.ConsistencyRules);
        AiExchangeStateStore.Save(project, state);
        await ProjectFileStore.SaveAsync(path, project);
        return true;
    }

    private void OpenTransport()
    {
        if (!TrySession(out var project, out var path)) return;
        var state = AiExchangeStateStore.Load(project);
        AiExchangeStateStore.Save(project, state);
        var imageUnits = state.WorkUnits
            .Where(w => string.Equals(w.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.Position).ThenBy(w => w.Code)
            .ToList();

        var modes = AiExchangeModes.All.Select(m => new ModeChoice(m, AiExchangeModes.UserLabel(m))).ToList();
        var mode = new ComboBox { ItemsSource = modes, SelectedIndex = 1, Width = 480 };
        var paradigmRoles = Editor("character, style", 44, acceptsReturn: false);
        paradigmRoles.Watermark = "Ruoli paradigma: personaggio, stile, palette...";

        var applyMode = Button("Applica modalità", 160);
        var addParadigm = Button("Aggiungi paradigma", 170);
        var export = Button("Crea Prompt Pack ZIP", 190);
        var import = Button("Importa risultati AI", 180);
        var review = Button("Controlla risultati", 175);

        applyMode.Click += async (_, _) =>
        {
            if (mode.SelectedItem is not ModeChoice choice) return;
            foreach (var unit in imageUnits) unit.Mode = choice.Mode;
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(path, project);
            Report($"Modalità '{choice.Label}' applicata alle {imageUnits.Count} immagini.");
        };

        addParadigm.Click += async (_, _) =>
        {
            var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Scegli una o più immagini paradigma",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Immagini") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"] }]
            });
            if (files.Count == 0) return;
            var roles = (paradigmRoles.Text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (roles.Count == 0) roles.Add("reference");
            foreach (var file in files)
            {
                var material = await MaterialImporter.ImportAsync(file.Path.LocalPath);
                var existing = project.Materials.FirstOrDefault(m => string.Equals(m.Sha256, material.Sha256, StringComparison.OrdinalIgnoreCase));
                if (existing is null) { project.Materials.Add(material); existing = material; }
                var paradigm = new AiExchangeParadigm
                {
                    MaterialId = existing.MaterialId,
                    Scope = "COLLECTION",
                    Roles = roles.ToList(),
                    Description = string.Join(", ", roles)
                };
                state.Paradigms.Add(paradigm);
                foreach (var unit in imageUnits)
                    if (!unit.ParadigmIds.Contains(paradigm.ParadigmId)) unit.ParadigmIds.Add(paradigm.ParadigmId);
                await ShowMaterialPreviewAsync(project, path, existing.MaterialId, $"Paradigma · {string.Join(", ", roles)}");
            }
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(path, project);
            Report($"Paradigmi aggiunti. Totale: {state.Paradigms.Count}.");
        };

        export.Click += async (_, _) =>
        {
            var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva Prompt Pack Diez",
                SuggestedFileName = "diez-prompt-pack.zip",
                DefaultExtension = "zip",
                FileTypeChoices = [new FilePickerFileType("Prompt Pack Diez") { Patterns = ["*.zip"] }]
            });
            if (file is null) return;
            var result = await AiExchangePromptPackBuilder.BuildAsync(project, path, state,
                imageUnits.Select(u => u.WorkUnitId), file.Path.LocalPath);
            Report(result.Message);
        };

        import.Click += async (_, _) =>
        {
            var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importa uno o più ZIP restituiti dall'AI",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Risultati AI Diez") { Patterns = ["*.zip"] }]
            });
            if (files.Count == 0) return;
            var result = await AiExchangeResponseImporter.ImportAsync(project, path, state,
                files.Select(f => f.Path.LocalPath));
            Report(result.Message);
            OpenReview();
        };

        review.Click += (_, _) => OpenReview();

        Push(new LogicalPage(
            "Coloring Book · 3/4 AI / Prompt Pack",
            new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = $"{imageUnits.Count} immagini pronte come Work Unit", FontSize = 21 },
                        Label("Come usare input e AI"),
                        mode,
                        applyMode,
                        new Separator(),
                        Label("Immagini paradigma"),
                        paradigmRoles,
                        addParadigm,
                        new Separator(),
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { export, import, review } },
                        new TextBlock
                        {
                            Text = "Prompt Pack e API condividono le stesse Work Unit e versioni. Qui stai provando il percorso Prompt Pack; i risultati finiscono nella stessa revisione.",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        }
                    }
                }
            },
            BuildCollectionPreview(project, imageUnits.Count),
            "Esporta il Prompt Pack, usa l'AI esterna e importa uno o più ZIP anche parziali."));
    }

    private void OpenReview()
    {
        if (!TrySession(out var project, out var path)) return;
        var state = AiExchangeStateStore.Load(project);
        var list = new ListBox { MinHeight = 320 };
        var description = Editor(string.Empty, 150);
        var info = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var approve = Button("Approva", 120);
        var saveDescription = Button("Salva descrizione", 155);
        var openExternal = Button("Apri esternamente", 165);
        var refresh = Button("Aggiorna", 115);
        string? externalPath = null;
        Guid? externalVersionId = null;

        var rows = state.WorkUnits
            .Where(w => string.Equals(w.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.Position).ThenBy(w => w.Code)
            .Select(w => new ReviewChoice(w, ReviewLabel(state, w)))
            .ToList();
        list.ItemsSource = rows;

        AiExchangeWorkUnit? Unit() => (list.SelectedItem as ReviewChoice)?.Unit;
        AiExchangeVersion? Version()
        {
            var unit = Unit();
            return unit is null ? null : state.Versions
                .Where(v => v.WorkUnitId == unit.WorkUnitId && v.Status != AiExchangeVersionStatuses.Rejected)
                .OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        }

        async Task LoadSelection()
        {
            var unit = Unit();
            var version = Version();
            if (unit is null || version is null)
            {
                description.Text = string.Empty;
                info.Text = "Nessun risultato disponibile per questa posizione.";
                SetPreview(PlaceholderPreview("Risultato non ancora ricevuto."));
                return;
            }
            description.Text = version.Description;
            info.Text = $"{unit.Code} · v{version.VersionNumber} · {version.Status} · descrizione {version.DescriptionStatus}";
            if (version.MaterialId is Guid materialId)
                await ShowMaterialPreviewAsync(project, path, materialId, $"{unit.Code} · v{version.VersionNumber}");
            else
                SetPreview(new TextBox { Text = version.TextContent, IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        }

        list.SelectionChanged += async (_, _) => await LoadSelection();
        refresh.Click += async (_, _) => await LoadSelection();
        saveDescription.Click += async (_, _) =>
        {
            var unit = Unit();
            var version = Version();
            if (unit is null || version is null) return;
            version.Description = (description.Text ?? string.Empty).Trim();
            version.DescriptionStatus = string.IsNullOrWhiteSpace(version.Description)
                ? AiExchangeDescriptionStatuses.Missing
                : AiExchangeDescriptionStatuses.Valid;
            if (version.MaterialId.HasValue && version.DescriptionStatus == AiExchangeDescriptionStatuses.Valid &&
                version.Status == AiExchangeVersionStatuses.Incomplete)
                version.Status = AiExchangeVersionStatuses.Candidate;
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(path, project);
            info.Text = "Descrizione salvata per questa versione.";
        };
        approve.Click += async (_, _) =>
        {
            var version = Version();
            if (version is null) return;
            if (!AiExchangeResultIngestor.Approve(project, state, version.VersionId, out var message))
            {
                info.Text = message;
                return;
            }
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(path, project);
            info.Text = message;
            var index = list.SelectedIndex;
            rows = state.WorkUnits
                .Where(w => string.Equals(w.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
                .OrderBy(w => w.Position).ThenBy(w => w.Code)
                .Select(w => new ReviewChoice(w, ReviewLabel(state, w))).ToList();
            list.ItemsSource = rows;
            if (rows.Count > 0) list.SelectedIndex = Math.Clamp(index, 0, rows.Count - 1);
        };
        openExternal.Click += async (_, _) =>
        {
            var version = Version();
            if (version?.MaterialId is not Guid materialId) { info.Text = "Nessun file da aprire."; return; }
            var material = project.Materials.FirstOrDefault(m => m.MaterialId == materialId);
            if (material is null) return;
            var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(path, material);
            if (bytes is null) return;
            var root = Path.Combine(Path.GetTempPath(), "DiezExternalReview");
            Directory.CreateDirectory(root);
            externalPath = Path.Combine(root, version.VersionId.ToString("N") + Path.GetExtension(material.FileName));
            await File.WriteAllBytesAsync(externalPath, bytes);
            externalVersionId = version.VersionId;
            Process.Start(new ProcessStartInfo(externalPath) { UseShellExecute = true });
            info.Text = "Aperto con il programma associato. La reimportazione come nuova versione resta disponibile nel percorso di revisione completo.";
        };

        Push(new LogicalPage(
            "Coloring Book · 4/4 Revisione",
            new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"),
                RowSpacing = 8,
                Children =
                {
                    new TextBlock { Text = "Seleziona una posizione: l'anteprima appare a destra.", FontSize = 20 },
                    list.WithGridRow(1),
                    info.WithGridRow(2),
                    new StackPanel { Spacing = 4, Children = { Label("Descrizione associata"), description } }.WithGridRow(3),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { saveDescription, openExternal, approve, refresh } }.WithGridRow(4)
                }
            },
            PlaceholderPreview("Seleziona un risultato per visualizzarlo."),
            "La revisione usa la stessa Candidate Version indipendentemente dall'origine Prompt Pack/API."));

        if (rows.Count > 0) list.SelectedIndex = 0;
    }

    private async Task ShowMaterialPreviewAsync(PreviewProject project, string path, Guid materialId, string caption)
    {
        var material = project.Materials.FirstOrDefault(m => m.MaterialId == materialId);
        if (material is null) { SetPreview(PlaceholderPreview("Materiale non trovato.")); return; }
        var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(path, material);
        if (bytes is null || bytes.Length == 0) { SetPreview(PlaceholderPreview("Anteprima non disponibile.")); return; }
        try
        {
            _previewBitmap?.Dispose();
            using var memory = new MemoryStream(bytes);
            _previewBitmap = new Bitmap(memory);
            SetPreview(new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                RowSpacing = 6,
                Children =
                {
                    new Image { Source = _previewBitmap, Stretch = Avalonia.Media.Stretch.Uniform },
                    new TextBlock { Text = caption, TextWrapping = Avalonia.Media.TextWrapping.Wrap }.WithGridRow(1)
                }
            });
        }
        catch
        {
            SetPreview(new TextBox { Text = material.Preview, IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        }
    }

    private Control BuildCollectionPreview(PreviewProject project, int imageCount) => new StackPanel
    {
        Spacing = 10,
        Children =
        {
            new TextBlock { Text = "Coloring Book", FontSize = 22 },
            new TextBlock { Text = $"Posizioni immagine attuali: {imageCount}" },
            new TextBlock
            {
                Text = "Quest'area resta riservata all'anteprima. Quando importerai o selezionerai un'immagine, Diez la mostrerà qui senza aprire un'altra finestra.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            },
            new TextBlock { Text = _coloring.Consistent ? "Consistent: ON" : "Consistent: OFF" }
        }
    };

    private static Control PlaceholderPreview(string text) => new Border
    {
        Padding = new Thickness(18),
        Child = new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 17 }
    };

    private void Push(LogicalPage page)
    {
        _history.Add(page);
        _mainView.IsVisible = false;
        _logicalShell.IsVisible = true;
        Show(page);
    }

    private void Show(LogicalPage page)
    {
        _screenTitle.Text = page.Title;
        _logicalContent.Content = page.Content;
        SetPreview(page.Preview);
        _screenStatus.Text = page.Status;
        _back.IsEnabled = _history.Count > 1;
    }

    private void Back()
    {
        if (_history.Count <= 1) { ShowMain(); return; }
        _history.RemoveAt(_history.Count - 1);
        Show(_history[^1]);
    }

    private void ShowMain()
    {
        _history.Clear();
        _logicalContent.Content = null;
        SetPreview(null);
        _logicalShell.IsVisible = false;
        _mainView.IsVisible = true;
        SetMainStatus("Tornato alla schermata principale del progetto.");
    }

    private void SetPreview(Control? control)
    {
        _previewContent.Content = control;
        _previewPane.IsVisible = control is not null;
    }

    private void Report(string text)
    {
        _screenStatus.Text = text;
        SetMainStatus(text);
    }

    private bool TrySession(out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(_window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(_window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private void SetMainStatus(string text)
    {
        var status = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_window) as TextBlock;
        if (status is not null) status.Text = text;
    }

    private static string BuildColoringPrompt(PreviewProject project, int count, ColoringFlowState state)
    {
        var sb = new StringBuilder();
        var common = (project.AiProduction?.ProjectBrief ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(common))
        {
            sb.AppendLine("REGOLE COMUNI DEL PROGETTO:");
            sb.AppendLine(common);
            sb.AppendLine();
        }
        sb.AppendLine($"Crea {count} {(count == 1 ? "immagine" : "immagini")} per un Coloring Book.");
        sb.AppendLine();
        sb.AppendLine("DEVE FARE:");
        sb.AppendLine(state.MustDo.Trim());
        sb.AppendLine();
        sb.AppendLine("NON DEVE FARE:");
        sb.AppendLine(state.MustNotDo.Trim());
        if (state.Consistent)
        {
            sb.AppendLine();
            sb.AppendLine("CONSISTENT:");
            sb.AppendLine(string.IsNullOrWhiteSpace(state.ConsistencyRules)
                ? "Mantieni coerenti personaggi, stile e tratto fra tutte le immagini salvo eccezioni esplicite."
                : state.ConsistencyRules.Trim());
        }
        sb.AppendLine();
        sb.AppendLine("Ogni immagine deve essere distinta e non deve contenere numeri, ID o nomi file dentro l'immagine.");
        return sb.ToString().Trim();
    }

    private static string ReviewLabel(AiExchangeState state, AiExchangeWorkUnit unit)
    {
        var version = state.Versions.Where(v => v.WorkUnitId == unit.WorkUnitId).OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        if (version is null) return "mancante";
        return version.Status switch
        {
            AiExchangeVersionStatuses.Approved => "✓ approvata",
            AiExchangeVersionStatuses.Incomplete => "⚠ incompleta",
            AiExchangeVersionStatuses.Stale => "⚠ da verificare",
            AiExchangeVersionStatuses.Rejected => "scartata",
            _ => "● nuova proposta"
        };
    }

    private static TextBox Editor(string text, double height, bool acceptsReturn = true) => new()
    {
        Text = text,
        Height = height,
        AcceptsReturn = acceptsReturn,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        IsReadOnly = false,
        IsEnabled = true,
        IsHitTestVisible = true,
        Focusable = true,
        IsUndoEnabled = true
    };

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 16 };

    private static Button Button(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        if (root is Panel panel)
            foreach (var child in panel.Children.SelectMany(Descendants)) yield return child;
        if (root is Border border && border.Child is Control child)
            foreach (var nested in Descendants(child)) yield return nested;
        if (root is ScrollViewer scroll && scroll.Content is Control scrollChild)
            foreach (var nested in Descendants(scrollChild)) yield return nested;
        if (root is ContentControl content && content.Content is Control contentChild)
            foreach (var nested in Descendants(contentChild)) yield return nested;
    }

    private sealed class ColoringFlowState
    {
        public string CountText { get; set; } = string.Empty;
        public bool Consistent { get; set; }
        public string ConsistencyRules { get; set; } = string.Empty;
        public string MustDo { get; set; } = string.Empty;
        public string MustNotDo { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
    }

    private sealed record LogicalPage(string Title, Control Content, Control? Preview, string Status);
    private sealed record ModeChoice(string Mode, string Label) { public override string ToString() => Label; }
    private sealed record ReviewChoice(AiExchangeWorkUnit Unit, string State) { public override string ToString() => $"{Unit.Code} · {State}"; }
}
