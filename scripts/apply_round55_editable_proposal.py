from pathlib import Path


def replace_once(path: str, old: str, new: str):
    p = Path(path)
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected exactly one match, found {count}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')


def replace_between(path: str, start: str, end: str, new: str):
    p = Path(path)
    text = p.read_text(encoding='utf-8')
    a = text.find(start)
    if a < 0:
        raise SystemExit(f'{path}: start marker not found')
    b = text.find(end, a)
    if b < 0:
        raise SystemExit(f'{path}: end marker not found')
    p.write_text(text[:a] + new + text[b:], encoding='utf-8')

planner = 'src/Diez.Core/DiezVisualSubjectPlannerFrontendBridge.cs'

replace_once(planner,
'''public sealed record DiezVisualSubjectProposalItemDto(
    string DisplayName,
    string CanonicalConcept,
    string Description,
    string CanonicalDescription);
''',
'''public sealed record DiezVisualSubjectProposalItemDto(
    string DisplayName,
    string CanonicalConcept,
    string Description,
    string CanonicalDescription);

public sealed record DiezVisualSubjectProposalUserEditDto(
    string DisplayName,
    string Description);
''')

replace_once(planner,
'''        if (!currentState.Required)
            return Mutation(projectJson, "NOT_REQUIRED", "Il piano soggetti è già risolto in Diez: non serve una nuova proposta AI.");
        if (currentState.ProposalValid)
''',
'''        if (!currentState.Required)
            return Mutation(projectJson, "NOT_REQUIRED", "Il piano soggetti è già risolto in Diez: non serve una nuova proposta AI.");
        if (string.Equals(currentState.ProposalStatus, "SEMANTIC_RECONCILIATION_PENDING", StringComparison.OrdinalIgnoreCase))
            return Mutation(projectJson, "REVISION_PENDING", "Le modifiche utente che cambiano il significato sono già in attesa di riconciliazione semantica. Completa l'attività planner esistente; Diez non crea duplicati.");
        if (currentState.ProposalValid)
''')

