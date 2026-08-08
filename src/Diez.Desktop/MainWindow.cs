using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

public sealed class MainWindow : Window
{
    private readonly TextBlock _status;
    private readonly ListBox _materialsList;
    private readonly ListBox _structureList;
    private readonly ListBox _entitiesList;
    private readonly TextBox _preview;
    private string? _currentProjectPath;
    private PreviewProject? _project;

    public MainWindow(string? startupProjectPath = null)
    {
        Title = "Diez Publishing Studio — 0.4 Preview";
        Width = 1180;
        Height = 940;
        MinWidth = 940;
        MinHeight = 740;

        var logo = new TextBlock { Text = "∞", FontSize = 38, HorizontalAlignment = HorizontalAlignment.Center };
        var title = new TextBlock { Text = "Diez Publishing Studio", FontSize = 27, HorizontalAlignment = HorizontalAlignment.Center };
        var subtitle = new TextBlock
        {
            Text = "Preview 0.4 — Intake, struttura editoriale, Content Graph e Bible",
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var newButton = MakeButton("Nuovo progetto");
        newButton.Click += async (_, _) => await CreateProjectAsync();
        var openButton = MakeButton("Apri .diez");
        openButton.Click += async (_, _) => await OpenProjectAsync();
        var importButton = MakeButton("Importa materiali");
        importButton.Click += async (_, _) => await ImportMaterialsAsync();
        var removeButton = MakeButton("Rimuovi materiale");
        removeButton.Click += async (_, _) => await RemoveSelectedMaterialAsync();
        var saveButton = MakeButton("Salva");
        saveButton.Click += async (_, _) => await SaveCurrentAsync();

        var confirmEntityButton = MakeButton("Conferma entità");
        confirmEntityButton.Click += async (_, _) => await ConfirmSelectedEntityAsync();
        var ignoreEntityButton = MakeButton("Ignora entità");
        ignoreEntityButton.Click += async (_, _) => await IgnoreSelectedEntityAsync();

        _status = new TextBlock
        {
            Text = "Pronto. Diez rileva candidati personaggio/luogo, li collega ai contenuti e li lascia da confermare prima che diventino canonici nella Bible.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 1020
        };

        _materialsList = new ListBox { Width = 1020, Height = 105 };
        _materialsList.SelectionChanged += (_, _) => ShowSelectedMaterial();
        _structureList = new ListBox { Width = 1020, Height = 130 };
        _structureList.SelectionChanged += (_, _) => ShowSelectedContentNode();
        _entitiesList = new ListBox { Width = 1020, Height = 125 };
        _entitiesList.SelectionChanged += (_, _) => ShowSelectedEntity();

        _preview = new TextBox
        {
            Width = 1020,
            Height = 180,
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Seleziona materiale, struttura o entità del Content Graph."
        };

        var projectButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { newButton, openButton, importButton, removeButton, saveButton }
        };
        var graphButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { confirmEntityButton, ignoreEntityButton }
        };

