using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DiezPublishingStudio;

public sealed record DiezVisualSubjectProposalItemDto(
    string DisplayName,
    string CanonicalConcept,
    string Description,
    string CanonicalDescription);

public sealed record DiezVisualSubjectProposalUserEditDto(
    string DisplayName,
    string Description);

public sealed record DiezVisualSubjectPlannerStateDto(
    bool Required,
    int RequestedCount,
    string SeriesTheme,
    Guid? PlannerJobId,
    Guid? PlannerWorkUnitId,
    string PlannerCode,
    string PlannerPrompt,
    Guid? ProposalVersionId,
    string ProposalStatus,
    IReadOnlyList<DiezVisualSubjectProposalItemDto> Proposal,
    bool ProposalValid,
    string ValidationMessage);

public sealed record DiezVisualSubjectPlannerMutation(
    string ProjectJson,
    string Status,
    string Message,
    DiezVisualSubjectPlannerStateDto State);

/// <summary>
/// Semantic planning boundary that resolves an aggregate visual-series theme into concrete canonical
/// subjects BEFORE image Work Units are compiled. The planner is a TEXT AI Exchange activity, never
/// an image-renderer responsibility. Today all built-in providers have direct API disabled, therefore
/// this bridge deliberately supports the existing copy/paste AI Exchange path; a future API executor
/// can use the same structured contract without changing canonical state.
/// </summary>
public static class DiezVisualSubjectPlannerFrontendBridge
{
    private const string PlannerTitle = "Diez · Piano soggetti visuali";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static readonly Regex DirectTheme = new(
        @"^\s*\S+\s+(?:soggett[oi]|subjects?|personagg(?:io|i)|characters?)\s*(?:(?:di|per|of|for|about)\s+)?(?<theme>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LegacyTheme = new(
        @"^\s*\S+\s+(?:images?|immagin[ei])\s+(?:(?:di|of)\s+)?(?:soggett[oi]|subjects?|personagg(?:io|i)|characters?)\s*(?:(?:di|per|of|for|about)\s+)?(?<theme>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlannerIrrelevantLayoutConstraint = new(
        @"(?:\bcollage\b|\btriptych\b|\bgrid\b|\bcontact\s+sheet\b|\bmulti[-\s]?panel\b|\bun['’]?\s*unic[ao]\b.*\b(?:image|immagine|canvas)\b.*\b(?:\d+|multiple|pi[uù])\b.*\b(?:illustr|soggett|subject)|\bone\s+(?:image|canvas)\b.*\b(?:multiple|\d+)\b.*\b(?:illustr|subject))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static DiezVisualSubjectPlannerStateDto Read(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        return State(project);
    }

    public static DiezVisualSubjectPlannerMutation Prepare(
        string projectJson,
        string? mustDo = null,
        string? mustNotDo = null)
    {
        var (_, currentProject) = Parse(projectJson);
        var currentState = State(currentProject);
        if (!currentState.Required)
            return Mutation(projectJson, "NOT_REQUIRED", "Il piano soggetti è già risolto in Diez: non serve una nuova proposta AI.");
        if (string.Equals(currentState.ProposalStatus, "SEMANTIC_RECONCILIATION_PENDING", StringComparison.OrdinalIgnoreCase))
            return Mutation(projectJson, "REVISION_PENDING", "Le modifiche utente che cambiano il significato sono già in attesa di riconciliazione semantica. Completa l'attività planner esistente; Diez non crea duplicati.");
        if (currentState.ProposalValid)
            return Mutation(projectJson, "PROPOSAL_READY", "Una proposta AI valida è già disponibile. Torna in Definizione, controlla i soggetti trovati e scegli se accettarla; non creo un secondo planner.");

        var setup = ProjectSetup(currentProject);
        if (setup.ImageCount < 2)
            return Mutation(projectJson, "INVALID", "Una serie aggregata deve richiedere almeno due immagini.");

        var theme = ExtractTheme(setup.Subject);
        if (string.IsNullOrWhiteSpace(theme))
            return Mutation(projectJson, "INVALID", "Diez riconosce una serie aggregata ma non riesce a isolare il tema. Rendi più esplicito il tema prima di chiedere la proposta.");

        var prompt = BuildPlannerPrompt(setup, theme, mustDo, mustNotDo);
        var existingPlanner = currentProject.AiProductionJobs
            .Where(IsPlannerJob)
            .OrderByDescending(x => x.CreatedAtLocal, StringComparer.Ordinal)
            .ThenByDescending(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => string.Equals((x.Prompt ?? string.Empty).Trim(), prompt, StringComparison.Ordinal));
        if (existingPlanner is not null)
            return Mutation(projectJson, "ALREADY_PREPARED", $"Il planner {existingPlanner.Code} è già pronto per questo stesso piano. Selezionalo in Produzione con AI, incolla la risposta JSON e importala come candidato; Diez non crea duplicati.");

        var prepared = DiezAiExchangeBridge.CreateReadyJob(
            projectJson,
            PlannerTitle,
            AiProductionService.TypeText,
            prompt);

        var message = "Proposta soggetti preparata come attività AI testuale. Apri Produzione con AI, copia il Prompt dell’attività planner selezionata, eseguilo con il provider scelto e importa la risposta JSON come candidato. Diez tornerà poi in Definizione per mostrarti ciò che l’AI ha trovato prima dell’accettazione.";
        return Mutation(prepared.ProjectJson, "PREPARED", message);
    }

