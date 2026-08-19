using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using DiezPublishingStudio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;

namespace DiezPublishingStudio.UnoSpike;

/// <summary>
/// Publisher interaction shell used after the second physical Uno review.
/// It keeps the existing book-family workspaces but owns transient navigation, project intake,
/// project history and window geometry so those concerns cannot leak into canonical book state.
/// </summary>
internal sealed class DiezPublisherShellHost : ContentControl
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly SolidColorBrush Napoli = Brush("#007FFF");
    private static readonly SolidColorBrush NapoliDark = Brush("#005EB8");
    private static readonly SolidColorBrush White = Brush("#FFFFFF");
    private static readonly SolidColorBrush BorderBlue = Brush("#9CCFFF");

    private readonly MainShellPage _shell;
    private readonly UIElement _polishedShell;
    private readonly TextBlock _projectMirror = new() { TextWrapping = TextWrapping.Wrap, Foreground = White };
    private readonly TextBlock _statusMirror = new() { TextWrapping = TextWrapping.Wrap, Foreground = White, FontSize = 12 };
    private readonly StackPanel _navigationBody = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _sidebarToggle = new() { Content = "«", MinWidth = 34, MinHeight = 30, Padding = new Thickness(5) };
    private readonly Dictionary<string, int> _visualPhaseByProject = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _selectedMaterialByProject = new(StringComparer.Ordinal);
    private readonly Dictionary<TextBox, TextUndoState> _textUndo = [];
    private readonly HashSet<TextBox> _wiredTextBoxes = [];
    private ColumnDefinition? _sidebarColumn;
    private TextBox? _lastFocusedTextBox;
    private string _activeSection = "project";
    private string _activeProjectId = string.Empty;
    private bool _sidebarCollapsed;
    private bool _closingDialogOpen;
    private bool _buildingTabs;
    private bool _tabNavigationBusy;
    private bool _rewrapQueued;
    private bool _cleaningTransientState;

    public DiezPublisherShellHost(MainShellPage shell, UIElement polishedShell)
    {
        _shell = shell;
        _polishedShell = polishedShell;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Background = Napoli;
        Content = BuildShell();
        HideLegacySidebar();
        Loaded += (_, _) =>
        {
            RefreshPresentation();
            ShowProject();
        };
        LayoutUpdated += (_, _) => RefreshPresentation();
    }

    public async Task<bool> ConfirmCloseAsync()
    {
        if (_closingDialogOpen) return false;
        _closingDialogOpen = true;
        try
        {
            var dirty = await HasUnsavedCanonicalChangesAsync();
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = dirty ? "Salvare prima di uscire?" : "Uscire da Diez?",
                Content = dirty
                    ? "Il progetto contiene modifiche non ancora salvate. Vuoi salvarle prima di chiudere Diez?"
                    : "Il progetto risulta salvato. Sei sicuro di voler uscire?",
                PrimaryButtonText = dirty ? "Salva e chiudi" : "Esci",
                CloseButtonText = "Annulla",
                DefaultButton = ContentDialogButton.Primary
            };
            if (dirty) dialog.SecondaryButtonText = "Esci senza salvare";
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.None) return false;
            if (!dirty) return result == ContentDialogResult.Primary;
            if (result == ContentDialogResult.Secondary) return true;
            await InvokeAsync("SaveProjectAsync");
            return !await HasUnsavedCanonicalChangesAsync();
        }
        finally
        {
            _closingDialogOpen = false;
        }
    }

    private UIElement BuildShell()
    {
        var root = new Grid { Background = Napoli };
        _sidebarColumn = new ColumnDefinition { Width = new GridLength(270) };
        root.ColumnDefinitions.Add(_sidebarColumn);
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _sidebarToggle.HorizontalAlignment = HorizontalAlignment.Right;
        _sidebarToggle.Background = NapoliDark;
        _sidebarToggle.Foreground = White;
        _sidebarToggle.BorderBrush = BorderBlue;
        _sidebarToggle.Click += (_, _) => ToggleSidebar();

        _navigationBody.Children.Add(new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                BrandText("Diez", 31, Microsoft.UI.Text.FontWeights.SemiBold),
                BrandText("∞", 40, Microsoft.UI.Text.FontWeights.SemiBold),
                BrandText("Publishing Studio", 14, Microsoft.UI.Text.FontWeights.Normal)
            }
        });
        _navigationBody.Children.Add(new Separator());
        _navigationBody.Children.Add(_projectMirror);
        _navigationBody.Children.Add(new Separator());
        _navigationBody.Children.Add(NavButton("Progetto", ShowProject));
        _navigationBody.Children.Add(NavButton("Tipo libro", ShowBookType));
        _navigationBody.Children.Add(NavButton("Produzione", ShowProduction));
        _navigationBody.Children.Add(NavButton("Controlli e revisione", ShowReview));
        _navigationBody.Children.Add(NavButton("Esportazione", ShowExport));
        _navigationBody.Children.Add(NavButton("Libri finalizzati", ShowFinalized));
        _navigationBody.Children.Add(new Separator());
        _navigationBody.Children.Add(Horizontal(
            MiniButton("↶", () => UndoFocusedControl(), "Undo · Ctrl+Z"),
            MiniButton("↷", () => RedoFocusedControl(), "Redo · Ctrl+Y")));
        _navigationBody.Children.Add(_statusMirror);

        var sidebarGrid = new Grid { Background = Napoli };
        sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _sidebarToggle.Margin = new Thickness(8, 8, 8, 4);
        sidebarGrid.Children.Add(_sidebarToggle);
        var navScroll = new ScrollViewer
        {
            Background = Napoli,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Padding = new Thickness(16, 8, 16, 16), Child = _navigationBody }
        };
        Grid.SetRow(navScroll, 1);
        sidebarGrid.Children.Add(navScroll);

        var sidebar = new Border { Background = Napoli, Child = sidebarGrid };
        Grid.SetColumn(sidebar, 0);
        root.Children.Add(sidebar);

        var workspace = new Border
        {
            Background = Napoli,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = _polishedShell
        };
        Grid.SetColumn(workspace, 1);
        root.Children.Add(workspace);
        return root;
    }

    private void ToggleSidebar()
    {
        if (_sidebarColumn is null) return;
        _sidebarCollapsed = !_sidebarCollapsed;
        _sidebarColumn.Width = new GridLength(_sidebarCollapsed ? 48 : 270);
        _navigationBody.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        _sidebarToggle.Content = _sidebarCollapsed ? "»" : "«";
        Report(_sidebarCollapsed ? "Barra laterale contratta." : "Barra laterale espansa.");
    }

    private void HideLegacySidebar()
    {
        if (_shell.Content is not Grid oldRoot || oldRoot.ColumnDefinitions.Count < 2) return;
        oldRoot.ColumnDefinitions[0].Width = new GridLength(0);
        oldRoot.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        oldRoot.HorizontalAlignment = HorizontalAlignment.Stretch;
        foreach (var child in oldRoot.Children)
            if (Grid.GetColumn(child) == 0) child.Visibility = Visibility.Collapsed;
    }

    private void RefreshPresentation()
    {
        HideLegacySidebar();
        _projectMirror.Text = GetField<TextBlock>("_projectHeader")?.Text ?? "Nessun progetto aperto";
        _statusMirror.Text = GetField<TextBlock>("_status")?.Text ?? "Pronto.";
        DetectProjectChange();
        WireTextEditing(_polishedShell);
        RewrapVisualWorkspaceIfNeeded();
    }

    private void DetectProjectChange()
    {
        var document = Document;
        var id = document is null ? string.Empty : PublisherProjectState.ProjectId(document);
        if (string.Equals(id, _activeProjectId, StringComparison.Ordinal)) return;
        _activeProjectId = id;
        if (document is null) return;
        _visualPhaseByProject[id] = 1;
        if (PublisherProjectState.RemoveUiKey(document, "Visual.ActivePhase"))
            _ = SaveTransientCleanupAsync();
    }

    private void RewrapVisualWorkspaceIfNeeded()
    {
        if (_buildingTabs || _rewrapQueued || !string.Equals(_activeSection, "production", StringComparison.Ordinal)) return;
        var document = Document;
        if (document is null || !BookTypeCatalog.IsVisual(BookTypeCatalog.Normalize(document.BookType))) return;
        if (ContentHost?.Content is TabView tabs && string.Equals(tabs.Tag?.ToString(), "Publisher.ProductionTabs", StringComparison.Ordinal)) return;
        if (ContentHost?.Content is not StackPanel raw || !LooksLikeVisualWorkspace(raw)) return;

        var transient = PublisherProjectState.ReadUiInt(document, "Visual.ActivePhase", 0);
        var id = PublisherProjectState.ProjectId(document);
        if (transient is >= 1 and <= 4) _visualPhaseByProject[id] = transient;
        if (PublisherProjectState.RemoveUiKey(document, "Visual.ActivePhase")) _ = SaveTransientCleanupAsync();
        var target = _visualPhaseByProject.TryGetValue(id, out var phase) ? phase : 1;
        _rewrapQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rewrapQueued = false;
            ShowVisualProductionTab(target);
        });
    }

    private static bool LooksLikeVisualWorkspace(StackPanel root) =>
        root.Children.OfType<TextBlock>().FirstOrDefault()?.Text?.Contains("· percorso immagini", StringComparison.OrdinalIgnoreCase) == true;

    private async Task SaveTransientCleanupAsync()
    {
        if (_cleaningTransientState) return;
        _cleaningTransientState = true;
        try { await InvokeAsync("SaveIfPossibleAsync"); }
        catch { }
        finally { _cleaningTransientState = false; }
    }

    private void ShowProject()
    {
        _activeSection = "project";
        var root = PageRoot("Progetto e materiali", "Crea/apri il .diez, aggiungi materiali con selezione file o drag & drop, verifica subito l'anteprima e dichiara a Diez il ruolo editoriale di ogni materiale.");
        root.Children.Add(Horizontal(
            AsyncButton("Nuovo progetto", async () =>
            {
                await InvokeAsync("CreateProjectAsync");
                DetectProjectChange();
                ShowProject();
            }),
            AsyncButton("Apri .diez", async () =>
            {
                await InvokeAsync("OpenProjectAsync");
                DetectProjectChange();
                ShowProject();
            }),
            AsyncButton("Salva", async () =>
            {
                await InvokeAsync("SaveProjectAsync");
                ShowProject();
            })));

        var document = Document;
        if (document is null)
        {
            root.Children.Add(Card("Nessun progetto aperto", new TextBlock
            {
                Text = "Apri un progetto esistente oppure creane uno nuovo. Il drag & drop si attiva appena esiste un progetto di destinazione.",
                TextWrapping = TextWrapping.Wrap
            }));
            SetWorkspace(root);
            return;
        }

        root.Children.Add(Card("Progetto attivo", Vertical(
            new TextBlock { Text = document.Name, FontSize = 21, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = $"Titolo: {document.EditionTitle} · Tipo: {BookTypeCatalog.Normalize(document.BookType)}", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = $"File: {ProjectPath ?? "(non ancora salvato)"}", TextWrapping = TextWrapping.Wrap })));
        root.Children.Add(BuildMaterialsWorkspace(document));
        root.Children.Add(BuildHistoryWorkspace(document));
        SetWorkspace(root);
    }

    private UIElement BuildMaterialsWorkspace(DiezProjectDocument document)
    {
        var projectId = PublisherProjectState.ProjectId(document);
        var items = ProjectMaterialPreviewPanel.ReadItems(document).ToList();
        var list = new ListView
        {
            MinHeight = 260,
            MaxHeight = 620,
            ItemsSource = items.Select(x => x.Label).ToList(),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var detailHost = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = ReadOnlyText(items.Count == 0 ? "Trascina qui file oppure usa Aggiungi materiali…" : "Seleziona un materiale.")
        };

        async Task RenderSelectionAsync()
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= items.Count)
            {
                detailHost.Content = ReadOnlyText("Seleziona un materiale per anteprima e ruolo editoriale.");
                return;
            }
            var item = items[list.SelectedIndex];
            _selectedMaterialByProject[projectId] = item.MaterialId;
            detailHost.Content = await BuildMaterialDetailAsync(document, item);
        }
        list.SelectionChanged += async (_, _) => await RenderSelectionAsync();

        if (_selectedMaterialByProject.TryGetValue(projectId, out var selectedId))
            list.SelectedIndex = items.FindIndex(x => x.MaterialId == selectedId);
        if (list.SelectedIndex < 0 && items.Count > 0) list.SelectedIndex = 0;

        var add = AsyncButton("Aggiungi materiali…", async () =>
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            var files = await picker.PickMultipleFilesAsync();
            await ImportMaterialFilesAsync(files.OfType<StorageFile>().ToList());
        });
        var remove = AsyncButton("Rimuovi selezionato", async () =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= items.Count)
            {
                Report("Seleziona prima un materiale da rimuovere.");
                return;
            }
            PublisherProjectState.EnsureHistoryBaseline(document);
            var removed = items[list.SelectedIndex];
            if (!document.RemoveMaterialAt(list.SelectedIndex)) return;
            PublisherProjectState.CreateCheckpoint(document, "MATERIAL_REMOVED", "Materiale rimosso", removed.FileName);
            _selectedMaterialByProject.Remove(projectId);
            await InvokeAsync("SaveIfPossibleAsync");
            ShowProject();
            Report($"Materiale rimosso: {removed.FileName}");
        });

        var left = Vertical(
            new TextBlock { Text = "Materiali", FontSize = 19, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Puoi selezionare più file oppure trascinarli direttamente in questa area.", TextWrapping = TextWrapping.Wrap },
            Horizontal(add, remove),
            list);

        var columns = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 430 };
        var leftColumn = new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 260 };
        var splitterColumn = new ColumnDefinition { Width = new GridLength(8) };
        var rightColumn = new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 320 };
        columns.ColumnDefinitions.Add(leftColumn);
        columns.ColumnDefinitions.Add(splitterColumn);
        columns.ColumnDefinitions.Add(rightColumn);
        var leftScroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(leftScroll, 0);
        columns.Children.Add(leftScroll);
        var splitter = new Thumb { Width = 8, Background = BorderBlue, HorizontalAlignment = HorizontalAlignment.Stretch };
        splitter.DragDelta += (_, e) =>
        {
            var total = columns.ActualWidth;
            if (total <= 600) return;
            var current = leftColumn.ActualWidth;
            var next = Math.Clamp(current + e.HorizontalChange, 260, total - 328);
            leftColumn.Width = new GridLength(next);
            rightColumn.Width = new GridLength(1, GridUnitType.Star);
        };
        Grid.SetColumn(splitter, 1);
        columns.Children.Add(splitter);
        var rightScroll = new ScrollViewer { Content = detailHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(rightScroll, 2);
        columns.Children.Add(rightScroll);

        var drop = new Border
        {
            AllowDrop = true,
            Padding = new Thickness(14),
            BorderThickness = new Thickness(2),
            BorderBrush = BorderBlue,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = columns
        };
        drop.DragOver += (_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems)) e.AcceptedOperation = DataPackageOperation.Copy;
        };
        drop.Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var storageItems = await e.DataView.GetStorageItemsAsync();
            await ImportMaterialFilesAsync(storageItems.OfType<StorageFile>().ToList());
        };
        return drop;
    }

    private async Task ImportMaterialFilesAsync(IReadOnlyList<StorageFile> files)
    {
        var document = Document;
        if (document is null || files.Count == 0) return;
        PublisherProjectState.EnsureHistoryBaseline(document);
        var before = ProjectMaterialPreviewPanel.ReadItems(document).Select(x => x.MaterialId).ToHashSet();
        var imported = 0;
        var duplicates = 0;
        foreach (var file in files)
        {
            var result = await document.ImportMaterialAsync(file.Path);
            if (result.StartsWith("Importato", StringComparison.OrdinalIgnoreCase)) imported++;
            else if (result.StartsWith("Duplicato", StringComparison.OrdinalIgnoreCase)) duplicates++;
        }
        var after = ProjectMaterialPreviewPanel.ReadItems(document).ToList();
        var newest = after.LastOrDefault(x => !before.Contains(x.MaterialId));
        if (newest is not null) _selectedMaterialByProject[PublisherProjectState.ProjectId(document)] = newest.MaterialId;
        if (imported > 0)
            PublisherProjectState.CreateCheckpoint(document, "MATERIAL_IMPORT", "Materiali importati", $"{imported} nuovi · {duplicates} duplicati ignorati");
        await InvokeAsync("SaveIfPossibleAsync");
        ShowProject();
        Report($"Materiali: {imported} importati · {duplicates} duplicati ignorati. Il nuovo materiale è selezionato per anteprima e ruolo editoriale.");
    }

    private async Task<UIElement> BuildMaterialDetailAsync(DiezProjectDocument document, ProjectMaterialPreviewItem item)
    {
        UIElement preview;
        try { preview = await InvokeMaterialPreviewAsync(document, item); }
        catch (Exception ex) { preview = ReadOnlyText("Anteprima non disponibile: " + ex.GetBaseException().Message); }

        var current = PublisherProjectState.ReadMaterialIntent(document, item.MaterialId);
        var choices = IntentChoices(item.Kind);
        var intent = new ComboBox
        {
            ItemsSource = choices,
            MinWidth = 330,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        intent.SelectedItem = choices.FirstOrDefault(x => x.Code == current.IntentCode) ?? choices.First();
        var instruction = new TextBox
        {
            Text = current.Instruction,
            PlaceholderText = "Istruzione specifica: cosa mantenere, cosa modificare, cosa non cambiare…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 100,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var policy = Combo(["ALLOW", "REFERENCE_ONLY", "DIRECT_ASSET", "NEVER_SEND"], current.AiUsePolicy);
        var fidelity = Combo(["EXACT", "CLOSE", "GUIDED", "LOOSE", "NOT_APPLICABLE"], current.Fidelity);
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap };

        void ApplyDefaults()
        {
            if (intent.SelectedItem is not IntentChoice selected) return;
            info.Text = selected.Description;
            policy.SelectedItem = selected.DefaultPolicy;
            fidelity.SelectedItem = selected.DefaultFidelity;
            instruction.PlaceholderText = selected.Code == "MODIFY_SPECIFIC_DETAILS"
                ? "Obbligatorio: indica esattamente i particolari da cambiare e quelli da lasciare invariati."
                : "Istruzione editoriale opzionale per questo materiale.";
        }
        intent.SelectionChanged += (_, _) => ApplyDefaults();
        info.Text = (intent.SelectedItem as IntentChoice)?.Description ?? string.Empty;

        var saveIntent = AsyncButton("Salva ruolo materiale", async () =>
        {
            if (intent.SelectedItem is not IntentChoice selected) return;
            if (selected.Code == "MODIFY_SPECIFIC_DETAILS" && string.IsNullOrWhiteSpace(instruction.Text))
            {
                Report("Per “Modifica particolari specifici” descrivi prima quali dettagli possono/devono cambiare.");
                return;
            }
            PublisherProjectState.EnsureHistoryBaseline(document);
            PublisherProjectState.SaveMaterialIntent(
                document,
                item.MaterialId,
                selected.Code,
                selected.Label,
                instruction.Text ?? string.Empty,
                policy.SelectedItem?.ToString() ?? selected.DefaultPolicy,
                fidelity.SelectedItem?.ToString() ?? selected.DefaultFidelity);
            PublisherProjectState.CreateCheckpoint(document, "MATERIAL_INTENT", "Ruolo materiale modificato", $"{item.FileName} → {selected.Label}");
            await InvokeAsync("SaveIfPossibleAsync");
            ShowProject();
            Report($"Ruolo salvato: {item.FileName} → {selected.Label}");
        });

        return Vertical(
            new TextBlock { Text = item.FileName, FontSize = 20, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = $"{item.Kind} · {ProjectMaterialPreviewPanel.FormatSize(item.SizeBytes)}\nSHA-256: {item.Sha256}", TextWrapping = TextWrapping.Wrap },
            Card("Anteprima", preview),
            Card("Come vuoi usare questo materiale?", Vertical(
                Labeled("Ruolo editoriale", intent),
                info,
                Labeled("Istruzione specifica", instruction),
                Horizontal(Labeled("Uso AI", policy), Labeled("Fedeltà", fidelity)),
                new TextBlock { Text = "“Archivio / non inviare all'AI” e “Asset diretto” restano nel progetto ma non devono entrare silenziosamente nei Prompt Pack di generazione.", TextWrapping = TextWrapping.Wrap },
                saveIntent)));
    }

    private UIElement BuildHistoryWorkspace(DiezProjectDocument document)
    {
        var history = PublisherProjectState.History(document).ToList();
        var list = new ListView
        {
            MinHeight = 170,
            MaxHeight = 320,
            ItemsSource = history.Select(x => x.Display).ToList()
        };
        var note = new TextBox
        {
            PlaceholderText = "Nota checkpoint (opzionale)",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 70,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        return Card("Cronologia progetto", Vertical(
            new TextBlock
            {
                Text = "Questa cronologia è diversa da Ctrl+Z/Ctrl+Y: registra stati di avanzamento del progetto. Un ripristino non cancella la cronologia e puoi tornare avanti o scegliere un ramo alternativo.",
                TextWrapping = TextWrapping.Wrap
            },
            list,
            Labeled("Nota", note),
            Horizontal(
                AsyncButton("Crea checkpoint", async () =>
                {
                    if (!PublisherProjectState.HasHistory(document))
                        PublisherProjectState.EnsureHistoryBaseline(document, "Primo checkpoint");
                    else
                        PublisherProjectState.CreateCheckpoint(document, "MANUAL_CHECKPOINT", "Checkpoint manuale", note.Text);
                    await InvokeAsync("SaveIfPossibleAsync");
                    ShowProject();
                    Report("Checkpoint progetto registrato.");
                }),
                AsyncButton("← Stato precedente", async () =>
                {
                    if (!PublisherProjectState.MoveBack(document, out var message)) { Report(message); return; }
                    await InvokeAsync("SaveIfPossibleAsync");
                    ShowProject();
                    Report(message);
                }),
                AsyncButton("Stato successivo →", async () =>
                {
                    if (!PublisherProjectState.MoveForward(document, out var message)) { Report(message); return; }
                    await InvokeAsync("SaveIfPossibleAsync");
                    ShowProject();
                    Report(message);
                }),
                AsyncButton("Ripristina selezionato", async () =>
                {
                    if (list.SelectedIndex < 0 || list.SelectedIndex >= history.Count)
                    {
                        Report("Seleziona un checkpoint dalla cronologia.");
                        return;
                    }
                    if (!PublisherProjectState.RestoreHistory(document, history[list.SelectedIndex].HistoryId, out var message)) { Report(message); return; }
                    await InvokeAsync("SaveIfPossibleAsync");
                    ShowProject();
                    Report(message);
                }))));
    }

    private void ShowBookType()
    {
        _activeSection = "book-type";
        Invoke("ShowBookRoute");
    }

    private void ShowProduction()
    {
        _activeSection = "production";
        var document = Document;
        if (document is null)
        {
            ShowProject();
            Report("Prima crea o apri un progetto .diez.");
            return;
        }
        var type = BookTypeCatalog.Normalize(document.BookType);
        if (!BookTypeCatalog.IsVisual(type))
        {
            Invoke("RouteCurrentBookType");
            return;
        }
        var id = PublisherProjectState.ProjectId(document);
        var selected = _visualPhaseByProject.TryGetValue(id, out var phase) ? phase : 1;
        ShowVisualProductionTab(selected);
    }

    private void ShowVisualProductionTab(int selected)
    {
        var document = Document;
        if (document is null) return;
        selected = Math.Clamp(selected, 1, 5);
        var projectId = PublisherProjectState.ProjectId(document);
        _visualPhaseByProject[projectId] = selected;
        _buildingTabs = true;
        try
        {
            if (selected == 5)
            {
                Invoke("ShowScenesAndSubjects");
            }
            else
            {
                document.SetUiInt("Visual.ActivePhase", selected);
                Invoke("ShowVisualWorkspace");
                PublisherProjectState.RemoveUiKey(document, "Visual.ActivePhase");
            }
            if (ContentHost?.Content is not UIElement currentContent) return;
            ContentHost.Content = BuildSafeTabView(
                ["1 · Definizione", "2 · Prompt", "3 · Produzione", "4 · Revisione", "Scene e soggetti"],
                selected - 1,
                currentContent,
                "Publisher.ProductionTabs",
                async index =>
                {
                    if (_tabNavigationBusy) return;
                    _tabNavigationBusy = true;
                    try { ShowVisualProductionTab(index + 1); }
                    finally { _tabNavigationBusy = false; }
                    await Task.CompletedTask;
                });
        }
        finally
        {
            _buildingTabs = false;
        }
    }

    private void ShowReview()
    {
        _activeSection = "review";
        ShowReviewTab(0);
    }

    private void ShowReviewTab(int selected)
    {
        if (Document is null)
        {
            ShowProject();
            Report("Prima crea o apri un progetto .diez.");
            return;
        }
        var methods = new[] { "ShowEditableMaster", "ShowContentGraph", "ShowConsistency" };
        selected = Math.Clamp(selected, 0, methods.Length - 1);
        _buildingTabs = true;
        try
        {
            Invoke(methods[selected]);
            if (ContentHost?.Content is not UIElement currentContent) return;
            ContentHost.Content = BuildSafeTabView(
                ["Testo principale", "Mappa contenuti + guida progetto", "Controllo coerenza"],
                selected,
                currentContent,
                "Publisher.ReviewTabs",
                async index =>
                {
                    if (_tabNavigationBusy) return;
                    _tabNavigationBusy = true;
                    try { ShowReviewTab(index); }
                    finally { _tabNavigationBusy = false; }
                    await Task.CompletedTask;
                });
        }
        finally { _buildingTabs = false; }
    }

    private TabView BuildSafeTabView(
        IReadOnlyList<string> names,
        int selected,
        UIElement currentContent,
        string tag,
        Func<int, Task> onChanged)
    {
        var tabs = new TabView
        {
            Tag = tag,
            IsAddTabButtonVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        for (var i = 0; i < names.Count; i++)
        {
            tabs.TabItems.Add(new TabViewItem
            {
                IsClosable = false,
                Header = new TextBlock { Text = names[i], Foreground = White, TextWrapping = TextWrapping.NoWrap },
                Content = i == selected ? currentContent : null
            });
        }
        tabs.SelectedIndex = Math.Clamp(selected, 0, names.Count - 1);
        var armed = false;
        tabs.Loaded += (_, _) => armed = true;
        tabs.SelectionChanged += async (_, _) =>
        {
            if (!armed || _buildingTabs || _tabNavigationBusy) return;
            var target = tabs.SelectedIndex;
            if (target < 0 || target == selected) return;
            await onChanged(target);
        };
        return tabs;
    }

    private void ShowExport()
    {
        _activeSection = "export";
        Invoke("ShowExportAndFinalization");
        var document = Document;
        if (document is null || ContentHost?.Content is not StackPanel root) return;
        var panel = Vertical(
            new TextBlock { Text = "Materiali a corredo", FontSize = 19, TextWrapping = TextWrapping.Wrap },
            new TextBlock
            {
                Text = "Esporta materiali utente e asset AI approvati separatamente dal file del libro. Le Candidate AI non approvate restano nel progetto.",
                TextWrapping = TextWrapping.Wrap
            },
            AsyncButton("Materiali del libro · ZIP", async () =>
            {
                var path = await PickSavePathAsync(SafeName(document.EditionTitle) + "-materiali.zip", ".zip", "Archivio ZIP");
                if (path is null) return;
                Report(await UnoConsolidationExportService.ExportMaterialsZipAsync(document, path));
            }));

        if (string.Equals(BookTypeCatalog.Normalize(document.BookType), BookTypeCatalog.WordSearch, StringComparison.OrdinalIgnoreCase))
        {
            panel.Children.Add(new Separator());
            panel.Children.Add(new TextBlock { Text = "Word Search · database XLSX", FontSize = 19, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(Horizontal(
                AsyncButton("Database completo · XLSX", async () =>
                {
                    var path = await PickSavePathAsync(SafeName(document.EditionTitle) + "-database-completo.xlsx", ".xlsx", "Foglio Excel XLSX");
                    if (path is null) return;
                    Report(await UnoConsolidationExportService.ExportWordSearchFullDatabaseAsync(document, path));
                }),
                AsyncButton("Database del libro · XLSX", async () =>
                {
                    var path = await PickSavePathAsync(SafeName(document.EditionTitle) + "-database-libro.xlsx", ".xlsx", "Foglio Excel XLSX");
                    if (path is null) return;
                    Report(await UnoConsolidationExportService.ExportWordSearchBookDatabaseAsync(document, path));
                })));
        }
        root.Children.Add(Card("Export publisher", panel));
    }

    private void ShowFinalized()
    {
        _activeSection = "finalized";
        Invoke("ShowFinalizedLibrary");
    }

    private void WireTextEditing(DependencyObject node)
    {
        if (node is TextBox box && !box.IsReadOnly)
        {
            box.SelectionHighlightColor = Napoli;
            if (_wiredTextBoxes.Add(box))
            {
                _textUndo[box] = new TextUndoState(box.Text ?? string.Empty);
                box.GotFocus += (_, _) =>
                {
                    _lastFocusedTextBox = box;
                    box.BorderBrush = Napoli;
                    box.BorderThickness = new Thickness(3);
                };
                box.LostFocus += (_, _) =>
                {
                    box.BorderBrush = BorderBlue;
                    box.BorderThickness = new Thickness(1);
                };
                box.TextChanged += (_, _) => TrackTextChange(box);
                var undo = new KeyboardAccelerator { Key = VirtualKey.Z, Modifiers = VirtualKeyModifiers.Control };
                undo.Invoked += (_, e) => { Undo(box); e.Handled = true; };
                var redo = new KeyboardAccelerator { Key = VirtualKey.Y, Modifiers = VirtualKeyModifiers.Control };
                redo.Invoked += (_, e) => { Redo(box); e.Handled = true; };
                box.KeyboardAccelerators.Add(undo);
                box.KeyboardAccelerators.Add(redo);
            }
        }
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++) WireTextEditing(VisualTreeHelper.GetChild(node, i));
    }

    private void TrackTextChange(TextBox box)
    {
        if (!_textUndo.TryGetValue(box, out var state) || state.Applying) return;
        var current = box.Text ?? string.Empty;
        if (string.Equals(current, state.LastText, StringComparison.Ordinal)) return;
        state.Undo.Push(state.LastText);
        state.Redo.Clear();
        state.LastText = current;
    }

    private void UndoFocusedControl()
    {
        if (_lastFocusedTextBox is null) { Report("Nessun controllo testuale attivo per Undo."); return; }
        Undo(_lastFocusedTextBox);
    }

    private void RedoFocusedControl()
    {
        if (_lastFocusedTextBox is null) { Report("Nessun controllo testuale attivo per Redo."); return; }
        Redo(_lastFocusedTextBox);
    }

    private void Undo(TextBox box)
    {
        if (!_textUndo.TryGetValue(box, out var state) || state.Undo.Count == 0) return;
        state.Redo.Push(box.Text ?? string.Empty);
        ApplyTextHistory(box, state, state.Undo.Pop());
    }

    private void Redo(TextBox box)
    {
        if (!_textUndo.TryGetValue(box, out var state) || state.Redo.Count == 0) return;
        state.Undo.Push(box.Text ?? string.Empty);
        ApplyTextHistory(box, state, state.Redo.Pop());
    }

    private static void ApplyTextHistory(TextBox box, TextUndoState state, string value)
    {
        state.Applying = true;
        try
        {
            box.Text = value;
            state.LastText = value;
            box.SelectionStart = value.Length;
        }
        finally { state.Applying = false; }
    }

    private async Task<UIElement> InvokeMaterialPreviewAsync(DiezProjectDocument document, ProjectMaterialPreviewItem item)
    {
        var method = typeof(ProjectMaterialPreviewPanel).GetMethod("BuildPreviewAsync", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("ProjectMaterialPreviewPanel.BuildPreviewAsync");
        if (method.Invoke(null, [document, item]) is not Task<UIElement> task)
            throw new InvalidOperationException("L'anteprima materiali non ha restituito il tipo atteso.");
        return await task;
    }

    private async Task<string?> PickSavePathAsync(string suggestedFileName, string extension, string typeName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName)
        };
        picker.FileTypeChoices.Add(typeName, new List<string> { extension });
        return (await picker.PickSaveFileAsync())?.Path;
    }

    private async Task<bool> HasUnsavedCanonicalChangesAsync()
    {
        var document = Document;
        if (document is null) return false;
        var path = ProjectPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return true;
        try
        {
            var memory = NormalizeForComparison(JsonNode.Parse(document.ExportProjectJson()));
            JsonNode? disk;
            try
            {
                using var archive = ZipFile.OpenRead(path);
                var entry = archive.GetEntry("project.json");
                if (entry is null) return true;
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
                disk = JsonNode.Parse(await reader.ReadToEndAsync());
            }
            catch (InvalidDataException)
            {
                disk = JsonNode.Parse(await File.ReadAllTextAsync(path));
            }
            disk = NormalizeForComparison(disk);
            return !JsonNode.DeepEquals(memory, disk);
        }
        catch { return true; }
    }

    private static JsonNode? NormalizeForComparison(JsonNode? node)
    {
        var clone = node?.DeepClone();
        if (clone is JsonObject obj) obj.Remove("SavedAtLocal");
        return clone;
    }

    private DiezProjectDocument? Document => GetField<DiezProjectDocument>("_document");
    private string? ProjectPath => GetField<string>("_projectPath");
    private ContentControl? ContentHost => GetField<ContentControl>("_contentHost");

    private T? GetField<T>(string name) where T : class =>
        typeof(MainShellPage).GetField(name, PrivateInstance)?.GetValue(_shell) as T;

    private object? Invoke(string name) =>
        typeof(MainShellPage).GetMethod(name, PrivateInstance)?.Invoke(_shell, null);

    private async Task InvokeAsync(string name)
    {
        if (Invoke(name) is Task task) await task;
    }

    private void SetWorkspace(UIElement content)
    {
        if (ContentHost is not null) ContentHost.Content = content;
    }

    private void Report(string message)
    {
        if (GetField<TextBlock>("_status") is { } status) status.Text = message;
        _statusMirror.Text = message;
    }

    private static StackPanel PageRoot(string title, string description)
    {
        var root = new StackPanel { Spacing = 16, Margin = new Thickness(28), HorizontalAlignment = HorizontalAlignment.Stretch };
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
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Child = Vertical(new TextBlock { Text = title, FontSize = 19, TextWrapping = TextWrapping.Wrap }, content)
    };

    private static StackPanel Vertical(params UIElement[] items)
    {
        var panel = new StackPanel { Spacing = 9, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static StackPanel Horizontal(params UIElement[] items)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static StackPanel Labeled(string label, UIElement control) =>
        Vertical(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, control);

    private static ComboBox Combo(IEnumerable<string> values, string selected)
    {
        var items = values.ToList();
        var combo = new ComboBox { ItemsSource = items, MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Left };
        combo.SelectedItem = items.FirstOrDefault(x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
        return combo;
    }

    private static Button NavButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Background = NapoliDark,
            Foreground = White,
            BorderBrush = BorderBlue,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 9)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button MiniButton(string text, Action action, string tooltip)
    {
        var button = new Button { Content = text, MinWidth = 36, Padding = new Thickness(6), Background = NapoliDark, Foreground = White, BorderBrush = BorderBlue };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    private static Button AsyncButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8), Background = NapoliDark, Foreground = White, BorderBrush = BorderBlue };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static TextBlock BrandText(string text, double size, Windows.UI.Text.FontWeight weight) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = White,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static UIElement ReadOnlyText(string text) => new TextBox
    {
        Text = text,
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 120,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static IReadOnlyList<IntentChoice> IntentChoices(string kind)
    {
        if (string.Equals(kind, "Image", StringComparison.OrdinalIgnoreCase))
            return
            [
                new("UNASSIGNED", "Da decidere", "Il file è nel progetto ma Diez non deve ancora assumerne un uso.", "NEVER_SEND", "NOT_APPLICABLE"),
                new("DIRECT_BOOK_ASSET", "Inserisci nel libro così com'è", "È un asset editoriale originale: non rigenerarlo automaticamente.", "DIRECT_ASSET", "NOT_APPLICABLE"),
                new("SUBJECT_IDENTITY_REFERENCE", "Modello / identità di un soggetto", "Usalo per preservare l'identità visuale di personaggi, oggetti o prodotti ricorrenti.", "REFERENCE_ONLY", "CLOSE"),
                new("STYLE_REFERENCE", "Reference di stile", "Usa linguaggio visuale, tratto, resa o atmosfera senza copiare automaticamente soggetto/composizione.", "REFERENCE_ONLY", "LOOSE"),
                new("COMPOSITION_REFERENCE", "Reference di composizione", "Usa inquadratura, disposizione, punto di vista o layout come riferimento.", "REFERENCE_ONLY", "GUIDED"),
                new("ENVIRONMENT_REFERENCE", "Reference ambiente / sfondo", "Usa luogo, scenario o background come riferimento editoriale.", "REFERENCE_ONLY", "GUIDED"),
                new("REPLICATE_CLOSELY", "Replica molto fedelmente", "Ricrea il contenuto con alta fedeltà rispettando i vincoli di output del libro.", "ALLOW", "CLOSE"),
                new("TRANSFORM_REINTERPRET", "Trasforma / reinterpreta", "Usa il contenuto come base ma produci una trasformazione sostanziale guidata dall'istruzione.", "ALLOW", "GUIDED"),
                new("MODIFY_SPECIFIC_DETAILS", "Modifica solo particolari specifici", "Preserva ciò che non viene indicato e cambia soltanto i dettagli descritti nel campo istruzione.", "ALLOW", "CLOSE"),
                new("INSPIRATION_ONLY", "Solo ispirazione", "Trai ispirazione generale senza imitare fedelmente il file.", "REFERENCE_ONLY", "LOOSE"),
                new("ARCHIVE_NEVER_SEND", "Solo archivio / non inviare all'AI", "Conserva il materiale per il publisher, ma escludilo dai Prompt Pack.", "NEVER_SEND", "NOT_APPLICABLE")
            ];
        if (string.Equals(kind, "Table", StringComparison.OrdinalIgnoreCase))
            return
            [
                new("UNASSIGNED", "Da decidere", "Uso ancora da assegnare.", "NEVER_SEND", "NOT_APPLICABLE"),
                new("CANONICAL_DATASET", "Dataset canonico", "Fonte dati autorevole del progetto.", "ALLOW", "EXACT"),
                new("BOOK_FAMILY_DATABASE_IMPORT", "Database della funzione libro", "Importalo nel database specializzato della famiglia, per esempio Word Search.", "ALLOW", "EXACT"),
                new("SCHEMA_REFERENCE", "Modello / schema dati", "Usa colonne e struttura come schema, non necessariamente i valori come verità finale.", "REFERENCE_ONLY", "GUIDED"),
                new("LOOKUP_REFERENCE", "Tabella di consultazione", "Usala per lookup e verifiche.", "REFERENCE_ONLY", "EXACT"),
                new("NORMALIZE_DEDUP_SOURCE", "Fonte da normalizzare / deduplicare", "I dati possono essere corretti, uniformati e deduplicati prima dell'uso.", "ALLOW", "GUIDED"),
                new("ARCHIVE_NEVER_SEND", "Solo archivio / non inviare all'AI", "Conserva senza inviare all'AI.", "NEVER_SEND", "NOT_APPLICABLE")
            ];
        return
        [
            new("UNASSIGNED", "Da decidere", "Uso ancora da assegnare.", "NEVER_SEND", "NOT_APPLICABLE"),
            new("AUTHORITATIVE_SOURCE", "Fonte autorevole", "Usa fatti e contenuti come fonte da rispettare.", "ALLOW", "EXACT"),
            new("TRANSFORM_SOURCE", "Fonte da trasformare", "Riassumi, adatta o rielabora secondo l'obiettivo editoriale.", "ALLOW", "GUIDED"),
            new("STYLE_TONE_REFERENCE", "Reference stile / tono", "Usa voce e tono come riferimento senza copiare il testo.", "REFERENCE_ONLY", "LOOSE"),
            new("STRUCTURE_REFERENCE", "Reference struttura / indice", "Usa organizzazione, sezioni e gerarchia come modello.", "REFERENCE_ONLY", "GUIDED"),
            new("TERMINOLOGY_AUTHORITY", "Autorità terminologica / glossario", "Terminologia e denominazioni sono vincolanti salvo istruzione contraria.", "ALLOW", "EXACT"),
            new("MASTER_SOURCE_TEXT", "Testo originale per il Master", "È testo editoriale da preservare e modificare nel Master, non semplice reference.", "DIRECT_ASSET", "NOT_APPLICABLE"),
            new("ARCHIVE_NEVER_SEND", "Solo archivio / non inviare all'AI", "Conserva il documento per il publisher senza inviarlo all'AI.", "NEVER_SEND", "NOT_APPLICABLE")
        ];
    }

    private static string SafeName(string? value)
    {
        var raw = string.IsNullOrWhiteSpace(value) ? "libro" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(raw.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "libro" : safe;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var value = hex.TrimStart('#');
        return new SolidColorBrush(Color.FromArgb(255, Convert.ToByte(value[0..2], 16), Convert.ToByte(value[2..4], 16), Convert.ToByte(value[4..6], 16)));
    }

    private sealed record IntentChoice(string Code, string Label, string Description, string DefaultPolicy, string DefaultFidelity)
    {
        public override string ToString() => Label;
    }

    private sealed class TextUndoState
    {
        public TextUndoState(string initial) => LastText = initial;
        public Stack<string> Undo { get; } = new();
        public Stack<string> Redo { get; } = new();
        public string LastText { get; set; }
        public bool Applying { get; set; }
    }
}