insert_marker = '''    public static DiezVisualSubjectPlannerMutation ApplyProposal(string projectJson, Guid versionId)
'''
new_methods = '''    public static async Task<DiezVisualSubjectPlannerMutation> SaveUserRevisionAsync(
        string projectJson,
        Guid versionId,
        IEnumerable<DiezVisualSubjectProposalUserEditDto>? edits,
        bool semanticChange)
    {
        var (_, project) = Parse(projectJson);
        var exchange = AiExchangeStateStore.Load(project);
        var version = exchange.Versions.FirstOrDefault(x => x.VersionId == versionId);
        var unit = version is null ? null : exchange.WorkUnits.FirstOrDefault(x => x.WorkUnitId == version.WorkUnitId);
        var legacyJob = unit?.LegacyAiJobId is Guid legacyId
            ? project.AiProductionJobs.FirstOrDefault(x => x.JobId == legacyId)
            : null;
        if (version is null || unit is null || legacyJob is null || !IsPlannerJob(legacyJob))
            return Mutation(projectJson, "NOT_FOUND", "La proposta da modificare non appartiene al planner soggetti Diez.");

        var setup = ProjectSetup(project);
        if (!TryParseProposal(version.TextContent, setup.ImageCount, out var original, out var validation))
            return Mutation(projectJson, "INVALID_PROPOSAL", validation);

        var userEdits = (edits ?? []).ToList();
        if (userEdits.Count != original.Count)
            return Mutation(projectJson, "INVALID_EDIT", $"La revisione contiene {userEdits.Count} soggetti, ma la proposta ne contiene {original.Count}.");

        for (var i = 0; i < userEdits.Count; i++)
        {
            userEdits[i] = new DiezVisualSubjectProposalUserEditDto(
                (userEdits[i].DisplayName ?? string.Empty).Trim(),
                (userEdits[i].Description ?? string.Empty).Trim());
            if (!VisualSemanticResolutionGuard.IsConcrete(userEdits[i].DisplayName))
                return Mutation(projectJson, "INVALID_EDIT", $"Il soggetto {i + 1} deve avere un nome concreto e riconoscibile.");
        }
        if (userEdits.Select(x => x.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != userEdits.Count)
            return Mutation(projectJson, "INVALID_EDIT", "I soggetti modificati devono restare distinti.");

        if (!semanticChange)
        {
            var revised = original.Select((item, index) => new DiezVisualSubjectProposalItemDto(
                userEdits[index].DisplayName,
                item.CanonicalConcept,
                userEdits[index].Description,
                item.CanonicalDescription)).ToList();
            var ingest = await DiezAiExchangeBridge.IngestTextResultAsync(
                projectJson,
                unit.WorkUnitId,
                ProposalJson(revised),
                resultStatus: "COMPLETE");
            if (ingest.Status is not ("IMPORTED" or "UPDATED" or "DUPLICATE"))
                return Mutation(projectJson, "BLOCKED", ingest.Message);
            return Mutation(
                ingest.ProjectJson,
                "EDITORIAL_REVISED",
                "Modifiche editoriali salvate come nuova Candidate. Il soggetto/significato è dichiarato invariato, quindi la semantica tecnica resta quella già validata. Controlla di nuovo la proposta prima di accettarla.");
        }

        var draft = UserRevisionDraftJson(userEdits);
        var savedDraft = await DiezAiExchangeBridge.IngestTextResultAsync(
            projectJson,
            unit.WorkUnitId,
            draft,
            resultStatus: "INCOMPLETE");
        if (savedDraft.Status is "INVALID" or "CONFLICT")
            return Mutation(projectJson, "BLOCKED", savedDraft.Message);

        var (_, revisedProject) = Parse(savedDraft.ProjectJson);
        var revisedSetup = ProjectSetup(revisedProject);
        var prompt = BuildReconciliationPrompt(revisedSetup, ExtractTheme(revisedSetup.Subject), userEdits);
        var existing = revisedProject.AiProductionJobs
            .Where(IsPlannerJob)
            .OrderByDescending(x => x.CreatedAtLocal, StringComparer.Ordinal)
            .ThenByDescending(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => string.Equals((x.Prompt ?? string.Empty).Trim(), prompt, StringComparison.Ordinal));
        if (existing is not null)
            return Mutation(savedDraft.ProjectJson, "REVISION_PENDING", $"La riconciliazione semantica {existing.Code} è già pronta. Completa quella attività; Diez non crea duplicati.");

        var prepared = DiezAiExchangeBridge.CreateReadyJob(
            savedDraft.ProjectJson,
            PlannerTitle,
            AiProductionService.TypeText,
            prompt);
        return Mutation(
            prepared.ProjectJson,
            "REVISION_PREPARED",
            "Le tue modifiche cambiano il soggetto/significato: la vecchia semantica AI è stata invalidata. Diez ha preparato una riconciliazione TEXT che deve preservare esattamente i soggetti italiani modificati e rigenerare solo i campi canonici tecnici. Importa la risposta e poi ricontrolla la proposta prima di accettarla.");
    }

'''
replace_once(planner, insert_marker, new_methods + insert_marker)

# Protect ApplyProposal from a stale canonical response when a semantic user draft exists.
replace_once(planner,
'''        if (!TryParseProposal(version.TextContent, setup.ImageCount, out var proposal, out var validation))
            return Mutation(projectJson, "INVALID_PROPOSAL", validation);

        // Explicit user acceptance also approves this non-image planning candidate in AI Exchange.
''',
'''        if (!TryParseProposal(version.TextContent, setup.ImageCount, out var proposal, out var validation))
            return Mutation(projectJson, "INVALID_PROPOSAL", validation);

        var pendingDraft = LatestUserRevisionDraft(originalProject, originalState, setup.ImageCount);
        if (pendingDraft.Count > 0 && !ReconciliationSatisfied(originalProject, originalState, unit, pendingDraft))
            return Mutation(projectJson, "STALE_SEMANTICS", "Le modifiche utente cambiano ancora il significato e non sono state riconciliate semanticamente. Completa il planner di revisione prima di accettare.");

        // Explicit user acceptance also approves this non-image planning candidate in AI Exchange.
''')