    public static async Task<DiezVisualSubjectPlannerMutation> SaveUserRevisionAsync(
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

    public static DiezVisualSubjectPlannerMutation ApplyProposal(string projectJson, Guid versionId)
    {
        var (_, originalProject) = Parse(projectJson);
        var originalState = AiExchangeStateStore.Load(originalProject);
        var version = originalState.Versions.FirstOrDefault(x => x.VersionId == versionId);
        var unit = version is null ? null : originalState.WorkUnits.FirstOrDefault(x => x.WorkUnitId == version.WorkUnitId);
        var legacyJob = unit?.LegacyAiJobId is Guid legacyId
            ? originalProject.AiProductionJobs.FirstOrDefault(x => x.JobId == legacyId)
            : null;
        if (version is null || unit is null || legacyJob is null || !IsPlannerJob(legacyJob))
            return Mutation(projectJson, "NOT_FOUND", "La proposta soggetti selezionata non appartiene al planner Diez.");

        var setup = DiezVisualBookFrontendBridge.Read(projectJson);
        if (!VisualSemanticResolutionGuard.LooksAggregateSubject(setup.Subject))
            return Mutation(projectJson, "STALE", "La Definizione non contiene più il tema aggregato per cui era stata preparata la proposta.");

        if (!TryParseProposal(version.TextContent, setup.ImageCount, out var proposal, out var validation))
            return Mutation(projectJson, "INVALID_PROPOSAL", validation);

        var pendingDraft = LatestUserRevisionDraft(originalProject, originalState, setup.ImageCount);
        if (pendingDraft.Count > 0 && !ReconciliationSatisfied(originalProject, originalState, unit, pendingDraft))
            return Mutation(projectJson, "STALE_SEMANTICS", "Le modifiche utente cambiano ancora il significato e non sono state riconciliate semanticamente. Completa il planner di revisione prima di accettare.");

        // Explicit user acceptance also approves this non-image planning candidate in AI Exchange.
        var approved = DiezAiExchangeBridge.ApproveVersion(projectJson, versionId);
        if (approved.Status is not ("APPROVED" or "BLOCKED") ||
            (approved.Status == "BLOCKED" && approved.Version?.Status != AiExchangeVersionStatuses.Approved))
            return Mutation(projectJson, "BLOCKED", approved.Message);

        var (root, project) = Parse(approved.ProjectJson);
        var model = new MultiSubjectProfile
        {
            SchemaVersion = 2,
            Enabled = true,
            RequestedCount = proposal.Count,
            GroupDescription = setup.Subject,
            Subjects = proposal.Select(item =>
            {
                var subject = new MultiSubjectDefinition
                {
                    SubjectId = Guid.NewGuid().ToString("D"),
                    Name = item.DisplayName,
                    CanonicalConcept = item.CanonicalConcept,
                    Description = item.Description,
                    CanonicalDescription = item.CanonicalDescription,
                    Included = true,
                    Archived = false
                };
                MultiSubjectProfileService.EnsureConsistencyDefaults(subject);
                return subject;
            }).ToList()
        };
        model.ActiveSubjectId = model.Subjects[0].SubjectId;
        MultiSubjectProfileService.Save(project, model);
        MergeEntities(root, project);

        var written = Write(root);
        return new DiezVisualSubjectPlannerMutation(
            written,
            "APPLIED",
            $"Proposta accettata: {proposal.Count} soggetti concreti sono ora congelati nello stato canonico Diez. Il Prompt immagini può essere ricompilato senza delegare la scelta al renderer.",
            State(project));
    }

    public static bool TryValidateProposal(string text, int expectedCount, out string message)
    {
        var valid = TryParseProposal(text, expectedCount, out _, out message);
        return valid;
    }

    private static DiezVisualSubjectPlannerMutation Mutation(string projectJson, string status, string message)
    {
        var (_, project) = Parse(projectJson);
        return new DiezVisualSubjectPlannerMutation(projectJson, status, message, State(project));
    }

    private static DiezVisualSubjectPlannerStateDto State(PreviewProject project)
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

    private static DiezVisualBookSetupDto ProjectSetup(PreviewProject project)
    {
        var plan = VisualBookPlanService.Load(project);
        var bookType = BookTypeProfileService.Get(project);
        if (string.Equals(bookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            var p = BookTypePromptProfileService.LoadColoring(project);
            return new DiezVisualBookSetupDto(bookType, plan.ImageCount, p.SubjectDescription, p.EnvironmentDescription,
                plan.Consistent, ImageCollectionWorkspaceService.GetConsistencyRules(project),
                new DiezColoringProfileDto(p.Style, p.BoldEasy, ColoringCozyPolicyStore.Resolve(project),
                    p.TargetAudience, p.Difficulty, p.LineWeight, p.Complexity, p.ElementDensity, p.Background,
                    p.WhiteSpace, p.ClosedAreas, p.AvoidTinyAreas, p.CleanContours, p.NoTextInsideImage,
                    p.SubjectClearlySeparated, p.CustomStyleNotes), null);
        }

        var image = ImageCollectionPromptProfileService.Load(project);
        return new DiezVisualBookSetupDto(bookType, plan.ImageCount, image.SubjectDescription, image.EnvironmentDescription,
            plan.Consistent, ImageCollectionWorkspaceService.GetConsistencyRules(project), null,
            new DiezImageProfileDto(image.EditorialUse, image.ColorMode, image.DetailLevel, image.LineTreatment,
                image.RenderingStyle, image.Background, image.Viewpoint, image.KeepSubjectReadable,
                image.AvoidTextInsideImage, image.EditorialClarity, image.SameScaleWhenSeries, image.Notes));
    }

    private static string BuildPlannerPrompt(DiezVisualBookSetupDto setup, string theme, string? mustDo, string? mustNotDo)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# DIEZ SEMANTIC SUBJECT PLANNER");
        sb.AppendLine();
        sb.AppendLine("You are planning canonical subjects for a visual publishing project BEFORE any image-rendering prompt is compiled.");
        sb.AppendLine("Do not generate images. Do not write image-model prompts. Do not decide layout, collage, panels, filenames, IDs, transport metadata or rendering procedure.");
        sb.AppendLine($"Book family: {BookTypeEnglish(setup.BookType)}.");
        sb.AppendLine($"Series theme: {theme}.");
        sb.AppendLine($"Required subject count: EXACTLY {setup.ImageCount}.");
        if (!string.IsNullOrWhiteSpace(setup.Coloring?.Style) && !string.Equals(setup.Coloring.Style, "Custom", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine($"Editorial style context: {PromptEnglishNormalizer.NormalizeProviderFacing(setup.Coloring.Style)}.");
        if (!string.IsNullOrWhiteSpace(setup.Coloring?.TargetAudience))
            sb.AppendLine($"Audience context: {PromptEnglishNormalizer.NormalizeProviderFacing(setup.Coloring.TargetAudience)}.");

        var required = PlannerSubjectConstraint(mustDo);
        var excluded = PlannerSubjectConstraint(mustNotDo);
        if (!string.IsNullOrWhiteSpace(required)) sb.AppendLine("Publisher HARD requirements relevant to subject selection: " + required);
        if (!string.IsNullOrWhiteSpace(excluded)) sb.AppendLine("Publisher HARD exclusions relevant to subject selection: " + excluded);

        sb.AppendLine();
        sb.AppendLine("Planning rules — HARD:");
        sb.AppendLine("- Return exactly the requested number of distinct concrete focal subjects.");
        sb.AppendLine("- Every subject must be individually drawable, immediately recognizable and relevant to the series theme.");
        sb.AppendLine("- Do not return categories, alternatives, groups, counts, scene descriptions, layouts or placeholders as subjects.");
        sb.AppendLine("- Avoid near-duplicates unless the publisher explicitly requested repetition.");
        sb.AppendLine("- Preserve the meaning of publisher HARD requirements and exclusions; never weaken them.");
        sb.AppendLine("- `display_name_it` and `description_it` are Italian user-visible editorial labels.");
        sb.AppendLine("- `canonical_concept` is a concise technical-English semantic concept, not a finished image prompt.");
        sb.AppendLine("- `canonical_description` is an optional technical-English semantic description used by the compiler; it must preserve the same facts as `description_it`.");
        sb.AppendLine();
        sb.AppendLine("Return JSON ONLY, with no Markdown fences and no commentary, using exactly this schema:");
        sb.AppendLine("{\"subjects\":[{\"display_name_it\":\"...\",\"canonical_concept\":\"...\",\"description_it\":\"...\",\"canonical_description\":\"...\"}]}");
        return sb.ToString().Trim();
    }

    private static string PlannerSubjectConstraint(string? value)
    {
        var kept = new List<string>();
        foreach (var raw in (value ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (PlannerIrrelevantLayoutConstraint.IsMatch(line)) continue;
            var normalized = PromptEnglishNormalizer.NormalizeProviderFacing(line);
            if (PlannerIrrelevantLayoutConstraint.IsMatch(normalized)) continue;
            kept.Add(normalized);
        }
        return string.Join("; ", kept.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildReconciliationPrompt(
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
        sb.AppendLine("{"subjects":[{"display_name_it":"...","canonical_concept":"...","description_it":"...","canonical_description":"..."}]}");
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
        string? text,
        int expectedCount,
        out IReadOnlyList<DiezVisualSubjectProposalItemDto> proposal,
        out string message)
    {
        proposal = [];
        var clean = StripFence(text);
        if (string.IsNullOrWhiteSpace(clean))
        {
            message = "La proposta è vuota.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(clean);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("subjects", out var subjects) ||
                subjects.ValueKind != JsonValueKind.Array)
            {
                message = "La risposta del planner deve essere un oggetto JSON con l'array `subjects`.";
                return false;
            }

            var items = new List<DiezVisualSubjectProposalItemDto>();
            foreach (var item in subjects.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    message = "Ogni elemento di `subjects` deve essere un oggetto JSON.";
                    return false;
                }
                var display = Read(item, "display_name_it");
                var concept = Read(item, "canonical_concept");
                var description = Read(item, "description_it");
                var canonicalDescription = Read(item, "canonical_description");
                if (display.Length == 0 || concept.Length == 0)
                {
                    message = "Ogni soggetto deve contenere `display_name_it` e `canonical_concept`.";
                    return false;
                }
                if (!VisualSemanticResolutionGuard.IsConcrete(display) || !VisualSemanticResolutionGuard.IsConcrete(concept))
                {
                    message = $"'{display}' non è un soggetto atomico valido.";
                    return false;
                }
                items.Add(new DiezVisualSubjectProposalItemDto(display, concept, description, canonicalDescription));
            }

            if (items.Count != expectedCount)
            {
                message = $"La proposta contiene {items.Count} soggetti, ma Diez ne richiede esattamente {expectedCount}.";
                return false;
            }
            if (items.Select(x => x.DisplayName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count ||
                items.Select(x => x.CanonicalConcept.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            {
                message = "La proposta contiene soggetti duplicati o semanticamente non distinti.";
                return false;
            }

            proposal = items;
            message = $"Proposta valida: {items.Count} soggetti concreti e distinti.";
            return true;
        }
        catch (JsonException ex)
        {
            message = "JSON della proposta non valido: " + ex.Message;
            return false;
        }
    }

    private static string ExtractTheme(string? aggregate)
    {
        var text = (aggregate ?? string.Empty).Trim();
        foreach (var regex in new[] { LegacyTheme, DirectTheme })
        {
            var match = regex.Match(text);
            if (!match.Success) continue;
            var theme = match.Groups["theme"].Value.Trim().Trim('.', ';', ':', '-', '–', '—');
            if (theme.Length > 0) return PromptEnglishNormalizer.NormalizeProviderFacing(theme);
        }
        return string.Empty;
    }

    private static string BookTypeEnglish(string? bookType) => BookTypeCatalog.Normalize(bookType) switch
    {
        BookTypeCatalog.ColoringBook => "coloring book",
        BookTypeCatalog.ImageCollection => "image collection",
        BookTypeCatalog.IllustratedBook => "illustrated book",
        _ => "visual book"
    };

    private static string StripFence(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal)) return text;
        var firstBreak = text.IndexOf('\n');
        if (firstBreak >= 0) text = text[(firstBreak + 1)..];
        if (text.EndsWith("```", StringComparison.Ordinal)) text = text[..^3];
        return text.Trim();
    }

    private static string Read(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? string.Empty).Trim()
            : string.Empty;

    private static bool IsPlannerJob(AiProductionJob job) =>
        string.Equals((job.Title ?? string.Empty).Trim(), PlannerTitle, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(job.OutputType, AiProductionService.TypeText, StringComparison.OrdinalIgnoreCase);

    private static (JsonObject Root, PreviewProject Project) Parse(string projectJson)
    {
        var root = JsonNode.Parse(projectJson) as JsonObject
            ?? throw new InvalidDataException("Il JSON del progetto Diez non è valido.");
        var project = JsonSerializer.Deserialize<PreviewProject>(projectJson, JsonOptions)
            ?? throw new InvalidDataException("Il progetto Diez non può essere letto dal Core.");
        project.EditionMetadata ??= new EditionMetadata();
        project.AiProduction ??= new AiProductionSettings();
        project.AiProductionJobs ??= [];
        project.Materials ??= [];
        project.ContentNodes ??= [];
        project.IllustrationPlacements ??= [];
        project.Entities ??= [];
        project.Relations ??= [];
        project.BibleEntries ??= [];
        project.ConsistencyFacts ??= [];
        project.ConsistencyIssues ??= [];
        project.ConsistencyResolutions ??= [];
        project.RevisionCandidates ??= [];
        return (root, project);
    }

    private static void MergeEntities(JsonObject root, PreviewProject project)
    {
        var raw = root["Entities"] as JsonArray ?? new JsonArray();
        root["Entities"] = raw;
        foreach (var entity in project.Entities)
        {
            if (JsonSerializer.SerializeToNode(entity, JsonOptions) is not JsonObject typed) continue;
            var id = Scalar(typed["EntityId"]);
            if (string.IsNullOrWhiteSpace(id)) continue;
            var existing = raw.OfType<JsonObject>().FirstOrDefault(x =>
                string.Equals(Scalar(x["EntityId"]), id, StringComparison.OrdinalIgnoreCase));
            if (existing is null) raw.Add(typed);
            else foreach (var pair in typed) existing[pair.Key] = pair.Value?.DeepClone();
        }
    }

    private static string Scalar(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        return node?.ToJsonString().Trim('"') ?? string.Empty;
    }

    private static string Write(JsonObject root) => root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}