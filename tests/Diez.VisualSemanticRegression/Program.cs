using System.Text.Json;
using System.Text.Json.Nodes;
using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string NewProject()
{
    var root = new JsonObject
    {
        ["Format"] = "diez-project-package",
        ["SchemaVersion"] = 10,
        ["Name"] = "Semantic regression",
        ["SavedAtLocal"] = "",
        ["ProjectId"] = Guid.NewGuid().ToString(),
        ["EditionMetadata"] = new JsonObject { ["Title"] = "Semantic regression", ["Language"] = "it" },
        ["AiProduction"] = new JsonObject { ["SchemaVersion"] = 1, ["ProjectBrief"] = "" },
        ["AiProductionJobs"] = new JsonArray(),
        ["Materials"] = new JsonArray(),
        ["ContentNodes"] = new JsonArray(),
        ["IllustrationPlacements"] = new JsonArray(),
        ["Entities"] = new JsonArray
        {
            new JsonObject
            {
                ["EntityId"] = Guid.NewGuid().ToString(),
                ["Kind"] = "DiezBookType",
                ["Name"] = BookTypeCatalog.ColoringBook,
                ["IsCandidate"] = false,
                ["Notes"] = ""
            }
        },
        ["Relations"] = new JsonArray(),
        ["BibleEntries"] = new JsonArray(),
        ["ConsistencyFacts"] = new JsonArray(),
        ["ConsistencyIssues"] = new JsonArray(),
        ["ConsistencyResolutions"] = new JsonArray(),
        ["RevisionCandidates"] = new JsonArray()
    };
    return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

static string Save(string json, string subject)
{
    return DiezVisualBookFrontendBridge.SaveColoring(
        json,
        3,
        subject,
        "Halloween",
        consistent: false,
        string.Empty,
        new DiezColoringProfileDto(
            "Cute & Playful",
            BoldEasy: true,
            Cozy: true,
            "Bambini 6–9 anni",
            "Facile",
            "Medio",
            "Bassa",
            "Bassa",
            "Dettagliato",
            "Ampio",
            ClosedAreas: true,
            AvoidTinyAreas: true,
            CleanContours: true,
            NoTextInsideImage: true,
            SubjectClearlySeparated: true,
            "")).ProjectJson;
}

static void RequireUnresolved(string subject)
{
    var json = Save(NewProject(), subject);
    try
    {
        _ = DiezVisualBookFrontendBridge.BuildPromptPack(json);
        throw new InvalidOperationException("Un tema aggregato non risolto non deve arrivare al renderer.");
    }
    catch (InvalidOperationException ex)
    {
        Require(ex.Message.Contains("Piano soggetti non risolto", StringComparison.OrdinalIgnoreCase),
            "Il blocco deve spiegare che il piano soggetti va risolto in Diez. Messaggio: " + ex.Message);
    }
}

RequireUnresolved("3 soggetti di Halloween");
RequireUnresolved("3 images di soggetti per Halloween");

var structured = Save(NewProject(), "3 soggetti di Halloween");
var configured = DiezVisualSceneFrontendBridge.ConfigureSubjects(structured, true, 3);
structured = configured.ProjectJson;
var state = configured.State;
var names = new[]
{
    "Zucca jack-o'-lantern simpatica",
    "Fantasma amichevole e sorridente",
    "Gatto con cappello da strega"
};
for (var i = 0; i < 3; i++)
{
    var mutation = DiezVisualSceneFrontendBridge.SaveSubject(
        structured,
        state.Subjects[i].SubjectId,
        names[i],
        names[i] + " come singolo soggetto focale riconoscibile.");
    structured = mutation.ProjectJson;
    state = mutation.State;
}

var pack = DiezVisualBookFrontendBridge.BuildPromptPack(structured);
Require(pack.Items.Count == 3, "Tre soggetti risolti devono produrre tre prompt atomici.");
foreach (var item in pack.Items)
{
    Require(!item.Prompt.Contains("SERIES SUBJECT ASSIGNMENT", StringComparison.OrdinalIgnoreCase),
        "Il renderer non deve più ricevere istruzioni per scegliere il soggetto della serie.");
    Require(!item.Prompt.Contains("3 images di soggetti", StringComparison.OrdinalIgnoreCase),
        "Il residuo legacy non deve contaminare il renderer.");
}

var jobs = DiezVisualJobFrontendBridge.SyncReadyJobs(structured);
Require(jobs.Success, "Le Work Unit strutturate devono poter essere sincronizzate: " + jobs.Message);
var packagePrompt = DiezPromptPackBatchFrontendBridge.BuildPackagePrompt(jobs.ProjectJson);
Require(!packagePrompt.Contains("crea PRIMA un piano soggetti", StringComparison.OrdinalIgnoreCase),
    "PROMPT.md non deve delegare al provider la pianificazione dei soggetti.");
Require(!packagePrompt.Contains("Il lotto contiene", StringComparison.OrdinalIgnoreCase),
    "Le istruzioni provider-facing del Prompt Pack devono essere in inglese tecnico, non UI italiana.");
Require(packagePrompt.Contains("project_id", StringComparison.OrdinalIgnoreCase) &&
        packagePrompt.Contains("request_snapshot_id", StringComparison.OrdinalIgnoreCase),
    "Il contratto Response deve richiedere esplicitamente gli identificatori di trasporto.");


// Round 5.4: aggregate subject planning is a TEXT AI Exchange step before image rendering.
var plannerProject = Save(NewProject(), "3 soggetti di Halloween");
var preparedPlanner = DiezVisualSubjectPlannerFrontendBridge.Prepare(plannerProject);
Require(preparedPlanner.Status == "PREPARED", "Il planner deve creare un'attività AI testuale: " + preparedPlanner.Message);
Require(preparedPlanner.State.PlannerWorkUnitId.HasValue, "Il planner deve avere una Work Unit AI Exchange.");
Require(preparedPlanner.State.PlannerPrompt.Contains("DIEZ SEMANTIC SUBJECT PLANNER", StringComparison.Ordinal),
    "Il prompt planner deve essere distinto dal prompt renderer immagini.");
Require(preparedPlanner.State.PlannerPrompt.Contains("JSON ONLY", StringComparison.OrdinalIgnoreCase),
    "Il planner deve chiedere un contratto strutturato, non prosa libera.");
Require(!preparedPlanner.State.PlannerPrompt.Contains("generate images", StringComparison.OrdinalIgnoreCase) ||
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

var plannerJson = """
{"subjects":[
  {"display_name_it":"Zucca jack-o'-lantern simpatica","canonical_concept":"friendly jack-o'-lantern pumpkin","description_it":"Una grande zucca sorridente e riconoscibile.","canonical_description":"A large friendly smiling jack-o'-lantern pumpkin."},
  {"display_name_it":"Fantasma amichevole","canonical_concept":"friendly smiling ghost","description_it":"Un singolo fantasma simpatico e ben leggibile.","canonical_description":"One friendly smiling ghost with a clear simple silhouette."},
  {"display_name_it":"Gatto con cappello da strega","canonical_concept":"cute cat wearing a witch hat","description_it":"Un gatto simpatico con cappello da strega.","canonical_description":"A cute cat wearing a witch hat."}
]}
""";
var plannerIngest = await DiezAiExchangeBridge.IngestTextResultAsync(
    preparedPlanner.ProjectJson,
    preparedPlanner.State.PlannerWorkUnitId!.Value,
    plannerJson);
Require(plannerIngest.Status is "IMPORTED" or "UPDATED", "La proposta planner deve entrare come Candidate testuale: " + plannerIngest.Message);
Require(plannerIngest.Version is not null, "La proposta planner deve creare una versione candidata.");

var plannerReady = DiezVisualSubjectPlannerFrontendBridge.Read(plannerIngest.ProjectJson);
Require(plannerReady.Required && plannerReady.ProposalValid && plannerReady.Proposal.Count == 3,
    "Diez deve mantenere il Prompt Pack bloccato ma mostrare tre soggetti concreti prima dell'applicazione.");
var proposalAlreadyReady = DiezVisualSubjectPlannerFrontendBridge.Prepare(plannerIngest.ProjectJson);
Require(proposalAlreadyReady.Status == "PROPOSAL_READY",
    "Con una proposta valida già importata Diez deve chiedere la verifica utente, non creare un altro planner.");
var appliedPlan = DiezVisualSubjectPlannerFrontendBridge.ApplyProposal(plannerIngest.ProjectJson, plannerIngest.Version!.VersionId);
Require(appliedPlan.Status == "APPLIED", "L'accettazione utente deve congelare il piano soggetti: " + appliedPlan.Message);
Require(!DiezVisualSubjectPlannerFrontendBridge.Read(appliedPlan.ProjectJson).Required,
    "Dopo l'accettazione il piano non deve restare segnato come irrisolto.");
var canonicalScene = DiezVisualSceneFrontendBridge.Read(appliedPlan.ProjectJson);
Require(canonicalScene.MultiSubjectEnabled && canonicalScene.Subjects.Count == 3,
    "La proposta accettata deve diventare stato canonico Soggetti, non testo del prompt.");
Require(canonicalScene.Subjects.Select(x => x.SubjectId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3,
    "Ogni soggetto risolto deve avere un SubjectId distinto.");

var plannedPack = DiezVisualBookFrontendBridge.BuildPromptPack(appliedPlan.ProjectJson);
Require(plannedPack.Items.Count == 3, "Il piano accettato deve sbloccare tre Work Unit immagine.");
Require(plannedPack.Items[0].Prompt.Contains("friendly jack-o'-lantern pumpkin", StringComparison.OrdinalIgnoreCase),
    "Il renderer deve ricevere il concetto canonico, non l'etichetta UI italiana.");
Require(!plannedPack.Items[0].Prompt.Contains("Zucca jack-o'-lantern simpatica", StringComparison.OrdinalIgnoreCase),
    "L'etichetta italiana visibile non deve essere la sorgente testuale del renderer.");
Require(plannedPack.Items.All(x => !x.Prompt.Contains("3 soggetti di Halloween", StringComparison.OrdinalIgnoreCase)),
    "Il tema aggregato non deve sopravvivere come soggetto nelle Work Unit immagine.");

var malformedPlanner = """{"subjects":[{"display_name_it":"Fantasma","canonical_concept":"ghost","description_it":"","canonical_description":""}]}""";
Require(!DiezVisualSubjectPlannerFrontendBridge.TryValidateProposal(malformedPlanner, 3, out _),
    "Una proposta con numero errato di soggetti deve essere respinta.");

// Round 5.5: the visible AI proposal is editable before acceptance.
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
