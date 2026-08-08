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
    private readonly ListBox _issuesList;
    private readonly TextBox _preview;
    private string? _currentProjectPath;
    private PreviewProject? _project;

    public MainWindow(string? startupProjectPath = null)
    {
        Title = "Diez Publishing Studio — 0.6 Preview";
        Width = 1200;
        Height = 980;
        MinWidth = 960;
        MinHeight = 760;

        var logo = new TextBlock { Text = "∞", FontSize = 36, HorizontalAlignment = HorizontalAlignment.Center };
        var title = new TextBlock { Text = "Diez Publishing Studio", FontSize = 27, HorizontalAlignment = HorizontalAlignment.Center };
        var subtitle = new TextBlock
        {
            Text = "Preview 0.6 — Consistency Review: rileva, valuta e registra senza modifiche automatiche al manoscritto",
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

        var reviewedButton = MakeSmallButton("Segna rivisto");
        reviewedButton.Click += async (_, _) => await ChangeSelectedIssueStatusAsync("Reviewed");
        var exceptionButton = MakeSmallButton("Accetta eccezione");
        exceptionButton.Click += async (_, _) => await ChangeSelectedIssueStatusAsync("AcceptedException");
        var resolvedButton = MakeSmallButton("Segna risolto");
        resolvedButton.Click += async (_, _) => await ChangeSelectedIssueStatusAsync("Resolved");
        var reopenButton = MakeSmallButton("Riapri");
        reopenButton.Click += async (_, _) => await ChangeSelectedIssueStatusAsync("Open");

        _status = new TextBlock
        {
            Text = "Pronto. I problemi di coerenza hanno ora uno stato umano persistente. Nessuna azione di revisione cambia automaticamente il testo.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 1040
        };

        _materialsList = new ListBox { Width = 1040, Height = 85 };
        _materialsList.SelectionChanged += (_, _) => ShowSelectedMaterial();
        _structureList = new ListBox { Width = 1040, Height = 105 };
        _structureList.SelectionChanged += (_, _) => ShowSelectedContentNode();
        _entitiesList = new ListBox { Width = 1040, Height = 105 };
        _entitiesList.SelectionChanged += (_, _) => ShowSelectedEntity();
        _issuesList = new ListBox { Width = 1040, Height = 120 };
        _issuesList.SelectionChanged += (_, _) => ShowSelectedIssue();

        _preview = new TextBox
        {
            Width = 1040,
            Height = 170,
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Seleziona materiale, struttura, entità o problema di coerenza."
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
        var reviewButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { reviewedButton, exceptionButton, resolvedButton, reopenButton }
        };

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 6,
                Children =
                {
                    logo, title, subtitle, projectButtons, _status,
                    MakeSectionLabel("Materiali incorporati"), _materialsList,
                    MakeSectionLabel("Struttura editoriale"), _structureList,
                    MakeSectionLabel("Content Graph / Bible"), _entitiesList, graphButtons,
                    MakeSectionLabel("Consistency Review"), _issuesList, reviewButtons,
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

    private static Button MakeSmallButton(string text) => new()
    {
        Content = text,
        Width = 160,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static TextBlock MakeSectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 16,
        HorizontalAlignment = HorizontalAlignment.Left,
        Width = 1040
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
            ConsistencyEngine.Rebuild(project);
            _currentProjectPath = path;
            _project = project;
            RefreshViews();
            _status.Text = wasPackage
                ? $"Aperto: {project.Name} · {project.Materials.Count} materiali · {project.Entities.Count} entità · {OpenIssueCount()} problemi aperti · {project.ConsistencyResolutions.Count} decisioni di revisione"
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

            var message = $"Importati {imported} materiali · {editorialNodes} elementi · {entities} nuove entità · {relations} nuove relazioni · {OpenIssueCount()} problemi aperti";
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
        ConsistencyEngine.Rebuild(_project);

        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews();
            _status.Text = $"Rimosso: {removed.FileName} · {removedNodes.Count} elementi e {removedEntities.Count} candidati collegati · {OpenIssueCount()} problemi aperti rimasti.";
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
            _status.Text = $"Confermato: {entity.Name}. Bible aggiornata · {EntityIssueCount(entity.EntityId)} problemi aperti collegati.";
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
            _status.Text = $"Ignorata entità candidata: {name}. Rimosse relazioni e voci Bible collegate.";
        }
        catch (Exception ex) { _status.Text = $"Errore salvataggio dopo esclusione: {ex.Message}"; }
    }

    private async Task ChangeSelectedIssueStatusAsync(string newStatus)
    {
        if (_project is null || string.IsNullOrWhiteSpace(_currentProjectPath)) return;
        var issue = GetSelectedIssue();
        if (issue is null)
        {
            _status.Text = "Seleziona prima un problema nella sezione Consistency Review.";
            return;
        }

        var changed = newStatus switch
        {
            "Reviewed" => ConsistencyReviewService.MarkReviewed(_project, issue.IssueId),
            "AcceptedException" => ConsistencyReviewService.AcceptException(_project, issue.IssueId),
            "Resolved" => ConsistencyReviewService.MarkResolved(_project, issue.IssueId),
            "Open" => ConsistencyReviewService.Reopen(_project, issue.IssueId),
            _ => false
        };
        if (!changed) return;

        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews(selectIssueId: issue.IssueId);
            _status.Text = $"Problema segnato come {StatusLabel(newStatus)}. Decisione registrata nel .diez; il manoscritto non è stato modificato.";
        }
        catch (Exception ex) { _status.Text = $"Errore salvataggio decisione di revisione: {ex.Message}"; }
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
            ConsistencyEngine.Rebuild(_project);
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews();
            _status.Text = $"Salvato: {_project.ContentNodes.Count} elementi · {_project.Entities.Count} entità · {_project.BibleEntries.Count(b => b.IsActive)} voci Bible · {OpenIssueCount()} problemi aperti · {_project.ConsistencyResolutions.Count} decisioni";
        }
        catch (Exception ex) { _status.Text = $"Errore salvataggio: {ex.Message}"; }
    }

    private void RefreshViews(Guid? selectEntityId = null, Guid? selectIssueId = null)
    {
        _preview.Text = string.Empty;
        if (_project is null)
        {
            _materialsList.ItemsSource = null;
            _structureList.ItemsSource = null;
            _entitiesList.ItemsSource = null;
            _issuesList.ItemsSource = null;
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
            .Select(e =>
            {
                var issueCount = EntityIssueCount(e.EntityId);
                var issueText = issueCount > 0 ? $" · ⚠ {issueCount}" : string.Empty;
                return $"{(e.IsCandidate ? "?" : "✓")}  {e.Kind}  ·  {e.Name}  ·  {_project.Relations.Count(r => (r.FromKind == "Entity" && r.FromId == e.EntityId) || (r.ToKind == "Entity" && r.ToId == e.EntityId))} relazioni{issueText}";
            })
            .ToList();

        var orderedIssues = GetOrderedIssues();
        _issuesList.ItemsSource = orderedIssues
            .Select(i => $"{IssueStatusSymbol(i.Status)}  [{i.Severity}]  {EntityName(i.SubjectEntityId)} · {i.Message}")
            .ToList();

        if (selectEntityId.HasValue)
        {
            var selectedIndex = orderedEntities.FindIndex(e => e.EntityId == selectEntityId.Value);
            if (selectedIndex >= 0) _entitiesList.SelectedIndex = selectedIndex;
        }
        if (selectIssueId.HasValue)
        {
            var selectedIndex = orderedIssues.FindIndex(i => i.IssueId == selectIssueId.Value);
            if (selectedIndex >= 0) _issuesList.SelectedIndex = selectedIndex;
        }
    }

    private void ShowSelectedMaterial()
    {
        if (_project is null || _materialsList.SelectedIndex < 0 || _materialsList.SelectedIndex >= _project.Materials.Count) return;
        ClearOtherSelections(_materialsList);
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
        ClearOtherSelections(_structureList);
        var node = ordered[_structureList.SelectedIndex];
        var mentions = _project.Relations.Count(r => r.Type == "AppearsIn" && r.ToKind == "Content" && r.ToId == node.ContentId);
        var consistencyReferences = _project.ConsistencyIssues.Count(i => i.Status == "Open" && i.ContentIds.Contains(node.ContentId));
        _preview.Text = $"{node.Kind}: {node.Title}\nProvenienza: {node.SourceLocator}\nOrdine: {node.Ordinal}\nEntità collegate: {mentions}\nProblemi aperti collegati: {consistencyReferences}\n\n{node.Body}";
    }

    private void ShowSelectedEntity()
    {
        var entity = GetSelectedEntity();
        if (_project is null || entity is null) return;
        ClearOtherSelections(_entitiesList);

        var builder = new StringBuilder();
        builder.AppendLine($"{(entity.IsCandidate ? "CANDIDATO" : "CONFERMATO")} · {entity.Kind}: {entity.Name}");
        builder.AppendLine(entity.Notes);
        builder.AppendLine();
        builder.AppendLine("Relazioni:");
        foreach (var relation in _project.Relations.Where(r =>
                     (r.FromKind == "Entity" && r.FromId == entity.EntityId) ||
                     (r.ToKind == "Entity" && r.ToId == entity.EntityId)).Take(10))
        {
            builder.AppendLine($"- {(relation.IsCandidate ? "?" : "✓")} {DescribeEndpoint(relation.FromKind, relation.FromId)} —{relation.Type}→ {DescribeEndpoint(relation.ToKind, relation.ToId)}");
        }

        var bible = _project.BibleEntries.Where(b => b.SubjectEntityId == entity.EntityId && b.IsActive).ToList();
        builder.AppendLine();
        builder.AppendLine("Bible:");
        if (bible.Count == 0) builder.AppendLine("- Nessuna voce canonica: conferma l'entità per promuoverla nella Bible.");
        else foreach (var entry in bible) builder.AppendLine($"- [{entry.Authority}] {entry.Key} = {entry.Value}");

        var issues = _project.ConsistencyIssues
            .Where(i => i.SubjectEntityId == entity.EntityId)
            .OrderBy(i => IssueStatusRank(i.Status))
            .ThenBy(i => SeverityRank(i.Severity))
            .ToList();
        builder.AppendLine();
        builder.AppendLine("Coerenza:");
        if (entity.IsCandidate) builder.AppendLine("- Il controllo completo parte quando l'entità viene confermata.");
        else if (issues.Count == 0) builder.AppendLine("- Nessuna contraddizione rilevata dalle regole attive.");
        else foreach (var issue in issues.Take(8)) builder.AppendLine($"- [{StatusLabel(issue.Status)} / {issue.Severity}] {issue.Message}");

        _preview.Text = builder.ToString().TrimEnd();
    }

    private void ShowSelectedIssue()
    {
        var issue = GetSelectedIssue();
        if (_project is null || issue is null) return;
        ClearOtherSelections(_issuesList);

        var builder = new StringBuilder();
        builder.AppendLine($"{IssueStatusSymbol(issue.Status)} {StatusLabel(issue.Status)} · {issue.Severity} · {issue.Code}");
        builder.AppendLine(issue.Message);
        builder.AppendLine($"Entità: {EntityName(issue.SubjectEntityId)}");
        builder.AppendLine($"Campo: {issue.Key}");
        builder.AppendLine();
        builder.AppendLine("Fonti / evidenze:");
        foreach (var contentId in issue.ContentIds)
        {
            var node = _project.ContentNodes.FirstOrDefault(n => n.ContentId == contentId);
            if (node is null) continue;
            builder.AppendLine($"- {node.Title} · {node.SourceLocator}");
            foreach (var fact in _project.ConsistencyFacts.Where(f => f.ContentId == contentId && f.SubjectEntityId == issue.SubjectEntityId && f.Key == issue.Key))
                builder.AppendLine($"  {fact.Key} = {fact.Value} · {fact.Evidence}");
        }

        var history = _project.ConsistencyResolutions
            .Where(r => r.IssueId == issue.IssueId || (!string.IsNullOrWhiteSpace(issue.Signature) && r.IssueSignature == issue.Signature))
            .OrderBy(r => r.CreatedAtLocal, StringComparer.Ordinal)
            .ToList();
        builder.AppendLine();
        builder.AppendLine("Decisioni umane:");
        if (history.Count == 0) builder.AppendLine("- Nessuna decisione registrata.");
        else foreach (var resolution in history.TakeLast(8))
            builder.AppendLine($"- {resolution.PreviousStatus} → {resolution.NewStatus} · {resolution.CreatedAtLocal}");

        builder.AppendLine();
        builder.AppendLine("Le azioni di revisione cambiano solo lo stato del problema: il testo sorgente resta intatto.");
        _preview.Text = builder.ToString().TrimEnd();
    }

    private void ClearOtherSelections(ListBox keep)
    {
        if (!ReferenceEquals(keep, _materialsList)) _materialsList.SelectedIndex = -1;
        if (!ReferenceEquals(keep, _structureList)) _structureList.SelectedIndex = -1;
        if (!ReferenceEquals(keep, _entitiesList)) _entitiesList.SelectedIndex = -1;
        if (!ReferenceEquals(keep, _issuesList)) _issuesList.SelectedIndex = -1;
    }

    private GraphEntity? GetSelectedEntity()
    {
        if (_project is null || _entitiesList.SelectedIndex < 0) return null;
        var ordered = GetOrderedEntities();
        return _entitiesList.SelectedIndex < ordered.Count ? ordered[_entitiesList.SelectedIndex] : null;
    }

    private ConsistencyIssue? GetSelectedIssue()
    {
        if (_project is null || _issuesList.SelectedIndex < 0) return null;
        var ordered = GetOrderedIssues();
        return _issuesList.SelectedIndex < ordered.Count ? ordered[_issuesList.SelectedIndex] : null;
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

    private List<ConsistencyIssue> GetOrderedIssues() => _project is null ? [] : _project.ConsistencyIssues
        .OrderBy(i => IssueStatusRank(i.Status))
        .ThenBy(i => SeverityRank(i.Severity))
        .ThenBy(i => EntityName(i.SubjectEntityId), StringComparer.OrdinalIgnoreCase)
        .ThenBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private int OpenIssueCount() => _project?.ConsistencyIssues.Count(i => i.Status == "Open") ?? 0;

    private int EntityIssueCount(Guid entityId) => _project?.ConsistencyIssues.Count(i =>
        i.Status == "Open" && i.SubjectEntityId == entityId) ?? 0;

    private string EntityName(Guid? entityId)
    {
        if (_project is null || !entityId.HasValue) return "progetto";
        return _project.Entities.FirstOrDefault(e => e.EntityId == entityId.Value)?.Name ?? "entità mancante";
    }

    private static int IssueStatusRank(string status) => status switch
    {
        "Open" => 0,
        "Reviewed" => 1,
        "AcceptedException" => 2,
        "Resolved" => 3,
        _ => 4
    };

    private static int SeverityRank(string severity) => severity switch
    {
        "Critical" => 0,
        "Error" => 1,
        "Warning" => 2,
        _ => 3
    };

    private static string StatusLabel(string status) => status switch
    {
        "Open" => "aperto",
        "Reviewed" => "rivisto",
        "AcceptedException" => "eccezione accettata",
        "Resolved" => "risolto",
        _ => status
    };

    private static string IssueStatusSymbol(string status) => status switch
    {
        "Open" => "⚠",
        "Reviewed" => "◐",
        "AcceptedException" => "≈",
        "Resolved" => "✓",
        _ => "?"
    };

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