# Replace State method with draft-aware version.
start = '    private static DiezVisualSubjectPlannerStateDto State(PreviewProject project)\n'
end = '    private static DiezVisualBookSetupDto ProjectSetup(PreviewProject project)\n'
new_state = '''    private static DiezVisualSubjectPlannerStateDto State(PreviewProject project)
    {
        var setup = ProjectSetup(project);
        var aggregate = VisualSemanticResolutionGuard.LooksAggregateSubject(setup.Subject);
        var multi = MultiSubjectProfileService.Load(project);
        var activeSubjects = multi.Enabled ? MultiSubjectProfileService.ActiveSubjects(multi) : [];
        var resolvedStructured = multi.Enabled &&
            activeSubjects.Count == setup.ImageCount &&
            activeSubjects.All(x => VisualSemanticResolutionGuard.IsConcrete(x.CanonicalConcept) ||
                                    VisualSemanticResolutionGuard.IsConcrete(x.Name));
        var required = aggregate && !resolvedStructured;
        var theme = aggregate ? ExtractTheme(setup.Subject) : string.Empty;
        var plannerJobs = project.AiProductionJobs
            .Where(IsPlannerJob)
            .OrderBy(x => x.CreatedAtLocal, StringComparer.Ordinal)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var plannerJob = plannerJobs.LastOrDefault();

        Guid? workUnitId = null;
        Guid? versionId = null;
        string proposalStatus = string.Empty;
        IReadOnlyList<DiezVisualSubjectProposalItemDto> proposal = [];
        var valid = false;
        var validation = required
            ? "Nessuna proposta valida ancora importata."
            : "Il piano soggetti non richiede risoluzione AI.";

        var exchange = AiExchangeStateStore.Load(project);
        var draft = required ? LatestUserRevisionDraft(project, exchange, setup.ImageCount) : [];
        if (plannerJob is not null)
        {
            var unit = exchange.WorkUnits.FirstOrDefault(x => x.LegacyAiJobId == plannerJob.JobId);
            workUnitId = unit?.WorkUnitId;
            if (unit is not null)
            {
                var version = exchange.Versions
                    .Where(x => x.WorkUnitId == unit.WorkUnitId && x.Status != AiExchangeVersionStatuses.Incomplete)
                    .OrderByDescending(x => x.VersionNumber)
                    .FirstOrDefault();
                if (version is not null)
                {
                    versionId = version.VersionId;
                    proposalStatus = version.Status ?? string.Empty;
                    valid = TryParseProposal(version.TextContent, setup.ImageCount, out var parsed, out validation);
                    if (valid)
                    {
                        var reconciliation = IsReconciliationPrompt(plannerJob.Prompt);
                        if (draft.Count > 0 && reconciliation && !ReconciliationSatisfied(project, exchange, unit, draft))
                        {
                            valid = false;
                            validation = "La risposta di riconciliazione non preserva esattamente i soggetti modificati dall'utente.";
                        }
                        else
                        {
                            proposal = parsed;
                            if (draft.Count > 0 && reconciliation)
                                validation = $"Proposta riconciliata: {parsed.Count} soggetti preservano le modifiche utente e hanno nuova semantica tecnica.";
                        }
                    }
                }
            }
        }

        if (required && !valid && draft.Count > 0)
        {
            proposal = draft.Select(x => new DiezVisualSubjectProposalItemDto(x.DisplayName, string.Empty, x.Description, string.Empty)).ToList();
            proposalStatus = plannerJob is not null && IsReconciliationPrompt(plannerJob.Prompt)
                ? "SEMANTIC_RECONCILIATION_PENDING"
                : "USER_REVISION_PENDING";
            validation = proposalStatus == "SEMANTIC_RECONCILIATION_PENDING"
                ? "Le modifiche utente sono salvate. Completa la riconciliazione semantica TEXT; il Prompt Pack immagini resta bloccato."
                : "Le modifiche utente cambiano il significato e richiedono una nuova riconciliazione semantica prima dell'accettazione.";
        }

        return new DiezVisualSubjectPlannerStateDto(
            required,
            setup.ImageCount,
            theme,
            plannerJob?.JobId,
            workUnitId,
            plannerJob?.Code ?? string.Empty,
            plannerJob?.Prompt ?? string.Empty,
            versionId,
            proposalStatus,
            proposal,
            valid,
            validation);
    }

'''
replace_between(planner, start, end, new_state)