        Content = new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8,
                Children =
                {
                    logo, title, subtitle, projectButtons, _status,
                    MakeSectionLabel("Materiali incorporati"), _materialsList,
                    MakeSectionLabel("Struttura editoriale"), _structureList,
                    MakeSectionLabel("Content Graph / candidati Bible"), _entitiesList, graphButtons,
                    MakeSectionLabel("Dettaglio"), _preview
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(startupProjectPath))
        {
            Opened += async (_, _) =>
            {
                if (File.Exists(startupProjectPath)) await OpenProjectPathAsync(startupProjectPath);
            };
        }
    }

    private static Button MakeButton(string text) => new()
    {
        Content = text,
        Width = 180,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static TextBlock MakeSectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 16,
        HorizontalAlignment = HorizontalAlignment.Left,
        Width = 1020
    };

    private async Task CreateProjectAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Crea progetto Diez",
            SuggestedFileName = "NuovoProgetto.diez",
            DefaultExtension = "diez",
            FileTypeChoices = [new FilePickerFileType("Progetto Diez") { Patterns = ["*.diez"] }]
        });
        if (file is null) return;

        try
        {
            _currentProjectPath = file.Path.LocalPath;
            _project = ProjectFileStore.Create(Path.GetFileNameWithoutExtension(_currentProjectPath));
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews();
            _status.Text = $"Creato pacchetto .diez: {_currentProjectPath}";
        }
        catch (Exception ex) { _status.Text = $"Errore creazione: {ex.Message}"; }
    }

    private async Task OpenProjectAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Apri progetto Diez",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Progetto Diez") { Patterns = ["*.diez"] }]
        });
        var file = files.FirstOrDefault();
        if (file is not null) await OpenProjectPathAsync(file.Path.LocalPath);
    }

    private async Task OpenProjectPathAsync(string path)
    {
        try
        {
            var wasPackage = ProjectFileStore.IsPackageFile(path);
            var project = await ProjectFileStore.LoadAsync(path);
            _currentProjectPath = path;
            _project = project;
            RefreshViews();
            _status.Text = wasPackage
                ? $"Aperto: {project.Name} · {project.Materials.Count} materiali · {project.ContentNodes.Count} elementi · {project.Entities.Count} entità · {project.BibleEntries.Count(b => b.IsActive)} voci Bible"
                : $"Aperto progetto legacy: {project.Name}. Al prossimo Salva verrà convertito nel pacchetto .diez corrente.";
        }
        catch (Exception ex) { _status.Text = $"Errore apertura: {ex.Message}"; }
    }

    private async Task ImportMaterialsAsync()
    {
        if (_project is null || string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            _status.Text = "Prima crea o apri un progetto .diez.";
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importa materiali",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Materiali supportati") { Patterns = ["*.txt", "*.md", "*.csv", "*.xlsx", "*.docx", "*.odt", "*.rtf", "*.pdf", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"] },
                new FilePickerFileType("Documenti") { Patterns = ["*.txt", "*.md", "*.docx", "*.odt", "*.rtf", "*.pdf"] },
                new FilePickerFileType("Tabelle") { Patterns = ["*.csv", "*.xlsx"] },
                new FilePickerFileType("Immagini") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"] }
            ]
        });
        if (files.Count == 0) return;

        var imported = 0;
        var duplicates = 0;
        var editorialNodes = 0;
        var entities = 0;
        var relations = 0;
        var errors = new List<string>();
        MaterialEntry? lastImported = null;
        _status.Text = $"Analisi di {files.Count} materiali in corso...";

        foreach (var file in files)
        {
            try
            {
                var sourcePath = file.Path.LocalPath;
                var material = await MaterialImporter.ImportAsync(sourcePath);
                if (_project.Materials.Any(existing => string.Equals(existing.Sha256, material.Sha256, StringComparison.OrdinalIgnoreCase)))
                {
                    duplicates++;
                    continue;
                }

                material.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
                _project.Materials.Add(material);
                var nodes = ContentStructureAnalyzer.Analyze(material);
                _project.ContentNodes.AddRange(nodes);
                var graph = ContentGraphEngine.Analyze(_project, material, nodes);
                editorialNodes += nodes.Count;
                entities += graph.EntitiesCreated;
                relations += graph.RelationsCreated;
                imported++;
                lastImported = material;
            }
            catch (Exception ex) { errors.Add($"{file.Name}: {ex.Message}"); }
        }

        try
        {
            if (imported > 0) await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews();
            if (lastImported is not null) _materialsList.SelectedIndex = _project.Materials.IndexOf(lastImported);

            var message = $"Importati {imported} materiali · {editorialNodes} elementi · {entities} nuove entità · {relations} nuove relazioni";
            if (duplicates > 0) message += $" · {duplicates} duplicati ignorati";
            if (errors.Count > 0) message += $" · {errors.Count} errori: {string.Join("; ", errors.Take(2))}";
            else if (imported > 0) message += " · originali incorporati nel .diez";
            _status.Text = message;
        }
        catch (Exception ex) { _status.Text = $"Errore durante il salvataggio del pacchetto: {ex.Message}"; }
    }

    private async Task RemoveSelectedMaterialAsync()
    {
        if (_project is null || string.IsNullOrWhiteSpace(_currentProjectPath) ||
            _materialsList.SelectedIndex < 0 || _materialsList.SelectedIndex >= _project.Materials.Count)
        {
            _status.Text = "Seleziona prima un materiale da rimuovere.";
            return;
        }

        var index = _materialsList.SelectedIndex;
        var removed = _project.Materials[index];
        var removedNodes = _project.ContentNodes.Where(n => n.MaterialId == removed.MaterialId).ToList();
        var removedNodeIds = removedNodes.Select(n => n.ContentId).ToHashSet();
        var removedEntities = _project.Entities.Where(e => e.SourceMaterialId == removed.MaterialId && e.IsCandidate).ToList();
        var removedEntityIds = removedEntities.Select(e => e.EntityId).ToHashSet();
        var removedRelations = _project.Relations.Where(r =>
            (r.FromKind == "Content" && removedNodeIds.Contains(r.FromId)) ||
            (r.ToKind == "Content" && removedNodeIds.Contains(r.ToId)) ||
            (r.FromKind == "Entity" && removedEntityIds.Contains(r.FromId)) ||
            (r.ToKind == "Entity" && removedEntityIds.Contains(r.ToId))).ToList();

        _project.Materials.RemoveAt(index);
        _project.ContentNodes.RemoveAll(n => removedNodeIds.Contains(n.ContentId));
        _project.Entities.RemoveAll(e => removedEntityIds.Contains(e.EntityId));
        _project.Relations.RemoveAll(r => removedRelations.Contains(r));
        _project.BibleEntries.RemoveAll(b => removedEntityIds.Contains(b.SubjectEntityId));

        foreach (var entity in _project.Entities.Where(e => e.FirstSourceContentId.HasValue && removedNodeIds.Contains(e.FirstSourceContentId.Value)))
            entity.FirstSourceContentId = null;

        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews();
            _status.Text = $"Rimosso: {removed.FileName} · {removedNodes.Count} elementi e {removedEntities.Count} candidati collegati. Entità già confermate preservate.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Errore salvataggio dopo rimozione: {ex.Message}. Riapri il progetto per ripristinare lo stato salvato.";
        }
    }

    private async Task ConfirmSelectedEntityAsync()
    {
        var entity = GetSelectedEntity();
        if (_project is null || entity is null || string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            _status.Text = "Seleziona un'entità del Content Graph da confermare.";
            return;
        }

        if (!ContentGraphEngine.ConfirmEntity(_project, entity.EntityId)) return;
        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews(entity.EntityId);
            _status.Text = $"Confermato: {entity.Name}. Inserito nella Bible come nome canonico e tipo vincolante.";
        }
        catch (Exception ex) { _status.Text = $"Errore salvataggio Bible: {ex.Message}"; }
    }

    private async Task IgnoreSelectedEntityAsync()
    {
        var entity = GetSelectedEntity();
        if (_project is null || entity is null || string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            _status.Text = "Seleziona un'entità da ignorare.";
            return;
        }

        var name = entity.Name;
        if (!ContentGraphEngine.IgnoreEntity(_project, entity.EntityId)) return;
        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews();
            _status.Text = $"Ignorata entità candidata: {name}. Rimosse anche le relazioni e le eventuali voci Bible collegate.";
        }
        catch (Exception ex) { _status.Text = $"Errore salvataggio dopo esclusione: {ex.Message}"; }
    }

    private async Task SaveCurrentAsync()
    {
        if (_project is null || string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            _status.Text = "Nessun progetto aperto. Usa Nuovo progetto o Apri .diez.";
            return;
        }

        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews();
            _status.Text = $"Salvato: {_project.ContentNodes.Count} elementi · {_project.Entities.Count} entità · {_project.Relations.Count} relazioni · {_project.BibleEntries.Count(b => b.IsActive)} voci Bible";
        }
        catch (Exception ex) { _status.Text = $"Errore salvataggio: {ex.Message}"; }
    }

    private void RefreshViews(Guid? selectEntityId = null)
    {
        _preview.Text = string.Empty;
        if (_project is null)
        {
            _materialsList.ItemsSource = null;
            _structureList.ItemsSource = null;
            _entitiesList.ItemsSource = null;
            return;
        }

        _materialsList.ItemsSource = _project.Materials
            .Select(m => $"{(m.IsEmbedded ? "●" : "○")}  {m.Kind}  ·  {m.FileName}  ·  {m.Summary}")
            .ToList();

        _structureList.ItemsSource = GetOrderedNodes()
            .Select(n => $"{NodePrefix(n.Kind)}  {n.Title}  ·  {n.SourceLocator}")
            .ToList();

        var orderedEntities = GetOrderedEntities();
        _entitiesList.ItemsSource = orderedEntities
            .Select(e => $"{(e.IsCandidate ? "?" : "✓")}  {e.Kind}  ·  {e.Name}  ·  {_project.Relations.Count(r => (r.FromKind == "Entity" && r.FromId == e.EntityId) || (r.ToKind == "Entity" && r.ToId == e.EntityId))} relazioni")
            .ToList();

        if (selectEntityId.HasValue)
        {
            var selectedIndex = orderedEntities.FindIndex(e => e.EntityId == selectEntityId.Value);
            if (selectedIndex >= 0) _entitiesList.SelectedIndex = selectedIndex;
        }
    }

    private void ShowSelectedMaterial()
    {
        if (_project is null || _materialsList.SelectedIndex < 0 || _materialsList.SelectedIndex >= _project.Materials.Count) return;
        _structureList.SelectedIndex = -1;
        _entitiesList.SelectedIndex = -1;
        var material = _project.Materials[_materialsList.SelectedIndex];
        var shortHash = material.Sha256.Length > 16 ? material.Sha256[..16] : material.Sha256;
        _preview.Text =
            $"{material.FileName}\nTipo: {material.Kind}\nOrigine importazione: {material.SourcePath}\n" +
            $"Dimensione: {material.SizeBytes:N0} byte\nSHA-256: {shortHash}...\n" +
            $"Nel progetto: {(material.IsEmbedded ? "originale incorporato nel .diez" : "solo metadati")}\n" +
            $"Testo editoriale estratto: {(string.IsNullOrWhiteSpace(material.ExtractedText) ? "no" : "sì")}\n{material.Summary}\n\n{material.Preview}";
    }

    private void ShowSelectedContentNode()
    {
        if (_project is null || _structureList.SelectedIndex < 0) return;
        var ordered = GetOrderedNodes();
        if (_structureList.SelectedIndex >= ordered.Count) return;
        _materialsList.SelectedIndex = -1;
        _entitiesList.SelectedIndex = -1;
        var node = ordered[_structureList.SelectedIndex];
        var mentions = _project.Relations.Count(r => r.Type == "AppearsIn" && r.ToKind == "Content" && r.ToId == node.ContentId);
        _preview.Text = $"{node.Kind}: {node.Title}\nProvenienza: {node.SourceLocator}\nOrdine: {node.Ordinal}\nEntità collegate: {mentions}\n\n{node.Body}";
    }

    private void ShowSelectedEntity()
    {
        var entity = GetSelectedEntity();
        if (_project is null || entity is null) return;
        _materialsList.SelectedIndex = -1;
        _structureList.SelectedIndex = -1;

        var builder = new StringBuilder();
        builder.AppendLine($"{(entity.IsCandidate ? "CANDIDATO" : "CONFERMATO")} · {entity.Kind}: {entity.Name}");
        builder.AppendLine(entity.Notes);
        builder.AppendLine();
        builder.AppendLine("Relazioni:");
        foreach (var relation in _project.Relations.Where(r =>
                     (r.FromKind == "Entity" && r.FromId == entity.EntityId) ||
                     (r.ToKind == "Entity" && r.ToId == entity.EntityId)).Take(12))
        {
            builder.AppendLine($"- {(relation.IsCandidate ? "?" : "✓")} {DescribeEndpoint(relation.FromKind, relation.FromId)} —{relation.Type}→ {DescribeEndpoint(relation.ToKind, relation.ToId)}");
            if (!string.IsNullOrWhiteSpace(relation.Evidence)) builder.AppendLine($"  {relation.Evidence}");
        }

        var bible = _project.BibleEntries.Where(b => b.SubjectEntityId == entity.EntityId && b.IsActive).ToList();
        builder.AppendLine();
        builder.AppendLine("Bible:");
        if (bible.Count == 0) builder.AppendLine("- Nessuna voce canonica: conferma l'entità per promuoverla nella Bible.");
        else foreach (var entry in bible) builder.AppendLine($"- [{entry.Authority}] {entry.Key} = {entry.Value}");
        _preview.Text = builder.ToString().TrimEnd();
    }

    private GraphEntity? GetSelectedEntity()
    {
        if (_project is null || _entitiesList.SelectedIndex < 0) return null;
        var ordered = GetOrderedEntities();
        return _entitiesList.SelectedIndex < ordered.Count ? ordered[_entitiesList.SelectedIndex] : null;
    }

    private List<ContentNode> GetOrderedNodes() => _project is null ? [] : _project.ContentNodes
        .OrderBy(n => _project.Materials.FindIndex(m => m.MaterialId == n.MaterialId))
        .ThenBy(n => n.Ordinal)
        .ToList();

    private List<GraphEntity> GetOrderedEntities() => _project is null ? [] : _project.Entities
        .OrderBy(e => e.IsCandidate ? 1 : 0)
        .ThenBy(e => e.Kind, StringComparer.OrdinalIgnoreCase)
        .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private string DescribeEndpoint(string kind, Guid id)
    {
        if (_project is null) return id.ToString("N")[..8];
        if (kind == "Entity") return _project.Entities.FirstOrDefault(e => e.EntityId == id)?.Name ?? "entità mancante";
        if (kind == "Content") return _project.ContentNodes.FirstOrDefault(n => n.ContentId == id)?.Title ?? "contenuto mancante";
        return id.ToString("N")[..8];
    }

    private static string NodePrefix(string kind) => kind switch
    {
        "Document" => "DOC",
        "Part" => "PARTE",
        "Chapter" => "CAP",
        _ => "SEZ"
    };
}
