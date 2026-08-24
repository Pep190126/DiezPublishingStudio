from pathlib import Path


def replace_once(path: str, old: str, new: str):
    p = Path(path)
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected exactly one match, found {count}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')

planner = 'src/Diez.Core/DiezVisualSubjectPlannerFrontendBridge.cs'

replace_once(planner,
'''    private static readonly Regex LegacyTheme = new(
        @"^\\s*\\S+\\s+(?:images?|immagin[ei])\\s+(?:(?:di|of)\\s+)?(?:soggett[oi]|subjects?|personagg(?:io|i)|characters?)\\s*(?:(?:di|per|of|for|about)\\s+)?(?<theme>.+?)\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
''',
'''    private static readonly Regex LegacyTheme = new(
        @"^\\s*\\S+\\s+(?:images?|immagin[ei])\\s+(?:(?:di|of)\\s+)?(?:soggett[oi]|subjects?|personagg(?:io|i)|characters?)\\s*(?:(?:di|per|of|for|about)\\s+)?(?<theme>.+?)\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlannerIrrelevantLayoutConstraint = new(
        @"(?:\\bcollage\\b|\\btriptych\\b|\\bgrid\\b|\\bcontact\\s+sheet\\b|\\bmulti[-\\s]?panel\\b|\\bun['’]?\\s*unic[ao]\\b.*\\b(?:image|immagine|canvas)\\b.*\\b(?:\\d+|multiple|pi[uù])\\b.*\\b(?:illustr|soggett|subject)|\\bone\\s+(?:image|canvas)\\b.*\\b(?:multiple|\\d+)\\b.*\\b(?:illustr|subject))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
''')

replace_once(planner,
'''    {
        var setup = DiezVisualBookFrontendBridge.Read(projectJson);
        if (!VisualSemanticResolutionGuard.LooksAggregateSubject(setup.Subject))
            return Mutation(projectJson, "NOT_REQUIRED", "Il soggetto è già atomico: non serve una proposta di piano soggetti.");
        if (setup.ImageCount < 2)
            return Mutation(projectJson, "INVALID", "Una serie aggregata deve richiedere almeno due immagini.");

        var theme = ExtractTheme(setup.Subject);
        if (string.IsNullOrWhiteSpace(theme))
            return Mutation(projectJson, "INVALID", "Diez riconosce una serie aggregata ma non riesce a isolare il tema. Rendi più esplicito il tema prima di chiedere la proposta.");

        var prompt = BuildPlannerPrompt(setup, theme, mustDo, mustNotDo);
        var prepared = DiezAiExchangeBridge.CreateReadyJob(
            projectJson,
            PlannerTitle,
            AiProductionService.TypeText,
            prompt);

        var message = "Proposta soggetti preparata come attività AI testuale. Apri Produzione con AI, copia il Prompt dell’attività selezionata, eseguilo con il provider scelto e importa la risposta JSON come candidato. Poi torna in Definizione per accettarla.";
        return Mutation(prepared.ProjectJson, "PREPARED", message);
    }
''',
'''    {
        var (_, currentProject) = Parse(projectJson);
        var currentState = State(currentProject);
        if (!currentState.Required)
            return Mutation(projectJson, "NOT_REQUIRED", "Il piano soggetti è già risolto in Diez: non serve una nuova proposta AI.");
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
''')

replace_once(planner,
'''        var setup = ProjectSetup(project);
        var required = VisualSemanticResolutionGuard.LooksAggregateSubject(setup.Subject);
        var theme = required ? ExtractTheme(setup.Subject) : string.Empty;
''',
'''        var setup = ProjectSetup(project);
        var aggregate = VisualSemanticResolutionGuard.LooksAggregateSubject(setup.Subject);
        var multi = MultiSubjectProfileService.Load(project);
        var activeSubjects = multi.Enabled ? MultiSubjectProfileService.ActiveSubjects(multi) : [];
        var resolvedStructured = multi.Enabled &&
            activeSubjects.Count == setup.ImageCount &&
            activeSubjects.All(x => VisualSemanticResolutionGuard.IsConcrete(x.CanonicalConcept) ||
                                    VisualSemanticResolutionGuard.IsConcrete(x.Name));
        var required = aggregate && !resolvedStructured;
        var theme = aggregate ? ExtractTheme(setup.Subject) : string.Empty;
''')