# Add reconciliation/draft helpers before TryParseProposal.
replace_once(planner,
'''    private static bool TryParseProposal(
''',
'''    private static string BuildReconciliationPrompt(
        DiezVisualBookSetupDto setup,
        string theme,
        IReadOnlyList<DiezVisualSubjectProposalUserEditDto> edits)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# DIEZ SEMANTIC SUBJECT RECONCILIATION");
        sb.AppendLine();
        sb.AppendLine("The publisher has edited and LOCKED the user-visible subjects. You are NOT selecting subjects.");
        sb.AppendLine("Preserve every `display_name_it` and `description_it` exactly as supplied below. Do not replace, merge, broaden or reinterpret the subjects.");
        sb.AppendLine("Generate only the matching technical-English `canonical_concept` and `canonical_description` values needed by the downstream compiler.");
        sb.AppendLine($"Book family: {BookTypeEnglish(setup.BookType)}.");
        if (!string.IsNullOrWhiteSpace(theme)) sb.AppendLine($"Series theme context: {theme}.");
        sb.AppendLine($"Required subject count: EXACTLY {edits.Count}.");
        sb.AppendLine();
        sb.AppendLine("USER-LOCKED SUBJECTS — HARD:");
        for (var i = 0; i < edits.Count; i++)
        {
            sb.AppendLine($"{i + 1}. display_name_it: {edits[i].DisplayName}");
            sb.AppendLine($"   description_it: {edits[i].Description}");
        }
        sb.AppendLine();
        sb.AppendLine("Return JSON ONLY, with no Markdown fences and no commentary, using exactly this schema:");
        sb.AppendLine("{\"subjects\":[{\"display_name_it\":\"...\",\"canonical_concept\":\"...\",\"description_it\":\"...\",\"canonical_description\":\"...\"}]}");
        return sb.ToString().Trim();
    }

    private static string UserRevisionDraftJson(IReadOnlyList<DiezVisualSubjectProposalUserEditDto> edits)
    {
        var subjects = new JsonArray();
        foreach (var edit in edits)
        {
            subjects.Add(new JsonObject
            {
                ["display_name_it"] = edit.DisplayName,
                ["description_it"] = edit.Description
            });
        }
        return new JsonObject
        {
            ["diez_user_revision"] = "SEMANTIC_REBUILD_REQUIRED",
            ["subjects"] = subjects
        }.ToJsonString(JsonOptions);
    }

    private static string ProposalJson(IReadOnlyList<DiezVisualSubjectProposalItemDto> proposal)
    {
        var subjects = new JsonArray();
        foreach (var item in proposal)
        {
            subjects.Add(new JsonObject
            {
                ["display_name_it"] = item.DisplayName,
                ["canonical_concept"] = item.CanonicalConcept,
                ["description_it"] = item.Description,
                ["canonical_description"] = item.CanonicalDescription
            });
        }
        return new JsonObject { ["subjects"] = subjects }.ToJsonString(JsonOptions);
    }

    private static IReadOnlyList<DiezVisualSubjectProposalUserEditDto> LatestUserRevisionDraft(
        PreviewProject project,
        AiExchangeState exchange,
        int expectedCount)
    {
        var plannerJobs = project.AiProductionJobs
            .Where(IsPlannerJob)
            .OrderBy(x => x.CreatedAtLocal, StringComparer.Ordinal)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        for (var jobIndex = plannerJobs.Count - 1; jobIndex >= 0; jobIndex--)
        {
            var unit = exchange.WorkUnits.FirstOrDefault(x => x.LegacyAiJobId == plannerJobs[jobIndex].JobId);
            if (unit is null) continue;
            foreach (var version in exchange.Versions.Where(x => x.WorkUnitId == unit.WorkUnitId).OrderByDescending(x => x.VersionNumber))
            {
                if (TryParseUserRevisionDraft(version.TextContent, expectedCount, out var draft)) return draft;
            }
        }
        return [];
    }

    private static bool TryParseUserRevisionDraft(
        string? text,
        int expectedCount,
        out IReadOnlyList<DiezVisualSubjectProposalUserEditDto> edits)
    {
        edits = [];
        try
        {
            using var doc = JsonDocument.Parse(StripFence(text));
            if (!doc.RootElement.TryGetProperty("diez_user_revision", out var marker) ||
                !string.Equals(marker.GetString(), "SEMANTIC_REBUILD_REQUIRED", StringComparison.Ordinal)) return false;
            if (!doc.RootElement.TryGetProperty("subjects", out var subjects) || subjects.ValueKind != JsonValueKind.Array) return false;
            var list = new List<DiezVisualSubjectProposalUserEditDto>();
            foreach (var item in subjects.EnumerateArray())
            {
                list.Add(new DiezVisualSubjectProposalUserEditDto(Read(item, "display_name_it"), Read(item, "description_it")));
            }
            if (list.Count != expectedCount || list.Any(x => !VisualSemanticResolutionGuard.IsConcrete(x.DisplayName))) return false;
            edits = list;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ReconciliationSatisfied(
        PreviewProject project,
        AiExchangeState exchange,
        AiExchangeWorkUnit currentUnit,
        IReadOnlyList<DiezVisualSubjectProposalUserEditDto> draft)
    {
        var currentJob = currentUnit.LegacyAiJobId is Guid legacyId
            ? project.AiProductionJobs.FirstOrDefault(x => x.JobId == legacyId)
            : null;
        if (currentJob is null || !IsReconciliationPrompt(currentJob.Prompt)) return false;
        foreach (var version in exchange.Versions
                     .Where(x => x.WorkUnitId == currentUnit.WorkUnitId && x.Status != AiExchangeVersionStatuses.Incomplete)
                     .OrderBy(x => x.VersionNumber))
        {
            if (!TryParseProposal(version.TextContent, draft.Count, out var proposal, out _)) continue;
            if (MatchesDraft(proposal, draft)) return true;
        }
        return false;
    }

    private static bool MatchesDraft(
        IReadOnlyList<DiezVisualSubjectProposalItemDto> proposal,
        IReadOnlyList<DiezVisualSubjectProposalUserEditDto> draft)
    {
        if (proposal.Count != draft.Count) return false;
        for (var i = 0; i < proposal.Count; i++)
        {
            if (!string.Equals(proposal[i].DisplayName.Trim(), draft[i].DisplayName.Trim(), StringComparison.Ordinal) ||
                !string.Equals(proposal[i].Description.Trim(), draft[i].Description.Trim(), StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static bool IsReconciliationPrompt(string? prompt) =>
        (prompt ?? string.Empty).Contains("# DIEZ SEMANTIC SUBJECT RECONCILIATION", StringComparison.Ordinal);

    private static bool TryParseProposal(
''')

