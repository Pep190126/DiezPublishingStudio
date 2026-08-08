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
        Title = "Diez Publishing Studio — 0.8 Preview";
        Width = 1200;
        Height = 1000;
        MinWidth = 960;
        MinHeight = 780;

        var logo = new TextBlock { Text = "∞", FontSize = 36, HorizontalAlignment = HorizontalAlignment.Center };
        var title = new TextBlock { Text = "Diez Publishing Studio", FontSize = 27, HorizontalAlignment = HorizontalAlignment.Center };
        var subtitle = new TextBlock
        {
            Text = "Preview 0.8 — Editable Master: modifica il contenuto senza sovrascrivere gli originali importati",
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

        var editMasterButton = MakeSmallButton("Modifica Master");
        editMasterButton.Click += async (_, _) => await EditSelectedContentAsync();
        var restoreImportedButton = MakeSmallButton("Ripristina importato");
        restoreImportedButton.Click += async (_, _) => await RestoreSelectedContentAsync();

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

        var proposeButton = MakeSmallButton("Crea proposta");
        proposeButton.Click += async (_, _) => await CreateRevisionCandidateAsync();
        var approveButton = MakeSmallButton("Approva proposta");
        approveButton.Click += async (_, _) => await ChangeRevisionCandidateStatusAsync("Approved");
        var rejectButton = MakeSmallButton("Scarta proposta");
        rejectButton.Click += async (_, _) => await ChangeRevisionCandidateStatusAsync("Rejected");
        var applyButton = MakeSmallButton("Applica approvata");
        applyButton.Click += async (_, _) => await ApplyRevisionCandidateAsync();

        _status = new TextBlock
        {
            Text = "Pronto. Il Master è modificabile; gli originali importati restano incorporati e intatti nel .diez.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 1040
        };

        _materialsList = new ListBox { Width = 1040, Height = 70 };
        _materialsList.SelectionChanged += (_, _) => ShowSelectedMaterial();
        _structureList = new ListBox { Width = 1040, Height = 92 };
        _structureList.SelectionChanged += (_, _) => ShowSelectedContentNode();
        _entitiesList = new ListBox { Width = 1040, Height = 85 };
        _entitiesList.SelectionChanged += (_, _) => ShowSelectedEntity();
        _issuesList = new ListBox { Width = 1040, Height = 108 };
        _issuesList.SelectionChanged += (_, _) => ShowSelectedIssue();

        _preview = new TextBox
        {
            Width = 1040,
            Height = 165,
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Seleziona materiale, contenuto, entità o problema di coerenza."
        };

        var projectButtons = Row(newButton, openButton, importButton, removeButton, saveButton);
        var masterButtons = Row(editMasterButton, restoreImportedButton);
        var graphButtons = Row(confirmEntityButton, ignoreEntityButton);
        var reviewButtons = Row(reviewedButton, exceptionButton, resolvedButton, reopenButton);
        var revisionButtons = Row(proposeButton, approveButton, rejectButton, applyButton);

        Content = new Border
        {
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 4,
                Children =
                {
                    logo, title, subtitle, projectButtons, _status,
                    MakeSectionLabel("Materiali incorporati"), _materialsList,
                    MakeSectionLabel("Editable Master / Struttura editoriale"), _structureList, masterButtons,
                    MakeSectionLabel("Content Graph / Bible"), _entitiesList, graphButtons,
                    MakeSectionLabel("Consistency Review / Revision Candidate"), _issuesList, reviewButtons, revisionButtons,
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

    private static StackPanel Row(params Control[] controls) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        HorizontalAlignment = HorizontalAlignment.Center,
        Children = { controls }
    };

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
                ? $"Aperto: {project.Name} · {project.Materials.Count} materiali · {project.ContentNodes.Count} contenuti · {ManualRevisionTotal()} revisioni Master · {OpenIssueCount()} problemi aperti"
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

        ConsistencyEngine.Rebuild(_project);
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

        var removed = _project.Materials[_materialsList.SelectedIndex];
        var removedNodes = _project.ContentNodes.Where(n => n.MaterialId == removed.MaterialId).ToList();
        var removedNodeIds = removedNodes.Select(n => n.ContentId).ToHashSet();
        var removedEntities = _project.Entities.Where(e => e.SourceMaterialId == removed.MaterialId && e.IsCandidate).ToList();
        var removedEntityIds = removedEntities.Select(e => e.EntityId).ToHashSet();

        _project.Materials.Remove(removed);
        _project.ContentNodes.RemoveAll(n => removedNodeIds.Contains(n.ContentId));
        _project.Entities.RemoveAll(e => removedEntityIds.Contains(e.EntityId));
        _project.Relations.RemoveAll(r =>
            (r.FromKind == "Content" && removedNodeIds.Contains(r.FromId)) ||
            (r.ToKind == "Content" && removedNodeIds.Contains(r.ToId)) ||
            (r.FromKind == "Entity" && removedEntityIds.Contains(r.FromId)) ||
            (r.ToKind == "Entity" && removedEntityIds.Contains(r.ToId)));
        _project.BibleEntries.RemoveAll(b => removedEntityIds.Contains(b.SubjectEntityId));
        _project.RevisionCandidates.RemoveAll(c => removedNodeIds.Contains(c.ContentId) || removedEntityIds.Contains(c.SubjectEntityId));

        foreach (var entity in _project.Entities.Where(e => e.FirstSourceContentId.HasValue && removedNodeIds.Contains(e.FirstSourceContentId.Value)))
            entity.FirstSourceContentId = null;
        ConsistencyEngine.Rebuild(_project);

        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews();
            _status.Text = $"Rimosso: {removed.FileName} · {removedNodes.Count} elementi e {removedEntities.Count} candidati collegati.";
        }
        catch (Exception ex) { _status.Text = $"Errore salvataggio dopo rimozione: {ex.Message}. Riapri il progetto per ripristinare lo stato salvato."; }
    }

    private async Task EditSelectedContentAsync()
    {
        if (_project is null || string.IsNullOrWhiteSpace(_currentProjectPath)) return;
        var node = GetSelectedContentNode();
        if (node is null)
        {
            _status.Text = "Seleziona prima un capitolo o una sezione nella Struttura editoriale.";
            return;
        }
        if (!EditableMasterService.CanEdit(_project, node))
        {
            _status.Text = "Questo nodo è strutturale. Seleziona un capitolo o una sezione modificabile.";
            return;
        }

        var editor = new ContentEditorWindow(node, EditableMasterService.ManualRevisionCount(_project, node.ContentId));
        var edited = await editor.ShowDialog<string?>(this);
        if (edited is null) return;

        var result = EditableMasterService.ApplyManualEdit(_project, node.ContentId, edited);
        if (!result.Changed)
        {
            _status.Text = result.Message;
            return;
        }

        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews(selectContentId: node.ContentId);
            _status.Text = $"{result.Message} Revisioni Master registrate: {EditableMasterService.ManualRevisionCount(_project, node.ContentId)}.";
        }
        catch (Exception ex) { _status.Text = $"La modifica è in memoria ma il salvataggio è fallito: {ex.Message}. Riapri il progetto prima di continuare."; }
    }

    private async Task RestoreSelectedContentAsync()
    {
        if (_project is null || string.IsNullOrWhiteSpace(_currentProjectPath)) return;
        var node = GetSelectedContentNode();
        if (node is null)
        {
            _status.Text = "Seleziona prima il contenuto da ripristinare.";
            return;
        }

        var result = EditableMasterService.RestoreImportedSnapshot(_project, node.ContentId);
        if (!result.Changed)
        {
            _status.Text = result.Message;
            return;
        }

        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews(selectContentId: node.ContentId);
            _status.Text = "Contenuto ripristinato dallo snapshot importato. Il ripristino è stato registrato come nuova revisione del Master.";
        }
        catch (Exception ex) { _status.Text = $"Ripristino in memoria riuscito ma salvataggio fallito: {ex.Message}."; }
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
            RefreshViews(selectEntityId: entity.EntityId);
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
        var entityId = entity.EntityId;
        if (!ContentGraphEngine.IgnoreEntity(_project, entityId)) return;
        _project.RevisionCandidates.RemoveAll(c => c.SubjectEntityId == entityId);
        ConsistencyEngine.Rebuild(_project);
        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews();
            _status.Text = $"Ignorata entità candidata: {name}. Rimosse relazioni, voci Bible e proposte collegate.";
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
            _status.Text = $"Problema segnato come {StatusLabel(newStatus)}. Decisione registrata; il Master non è stato modificato.";
        }
        catch (Exception ex) { _status.Text = $"Errore salvataggio decisione di revisione: {ex.Message}"; }
    }

    private async Task CreateRevisionCandidateAsync()
    {
        if (_project is null || string.IsNullOrWhiteSpace(_currentProjectPath)) return;
        var issue = GetSelectedIssue();
        if (issue is null)
        {
            _status.Text = "Seleziona prima un problema di coerenza da trasformare in proposta.";
            return;
        }

        var result = RevisionCandidateService.CreateForIssue(_project, issue.IssueId);
        if (result.Candidate is null)
        {
            _status.Text = result.Message;
            return;
        }

        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews(selectIssueId: issue.IssueId);
            _status.Text = result.Message;
        }
        catch (Exception ex) { _status.Text = $"Errore salvataggio proposta: {ex.Message}"; }
    }

    private async Task ChangeRevisionCandidateStatusAsync(string newStatus)
    {
        if (_project is null || string.IsNullOrWhiteSpace(_currentProjectPath)) return;
        var issue = GetSelectedIssue();
        if (issue is null)
        {
            _status.Text = "Seleziona il problema a cui appartiene la proposta.";
            return;
        }

        var candidate = GetLatestCandidate(issue);
        if (candidate is null)
        {
            _status.Text = "Non esiste ancora una proposta per questo problema. Usa Crea proposta.";
            return;
        }

        var changed = newStatus switch
        {
            "Approved" => RevisionCandidateService.Approve(_project, candidate.CandidateId),
            "Rejected" => RevisionCandidateService.Reject(_project, candidate.CandidateId),
            _ => false
        };
        if (!changed)
        {
            _status.Text = $"La proposta è già nello stato {CandidateStatusLabel(candidate.Status)} o non può effettuare questa transizione.";
            return;
        }

        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews(selectIssueId: issue.IssueId);
            _status.Text = newStatus == "Approved"
                ? "Proposta approvata. Il Master non è ancora cambiato: serve Applica approvata."
                : "Proposta scartata. Il Master non è stato modificato.";
        }
        catch (Exception ex) { _status.Text = $"Errore salvataggio stato proposta: {ex.Message}"; }
    }

    private async Task ApplyRevisionCandidateAsync()
    {
        if (_project is null || string.IsNullOrWhiteSpace(_currentProjectPath)) return;
        var issue = GetSelectedIssue();
        if (issue is null)
        {
            _status.Text = "Seleziona il problema a cui appartiene la proposta approvata.";
            return;
        }

        var candidate = GetLatestCandidate(issue);
        if (candidate is null)
        {
            _status.Text = "Non esiste una proposta per questo problema.";
            return;
        }

        var result = RevisionCandidateService.ApplyApproved(_project, candidate.CandidateId);
        if (!result.Applied)
        {
            _status.Text = result.Message;
            return;
        }

        try
        {
            await ProjectFileStore.SaveAsync(_currentProjectPath, _project);
            RefreshViews(selectContentId: candidate.ContentId);
            _status.Text = $"{result.Message} Problemi aperti rimasti: {OpenIssueCount()}.";
        }
        catch (Exception ex) { _status.Text = $"La proposta è stata applicata in memoria ma il salvataggio è fallito: {ex.Message}. Riapri il progetto prima di continuare."; }
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
            _status.Text = $"Salvato: {_project.ContentNodes.Count} contenuti · {ManualRevisionTotal()} revisioni Master · {_project.Entities.Count} entità · {OpenIssueCount()} problemi aperti · {ProposalCount()} proposte";
        }
        catch (Exception ex) { _status.Text = $"Errore salvataggio: {ex.Message}"; }
    }

    private void RefreshViews(Guid? selectEntityId = null, Guid? selectIssueId = null, Guid? selectContentId = null)
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

        var orderedNodes = GetOrderedNodes();
        _structureList.ItemsSource = orderedNodes
            .Select(n =>
            {
                var revisions = EditableMasterService.ManualRevisionCount(_project, n.ContentId);
                var editable = EditableMasterService.CanEdit(_project, n) ? "✎" : "·";
                var history = revisions > 0 ? $" · rev {revisions}" : string.Empty;
                return $"{editable} {NodePrefix(n.Kind)}  {n.Title}  ·  {n.SourceLocator}{history}";
            })
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
            .Select(i =>
            {
                var candidate = GetLatestCandidate(i);
                var candidateText = candidate is null ? string.Empty : $" · proposta {CandidateStatusLabel(candidate.Status)}";
                return $"{IssueStatusSymbol(i.Status)}  [{i.Severity}]  {EntityName(i.SubjectEntityId)} · {i.Message}{candidateText}";
            })
            .ToList();

        if (selectContentId.HasValue)
        {
            var index = orderedNodes.FindIndex(n => n.ContentId == selectContentId.Value);
            if (index >= 0) _structureList.SelectedIndex = index;
        }
        if (selectEntityId.HasValue)
        {
            var index = orderedEntities.FindIndex(e => e.EntityId == selectEntityId.Value);
            if (index >= 0) _entitiesList.SelectedIndex = index;
        }
        if (selectIssueId.HasValue)
        {
            var index = orderedIssues.FindIndex(i => i.IssueId == selectIssueId.Value);
            if (index >= 0) _issuesList.SelectedIndex = index;
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
            $"Snapshot testuale: {(string.IsNullOrWhiteSpace(material.ExtractedText) ? "no" : "sì")}\n{material.Summary}\n\n{material.Preview}";
    }

    private void ShowSelectedContentNode()
    {
        var node = GetSelectedContentNode();
        if (_project is null || node is null) return;
        ClearOtherSelections(_structureList);
        var mentions = _project.Relations.Count(r => r.Type == "AppearsIn" && r.ToKind == "Content" && r.ToId == node.ContentId);
        var consistencyReferences = _project.ConsistencyIssues.Count(i => i.Status == "Open" && i.ContentIds.Contains(node.ContentId));
        var revisions = EditableMasterService.ManualHistory(_project, node.ContentId);
        var candidateCount = _project.RevisionCandidates.Count(c => c.ContentId == node.ContentId && c.Key != "manual_edit");
        var builder = new StringBuilder();
        builder.AppendLine($"{node.Kind}: {node.Title}");
        builder.AppendLine($"Provenienza: {node.SourceLocator}");
        builder.AppendLine($"Modificabile nel Master: {(EditableMasterService.CanEdit(_project, node) ? "sì" : "no, nodo strutturale")}");
        builder.AppendLine($"Revisioni manuali: {revisions.Count} · proposte: {candidateCount} · problemi aperti: {consistencyReferences} · entità collegate: {mentions}");
        if (revisions.Count > 0)
        {
            var latest = revisions[^1];
            builder.AppendLine($"Ultima revisione: {latest.AppliedAtLocal} · {latest.Rationale}");
        }
        builder.AppendLine();
        builder.AppendLine(node.Body);
        _preview.Text = builder.ToString().TrimEnd();
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
        builder.AppendLine("Bible:");
        var bible = _project.BibleEntries.Where(b => b.SubjectEntityId == entity.EntityId && b.IsActive).ToList();
        if (bible.Count == 0) builder.AppendLine("- Nessuna voce canonica.");
        else foreach (var entry in bible) builder.AppendLine($"- [{entry.Authority}] {entry.Key} = {entry.Value}");
        builder.AppendLine();
        builder.AppendLine("Coerenza:");
        var issues = _project.ConsistencyIssues.Where(i => i.SubjectEntityId == entity.EntityId).OrderBy(i => IssueStatusRank(i.Status)).ThenBy(i => SeverityRank(i.Severity)).Take(8).ToList();
        if (issues.Count == 0) builder.AppendLine("- Nessuna contraddizione rilevata dalle regole attive.");
        else foreach (var issue in issues) builder.AppendLine($"- [{StatusLabel(issue.Status)} / {issue.Severity}] {issue.Message}");
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
        builder.AppendLine($"Entità: {EntityName(issue.SubjectEntityId)} · campo: {issue.Key}");
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
        else foreach (var resolution in history.TakeLast(6)) builder.AppendLine($"- {resolution.PreviousStatus} → {resolution.NewStatus} · {resolution.CreatedAtLocal}");

        var candidate = GetLatestCandidate(issue);
        builder.AppendLine();
        builder.AppendLine("Revision Candidate:");
        if (candidate is null)
        {
            builder.AppendLine("- Nessuna proposta. Crea proposta prepara una modifica separata dal Master.");
        }
        else
        {
            builder.AppendLine($"- Stato: {CandidateStatusLabel(candidate.Status)} · {candidate.OriginalValue} → {candidate.ProposedValue}");
            builder.AppendLine($"- Motivo: {candidate.Rationale}");
            builder.AppendLine("PRIMA: " + TrimForPreview(candidate.OriginalBody));
            builder.AppendLine("DOPO: " + TrimForPreview(candidate.ProposedBody));
        }
        _preview.Text = builder.ToString().TrimEnd();
    }

    private void ClearOtherSelections(ListBox keep)
    {
        if (!ReferenceEquals(keep, _materialsList)) _materialsList.SelectedIndex = -1;
        if (!ReferenceEquals(keep, _structureList)) _structureList.SelectedIndex = -1;
        if (!ReferenceEquals(keep, _entitiesList)) _entitiesList.SelectedIndex = -1;
        if (!ReferenceEquals(keep, _issuesList)) _issuesList.SelectedIndex = -1;
    }

    private ContentNode? GetSelectedContentNode()
    {
        if (_project is null || _structureList.SelectedIndex < 0) return null;
        var ordered = GetOrderedNodes();
        return _structureList.SelectedIndex < ordered.Count ? ordered[_structureList.SelectedIndex] : null;
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

    private RevisionCandidate? GetLatestCandidate(ConsistencyIssue issue)
    {
        if (_project is null) return null;
        return _project.RevisionCandidates
            .Where(c => c.Key != "manual_edit" && (c.IssueId == issue.IssueId || (!string.IsNullOrWhiteSpace(issue.Signature) && c.IssueSignature == issue.Signature)))
            .OrderByDescending(c => c.CreatedAtLocal, StringComparer.Ordinal)
            .FirstOrDefault();
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
    private int ManualRevisionTotal() => _project?.RevisionCandidates.Count(c => c.Key == "manual_edit" && c.Status == "Applied") ?? 0;
    private int ProposalCount() => _project?.RevisionCandidates.Count(c => c.Key != "manual_edit") ?? 0;
    private int EntityIssueCount(Guid entityId) => _project?.ConsistencyIssues.Count(i => i.Status == "Open" && i.SubjectEntityId == entityId) ?? 0;

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

    private static string CandidateStatusLabel(string status) => status switch
    {
        "Proposed" => "proposta",
        "Approved" => "approvata",
        "Applied" => "applicata",
        "Rejected" => "scartata",
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

    private static string TrimForPreview(string value)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= 450 ? clean : clean[..447] + "...";
    }

    private static string NodePrefix(string kind) => kind switch
    {
        "Document" => "DOC",
        "Part" => "PARTE",
        "Chapter" => "CAP",
        _ => "SEZ"
    };
}