replace_once(planner,
'''        var required = PromptEnglishNormalizer.NormalizeProviderFacing(mustDo);
        var excluded = PromptEnglishNormalizer.NormalizeProviderFacing(mustNotDo);
        if (!string.IsNullOrWhiteSpace(required)) sb.AppendLine("Publisher HARD requirements relevant to subject selection: " + required);
        if (!string.IsNullOrWhiteSpace(excluded)) sb.AppendLine("Publisher HARD exclusions relevant to subject selection: " + excluded);
''',
'''        var required = PlannerSubjectConstraint(mustDo);
        var excluded = PlannerSubjectConstraint(mustNotDo);
        if (!string.IsNullOrWhiteSpace(required)) sb.AppendLine("Publisher HARD requirements relevant to subject selection: " + required);
        if (!string.IsNullOrWhiteSpace(excluded)) sb.AppendLine("Publisher HARD exclusions relevant to subject selection: " + excluded);
''')

replace_once(planner,
'''    private static bool TryParseProposal(
''',
'''    private static string PlannerSubjectConstraint(string? value)
    {
        var kept = new List<string>();
        foreach (var raw in (value ?? string.Empty).Replace("\\r\\n", "\\n").Split('\\n'))
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

    private static bool TryParseProposal(
''')

workspace = 'src/Diez.Uno/VisualBookWorkspace.cs'
replace_once(workspace,
'''        var plannerState = document.ReadVisualSubjectPlanner();
        var plannerSummary = new TextBlock
        {
            Text = plannerState.Required
                ? plannerState.ProposalValid
                    ? "Proposta valida ricevuta:\\n" + string.Join("\\n", plannerState.Proposal.Select((x, i) => $"{i + 1}. {x.DisplayName}"))
                    : $"Tema di serie da risolvere: {plannerState.SeriesTheme}. {plannerState.ValidationMessage}"
                : "Il soggetto corrente non richiede un piano AI: è già atomico oppure viene gestito da Soggetti/Scene strutturati.",
            TextWrapping = TextWrapping.Wrap
        };
''',
'''        var plannerState = document.ReadVisualSubjectPlanner();

        string PlannerSummaryText()
        {
            if (!plannerState.Required)
                return "Piano soggetti risolto in Diez. I soggetti canonici sono già disponibili per la compilazione del Prompt immagini.";
            if (!plannerState.ProposalValid)
                return $"Tema di serie da risolvere: {plannerState.SeriesTheme}. {plannerState.ValidationMessage}";

            var lines = new List<string> { "Proposta AI da verificare prima di accettare:" };
            foreach (var item in plannerState.Proposal.Select((value, index) => (value, index)))
            {
                lines.Add($"{item.index + 1}. {item.value.DisplayName}");
                if (!string.IsNullOrWhiteSpace(item.value.Description))
                    lines.Add("   " + item.value.Description.Trim());
            }
            lines.Add(string.Empty);
            lines.Add("Nessun soggetto viene applicato o congelato finché non premi “Accetta proposta e congela i soggetti”.");
            return string.Join(Environment.NewLine, lines);
        }

        var plannerSummary = new TextBlock
        {
            Text = PlannerSummaryText(),
            TextWrapping = TextWrapping.Wrap
        };
''')

replace_once(workspace,
'''            if (!current.ProposalValid || !current.ProposalVersionId.HasValue)
            {
                report("Non c'è ancora una proposta soggetti valida da accettare.");
                return;
            }
''',
'''            if (!current.Required)
            {
                report("Il piano soggetti è già risolto in Diez.");
                return;
            }
            if (!current.ProposalValid || !current.ProposalVersionId.HasValue)
            {
                report("Non c'è ancora una proposta soggetti valida da accettare.");
                return;
            }
''')

replace_once(workspace,
'''        acceptProposal.IsEnabled = plannerState.ProposalValid && plannerState.ProposalVersionId.HasValue;
''',
'''        acceptProposal.IsEnabled = plannerState.Required && plannerState.ProposalValid && plannerState.ProposalVersionId.HasValue;
''')

replace_once(workspace,
'''                Text = "Se hai descritto una serie (per esempio “3 soggetti di Halloween”), il generatore immagini non deve scegliere i soggetti. Diez prepara prima una proposta strutturata con un'attività AI testuale, la valida e la applica allo stato canonico solo dopo la tua accettazione.",
''',
'''                Text = "Se hai descritto una serie (per esempio “3 soggetti di Halloween”), il generatore immagini non deve scegliere i soggetti. Diez prepara una proposta strutturata con un'attività AI testuale. Dopo l'import della risposta, qui vedi nomi e descrizioni trovati dall'AI prima di decidere se accettarli.",
''')

center = 'src/Diez.Uno/AiCenterWorkspace.cs'
replace_once(center,
'''        var jobModels = document.AiJobs().ToList();
        var jobs = new ListView
''',
'''        var jobModels = document.AiJobs().ToList();
        var visualPlannerState = BookTypeCatalog.IsVisual(document.BookType)
            ? document.ReadVisualSubjectPlanner()
            : null;
        var jobs = new ListView
''')