adapter = 'src/Diez.Uno/VisualBookDocumentAdapter.cs'
replace_once(adapter,
'''    public static DiezVisualSubjectPlannerMutation ApplyVisualSubjectPlannerProposal(this DiezProjectDocument document, Guid versionId)
    {
        var result = DiezVisualSubjectPlannerFrontendBridge.ApplyProposal(document.ExportProjectJson(), versionId);
        if (result.Status == "APPLIED") ApplyCoreJson(document, result.ProjectJson);
        return result;
    }
''',
'''    public static DiezVisualSubjectPlannerMutation ApplyVisualSubjectPlannerProposal(this DiezProjectDocument document, Guid versionId)
    {
        var result = DiezVisualSubjectPlannerFrontendBridge.ApplyProposal(document.ExportProjectJson(), versionId);
        if (result.Status == "APPLIED") ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static async Task<DiezVisualSubjectPlannerMutation> SaveVisualSubjectPlannerRevisionAsync(
        this DiezProjectDocument document,
        Guid versionId,
        IEnumerable<DiezVisualSubjectProposalUserEditDto> edits,
        bool semanticChange)
    {
        var result = await DiezVisualSubjectPlannerFrontendBridge.SaveUserRevisionAsync(
            document.ExportProjectJson(), versionId, edits, semanticChange);
        if (result.Status is "EDITORIAL_REVISED" or "REVISION_PREPARED" or "REVISION_PENDING")
            ApplyCoreJson(document, result.ProjectJson);
        return result;
    }
''')