replace_once(center,
'''            var job = jobModels[jobs.SelectedIndex];
            selectedJob.Text = $"{job.Code} · {job.DisplayType} · {job.DisplayStatus}\\n{job.Title}";
            var image = string.Equals(job.OutputType, "Image", StringComparison.OrdinalIgnoreCase);
            response.IsEnabled = !image;
            response.PlaceholderText = image
                ? "Per le immagini importa il Response ZIP e usa Vision per l'approvazione HARD."
                : "Incolla qui la risposta ricevuta dall’AI.";
''',
'''            var job = jobModels[jobs.SelectedIndex];
            var plannerJob = (visualPlannerState?.PlannerJobId.HasValue == true && visualPlannerState.PlannerJobId.Value == job.JobId) ||
                             string.Equals(job.Title, "Diez · Piano soggetti visuali", StringComparison.OrdinalIgnoreCase);
            selectedJob.Text = plannerJob
                ? $"{job.Code} · {job.DisplayType} · {job.DisplayStatus}\\n{job.Title}\\nPasso corrente: esegui questo Prompt con l'AI, poi incolla qui sotto il JSON dei soggetti. Dopo l'import Diez ti riporta in Definizione per mostrarti la proposta prima dell'accettazione."
                : $"{job.Code} · {job.DisplayType} · {job.DisplayStatus}\\n{job.Title}";
            var image = string.Equals(job.OutputType, "Image", StringComparison.OrdinalIgnoreCase);
            response.IsEnabled = !image;
            response.PlaceholderText = image
                ? "Per le immagini importa il Response ZIP e usa Vision per l'approvazione HARD."
                : plannerJob
                    ? "Incolla qui SOLO il JSON restituito dal planner soggetti."
                    : "Incolla qui la risposta ricevuta dall’AI.";
''')

replace_once(center,
'''        jobs.SelectionChanged += (_, _) => RefreshSelectedJob();
        jobs.SelectedIndex = jobModels.Count > 0 ? 0 : -1;
        RefreshSelectedJob();
''',
'''        jobs.SelectionChanged += (_, _) => RefreshSelectedJob();
        var preferredPlannerIndex = visualPlannerState?.Required == true && visualPlannerState.PlannerJobId.HasValue
            ? jobModels.FindIndex(x => x.JobId == visualPlannerState.PlannerJobId.Value)
            : -1;
        jobs.SelectedIndex = preferredPlannerIndex >= 0 ? preferredPlannerIndex : jobModels.Count > 0 ? 0 : -1;
        RefreshSelectedJob();
''')

replace_once(center,
'''            var apiInfo = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var responseImportInfo = new TextBlock
''',
'''            var apiInfo = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var plannerGateInfo = new TextBlock
            {
                Text = visualPlannerState?.Required == true
                    ? visualPlannerState.ProposalValid
                        ? "Prompt Pack immagini BLOCCATO: la proposta AI è arrivata ma deve ancora essere verificata e accettata in Definizione."
                        : "Prompt Pack immagini BLOCCATO: completa prima il Piano soggetti Diez. Seleziona l'attività planner, copia il Prompt, eseguilo con l'AI, incolla il JSON qui sotto e scegli “Importa come candidato”."
                    : "Piano soggetti: risolto. Il Prompt Pack immagini può usare soggetti atomici congelati.",
                TextWrapping = TextWrapping.Wrap
            };
            var responseImportInfo = new TextBlock
''')

replace_once(center,
'''                namingInfo,
                Horizontal(
                    AsyncButton("Crea Prompt Pack ZIP · Manuale", async () =>
''',
'''                plannerGateInfo,
                namingInfo,
                Horizontal(
                    GuardedAsyncButton("Crea Prompt Pack ZIP · Manuale", visualPlannerState?.Required != true, async () =>
''')

replace_once(center,
'''                    document.SetUiString("AI.LastResponseDraft", response.Text);
                    var result = await document.IngestAiTextResultAsync(job.WorkUnitId.Value, response.Text);
                    if (result.Status is "IMPORTED" or "UPDATED" or "DUPLICATE")
                        document.SetUiString("AI.LastResponseDraft", "");
                    await save();
                    showAiCenter();
                    report(result.Message);
''',
'''                    var plannerJob = (visualPlannerState?.PlannerJobId.HasValue == true && visualPlannerState.PlannerJobId.Value == job.JobId) ||
                                     string.Equals(job.Title, "Diez · Piano soggetti visuali", StringComparison.OrdinalIgnoreCase);
                    document.SetUiString("AI.LastResponseDraft", response.Text);
                    var result = await document.IngestAiTextResultAsync(job.WorkUnitId.Value, response.Text);
                    if (result.Status is "IMPORTED" or "UPDATED" or "DUPLICATE")
                        document.SetUiString("AI.LastResponseDraft", "");
                    await save();
                    if (plannerJob && result.Status is "IMPORTED" or "UPDATED" or "DUPLICATE")
                    {
                        report(result.Message + " Proposta planner importata: torno in Definizione per mostrarti cosa ha trovato l'AI prima dell'accettazione.");
                        routeCurrentBook();
                        return;
                    }
                    showAiCenter();
                    report(result.Message);
''')