workspace = 'src/Diez.Uno/VisualBookWorkspace.cs'
start = '        var plannerState = document.ReadVisualSubjectPlanner();\n'
end = '        root.Children.Add(NavigationRow(\n            null,\n            AsyncButton("Salva e continua → Prompt"'
new_section = '''        var plannerState = document.ReadVisualSubjectPlanner();

        string PlannerSummaryText()
        {
            if (!plannerState.Required)
                return "Piano soggetti risolto in Diez. I soggetti canonici sono già disponibili per la compilazione del Prompt immagini.";
            if (plannerState.Proposal.Count == 0)
                return $"Tema di serie da risolvere: {plannerState.SeriesTheme}. {plannerState.ValidationMessage}";
            if (!plannerState.ProposalValid)
                return plannerState.ValidationMessage;
            return "Proposta AI valida. Controlla e, se vuoi, modifica nomi e descrizioni prima di accettarla.";
        }

        var plannerSummary = new TextBlock
        {
            Text = PlannerSummaryText(),
            TextWrapping = TextWrapping.Wrap
        };

        var proposalEditors = new List<(TextBox Name, TextBox Description)>();
        var proposalEditorPanel = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (plannerState.Proposal.Count > 0)
        {
            proposalEditorPanel.Children.Add(new TextBlock
            {
                Text = plannerState.ProposalValid
                    ? "Proposta AI da verificare e modificare prima di accettare"
                    : "Modifiche utente salvate · in attesa di riconciliazione semantica",
                FontSize = 17,
                TextWrapping = TextWrapping.Wrap
            });
            foreach (var item in plannerState.Proposal.Select((value, index) => (value, index)))
            {
                var nameBox = Editor(item.value.DisplayName, $"Nome soggetto {item.index + 1}", 54);
                var descriptionBox = Editor(item.value.Description, $"Descrizione soggetto {item.index + 1}", 72);
                nameBox.IsReadOnly = !plannerState.ProposalValid;
                descriptionBox.IsReadOnly = !plannerState.ProposalValid;
                proposalEditors.Add((nameBox, descriptionBox));
                proposalEditorPanel.Children.Add(Card($"Soggetto {item.index + 1}", Vertical(
                    Labeled("Nome", nameBox),
                    Labeled("Descrizione", descriptionBox))));
            }
        }

        var editorialOnly = new RadioButton
        {
            GroupName = "PlannerProposalEditMeaning",
            Content = "Ho cambiato solo la formulazione · il soggetto/significato è lo stesso",
            IsChecked = true
        };
        var semanticChange = new RadioButton
        {
            GroupName = "PlannerProposalEditMeaning",
            Content = "Ho cambiato il soggetto o il significato · Diez deve ricompilare la semantica",
            IsChecked = false
        };
        var editStatus = new TextBlock
        {
            Text = plannerState.ProposalValid
                ? "Puoi accettare la proposta così com'è oppure modificarla. Se modifichi un campo, salva la revisione prima di accettare."
                : plannerState.ValidationMessage,
            TextWrapping = TextWrapping.Wrap
        };

        var acceptProposal = AsyncButton("Accetta proposta e congela i soggetti", async () =>
        {
            var current = document.ReadVisualSubjectPlanner();
            if (!current.Required)
            {
                report("Il piano soggetti è già risolto in Diez.");
                return;
            }
            if (!current.ProposalValid || !current.ProposalVersionId.HasValue)
            {
                report("Non c'è ancora una proposta soggetti valida da accettare.");
                return;
            }
            var applied = document.ApplyVisualSubjectPlannerProposal(current.ProposalVersionId.Value);
            await save();
            report(applied.Message);
            if (applied.Status == "APPLIED") refresh();
        });
        acceptProposal.IsEnabled = plannerState.Required && plannerState.ProposalValid && plannerState.ProposalVersionId.HasValue;

        var saveProposalEdits = AsyncButton("Salva modifiche alla proposta", async () =>
        {
            var current = document.ReadVisualSubjectPlanner();
            if (!current.ProposalValid || !current.ProposalVersionId.HasValue)
            {
                report("La proposta non è ancora in uno stato modificabile: completa prima la risposta/ricalcolo semantico in corso.");
                return;
            }
            if (proposalEditors.Count != current.Proposal.Count)
            {
                report("La proposta a video non corrisponde più allo stato del progetto. Riapri la Definizione prima di modificarla.");
                return;
            }
            var edits = proposalEditors
                .Select(x => new DiezVisualSubjectProposalUserEditDto(x.Name.Text ?? string.Empty, x.Description.Text ?? string.Empty))
                .ToList();
            var result = await document.SaveVisualSubjectPlannerRevisionAsync(
                current.ProposalVersionId.Value,
                edits,
                semanticChange.IsChecked == true);
            await save();
            report(result.Message);
            if (result.Status is "REVISION_PREPARED" or "REVISION_PENDING")
            {
                showAiCenter();
                return;
            }
            if (result.Status == "EDITORIAL_REVISED") refresh();
        });
        saveProposalEdits.IsEnabled = plannerState.ProposalValid && proposalEditors.Count > 0;

        foreach (var editor in proposalEditors.SelectMany(x => new[] { x.Name, x.Description }))
        {
            editor.TextChanged += (_, _) =>
            {
                acceptProposal.IsEnabled = false;
                editStatus.Text = "Hai modifiche non salvate. Salva la revisione prima di accettare, così Diez non ignora nessuna tua scelta.";
            };
        }

        var prepareProposal = AsyncButton("Prepara proposta soggetti con AI", async () =>
        {
            if (!await SaveSetupAsync()) return;
            var prepared = document.PrepareVisualSubjectPlanner(
                document.GetUiString("Prompt.MustDo"),
                document.GetUiString("Prompt.MustNotDo"));
            await save();
            report(prepared.Message);
            if (prepared.Status is "PREPARED" or "ALREADY_PREPARED" or "REVISION_PENDING") showAiCenter();
            else if (prepared.Status == "PROPOSAL_READY") refresh();
        });
        prepareProposal.IsEnabled = plannerState.Required &&
            !plannerState.ProposalValid &&
            !string.Equals(plannerState.ProposalStatus, "SEMANTIC_RECONCILIATION_PENDING", StringComparison.OrdinalIgnoreCase);

        var proposalControls = new List<UIElement>
        {
            new TextBlock
            {
                Text = "Se hai descritto una serie (per esempio “3 soggetti di Halloween”), il generatore immagini non deve scegliere i soggetti. Dopo l'import della risposta planner, Diez mostra qui ciò che l'AI ha trovato. Puoi accettarlo, correggerne la formulazione o cambiare davvero uno o più soggetti prima del freeze canonico.",
                TextWrapping = TextWrapping.Wrap
            },
            plannerSummary
        };
        if (plannerState.Proposal.Count > 0)
        {
            proposalControls.Add(proposalEditorPanel);
            proposalControls.Add(editStatus);
            if (plannerState.ProposalValid)
            {
                proposalControls.Add(new TextBlock
                {
                    Text = "Se una modifica cambia solo il testo visibile, Diez conserva la semantica tecnica già validata. Se cambia il soggetto/significato, la vecchia semantica viene invalidata e serve una nuova riconciliazione AI prima dell'accettazione.",
                    TextWrapping = TextWrapping.Wrap
                });
                proposalControls.Add(WrapRow(editorialOnly, semanticChange));
                proposalControls.Add(WrapRow(saveProposalEdits, acceptProposal));
            }
        }
        proposalControls.Add(WrapRow(prepareProposal, ActionButton("Apri Produzione con AI", showAiCenter)));
        root.Children.Add(Card("Piano soggetti Diez · prima del Prompt", Vertical(proposalControls.ToArray())));

'''
replace_between(workspace, start, end, new_section)