replace_once(center,
'''    private static Button AsyncButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8) };
        button.Click += async (_, _) => await action();
        return button;
    }
''',
'''    private static Button AsyncButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static Button GuardedAsyncButton(string text, bool enabled, Func<Task> action)
    {
        var button = AsyncButton(text, action);
        button.IsEnabled = enabled;
        return button;
    }
''')

test = 'tests/Diez.VisualSemanticRegression/Program.cs'
replace_once(test,
'''Require(!preparedPlanner.State.PlannerPrompt.Contains("generate images", StringComparison.OrdinalIgnoreCase) ||
        preparedPlanner.State.PlannerPrompt.Contains("Do not generate images", StringComparison.OrdinalIgnoreCase),
    "Il planner non deve trasformarsi in un renderer immagini.");
''',
'''Require(!preparedPlanner.State.PlannerPrompt.Contains("generate images", StringComparison.OrdinalIgnoreCase) ||
        preparedPlanner.State.PlannerPrompt.Contains("Do not generate images", StringComparison.OrdinalIgnoreCase),
    "Il planner non deve trasformarsi in un renderer immagini.");

var plannerJobCount = DiezAiExchangeBridge.ReadJobs(preparedPlanner.ProjectJson).Count;
var duplicatePlanner = DiezVisualSubjectPlannerFrontendBridge.Prepare(preparedPlanner.ProjectJson);
Require(duplicatePlanner.Status == "ALREADY_PREPARED",
    "Una seconda preparazione identica deve riusare il planner esistente: " + duplicatePlanner.Message);
Require(DiezAiExchangeBridge.ReadJobs(duplicatePlanner.ProjectJson).Count == plannerJobCount,
    "Una seconda preparazione identica non deve creare una seconda Work Unit planner.");

var layoutFilteredPlanner = DiezVisualSubjectPlannerFrontendBridge.Prepare(
    Save(NewProject(), "3 soggetti di Halloween"),
    mustNotDo: "generare un'unica image con 3 illustrazioni");
Require(layoutFilteredPlanner.Status == "PREPARED", "Il planner con esclusione layout deve prepararsi normalmente.");
Require(!layoutFilteredPlanner.State.PlannerPrompt.Contains("un'unica image", StringComparison.OrdinalIgnoreCase),
    "Un vincolo layout raw non deve contaminare il planner soggetti.");
Require(!layoutFilteredPlanner.State.PlannerPrompt.Contains("3 illustrazioni", StringComparison.OrdinalIgnoreCase),
    "La cardinalità/layout del canvas appartiene al renderer, non al planner soggetti.");
''')

replace_once(test,
'''Require(plannerReady.ProposalValid && plannerReady.Proposal.Count == 3,
    "Diez deve validare esattamente tre soggetti concreti prima dell'applicazione.");
''',
'''Require(plannerReady.Required && plannerReady.ProposalValid && plannerReady.Proposal.Count == 3,
    "Diez deve mantenere il Prompt Pack bloccato ma mostrare tre soggetti concreti prima dell'applicazione.");
var proposalAlreadyReady = DiezVisualSubjectPlannerFrontendBridge.Prepare(plannerIngest.ProjectJson);
Require(proposalAlreadyReady.Status == "PROPOSAL_READY",
    "Con una proposta valida già importata Diez deve chiedere la verifica utente, non creare un altro planner.");
''')

replace_once(test,
'''Require(appliedPlan.Status == "APPLIED", "L'accettazione utente deve congelare il piano soggetti: " + appliedPlan.Message);
var canonicalScene = DiezVisualSceneFrontendBridge.Read(appliedPlan.ProjectJson);
''',
'''Require(appliedPlan.Status == "APPLIED", "L'accettazione utente deve congelare il piano soggetti: " + appliedPlan.Message);
Require(!DiezVisualSubjectPlannerFrontendBridge.Read(appliedPlan.ProjectJson).Required,
    "Dopo l'accettazione il piano non deve restare segnato come irrisolto.");
var canonicalScene = DiezVisualSceneFrontendBridge.Read(appliedPlan.ProjectJson);
''')

print('Round 5.5 patch applied successfully.')