# Extend regression coverage before final Console.WriteLine.
test = 'tests/Diez.VisualSemanticRegression/Program.cs'
replace_once(test,
'''Console.WriteLine("VISUAL_SEMANTIC_REGRESSION_OK");
''',
'''// Round 5.5: the visible AI proposal is editable before acceptance.
var editableProject = Save(NewProject(), "3 soggetti di Halloween");
var editablePrepared = DiezVisualSubjectPlannerFrontendBridge.Prepare(editableProject);
var editableIngest = await DiezAiExchangeBridge.IngestTextResultAsync(
    editablePrepared.ProjectJson,
    editablePrepared.State.PlannerWorkUnitId!.Value,
    plannerJson);
var editableState = DiezVisualSubjectPlannerFrontendBridge.Read(editableIngest.ProjectJson);
Require(editableState.ProposalValid && editableState.ProposalVersionId.HasValue,
    "La proposta da modificare deve essere visibile e valida prima dell'accettazione.");

var editorialEdits = editableState.Proposal
    .Select((x, i) => new DiezVisualSubjectProposalUserEditDto(
        i == 0 ? "Zucca jack-o'-lantern allegra" : x.DisplayName,
        i == 0 ? "Una grande zucca allegra e immediatamente riconoscibile." : x.Description))
    .ToList();
var editorialRevision = await DiezVisualSubjectPlannerFrontendBridge.SaveUserRevisionAsync(
    editableIngest.ProjectJson,
    editableState.ProposalVersionId!.Value,
    editorialEdits,
    semanticChange: false);
Require(editorialRevision.Status == "EDITORIAL_REVISED", "La revisione solo editoriale deve restare una Candidate: " + editorialRevision.Message);
Require(editorialRevision.State.ProposalValid && editorialRevision.State.Proposal[0].DisplayName == "Zucca jack-o'-lantern allegra",
    "La nuova formulazione italiana deve essere visibile prima dell'accettazione.");
Require(editorialRevision.State.Proposal[0].CanonicalConcept == editableState.Proposal[0].CanonicalConcept,
    "Una modifica dichiarata solo editoriale deve preservare il canonical_concept validato.");

var semanticEdits = editorialRevision.State.Proposal
    .Select((x, i) => new DiezVisualSubjectProposalUserEditDto(
        i == 0 ? "Strega simpatica con cappello a punta" : x.DisplayName,
        i == 0 ? "Una singola strega simpatica e riconoscibile con cappello a punta." : x.Description))
    .ToList();
var semanticRevision = await DiezVisualSubjectPlannerFrontendBridge.SaveUserRevisionAsync(
    editorialRevision.ProjectJson,
    editorialRevision.State.ProposalVersionId!.Value,
    semanticEdits,
    semanticChange: true);
Require(semanticRevision.Status == "REVISION_PREPARED", "Una modifica semantica deve preparare una nuova riconciliazione: " + semanticRevision.Message);
Require(!semanticRevision.State.ProposalValid && semanticRevision.State.Proposal[0].DisplayName.Contains("Strega", StringComparison.OrdinalIgnoreCase),
    "Durante la riconciliazione Diez deve conservare e mostrare la modifica utente, senza fingere che sia già semanticamente valida.");
Require(semanticRevision.State.PlannerPrompt.Contains("DIEZ SEMANTIC SUBJECT RECONCILIATION", StringComparison.Ordinal),
    "La modifica semantica deve usare una Work Unit di riconciliazione distinta dalla scelta iniziale dei soggetti.");
Require(semanticRevision.State.PlannerPrompt.Contains("Strega simpatica con cappello a punta", StringComparison.Ordinal),
    "La riconciliazione deve trattare il soggetto modificato dall'utente come HARD LOCK.");
var duplicateSemanticRevision = DiezVisualSubjectPlannerFrontendBridge.Prepare(semanticRevision.ProjectJson);
Require(duplicateSemanticRevision.Status == "REVISION_PENDING",
    "Con una riconciliazione già pronta Diez non deve creare un altro planner.");

var reconciledJson = """
{"subjects":[
  {"display_name_it":"Strega simpatica con cappello a punta","canonical_concept":"friendly witch wearing a pointed hat","description_it":"Una singola strega simpatica e riconoscibile con cappello a punta.","canonical_description":"One friendly recognizable witch wearing a pointed hat."},
  {"display_name_it":"Fantasma amichevole","canonical_concept":"friendly smiling ghost","description_it":"Un singolo fantasma simpatico e ben leggibile.","canonical_description":"One friendly smiling ghost with a clear simple silhouette."},
  {"display_name_it":"Gatto con cappello da strega","canonical_concept":"cute cat wearing a witch hat","description_it":"Un gatto simpatico con cappello da strega.","canonical_description":"A cute cat wearing a witch hat."}
]}
""";
var reconciledIngest = await DiezAiExchangeBridge.IngestTextResultAsync(
    semanticRevision.ProjectJson,
    semanticRevision.State.PlannerWorkUnitId!.Value,
    reconciledJson);
var reconciledState = DiezVisualSubjectPlannerFrontendBridge.Read(reconciledIngest.ProjectJson);
Require(reconciledState.ProposalValid && reconciledState.Proposal[0].CanonicalConcept.Contains("friendly witch", StringComparison.OrdinalIgnoreCase),
    "Solo dopo la riconciliazione la proposta modificata deve tornare accettabile.");
var reconciledApplied = DiezVisualSubjectPlannerFrontendBridge.ApplyProposal(
    reconciledIngest.ProjectJson,
    reconciledState.ProposalVersionId!.Value);
Require(reconciledApplied.Status == "APPLIED", "La proposta modificata e riconciliata deve poter essere congelata.");
var reconciledPack = DiezVisualBookFrontendBridge.BuildPromptPack(reconciledApplied.ProjectJson);
Require(reconciledPack.Items[0].Prompt.Contains("friendly witch wearing a pointed hat", StringComparison.OrdinalIgnoreCase),
    "Il renderer deve ricevere la nuova semantica canonica, non quella stale della zucca.");
Require(!reconciledPack.Items[0].Prompt.Contains("friendly jack-o'-lantern pumpkin", StringComparison.OrdinalIgnoreCase),
    "La vecchia semantica AI non deve sopravvivere a una modifica utente del soggetto.");

Console.WriteLine("VISUAL_SEMANTIC_REGRESSION_OK");
''')

print('Round 5.5 editable proposal patch applied successfully.')
