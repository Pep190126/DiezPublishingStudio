from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def rep(path: str, old: str, new: str, count: int = 1):
    p = ROOT / path
    text = p.read_text(encoding="utf-8")
    actual = text.count(old)
    if actual < count:
        raise SystemExit(f"{path}: expected {count}, found {actual}: {old[:100]!r}")
    p.write_text(text.replace(old, new, count), encoding="utf-8")


# Planner bridge compile fix + dual visible/canonical semantics.
planner = "src/Diez.Core/DiezVisualSubjectPlannerFrontendBridge.cs"
rep(planner,
    "public sealed record DiezVisualSubjectProposalItemDto(\n    string DisplayName,\n    string CanonicalConcept,\n    string Description);",
    "public sealed record DiezVisualSubjectProposalItemDto(\n    string DisplayName,\n    string CanonicalConcept,\n    string Description,\n    string CanonicalDescription);")
rep(planner,
    "CanonicalConcept = item.CanonicalConcept,\n                    Description = item.Description,",
    "CanonicalConcept = item.CanonicalConcept,\n                    Description = item.Description,\n                    CanonicalDescription = item.CanonicalDescription,")
rep(planner,
    "new DiezColoringProfileDto(p.Style, p.BoldEasy, ColoringCozyPolicyStore.Load(project).Enabled,",
    "new DiezColoringProfileDto(p.Style, p.BoldEasy, ColoringCozyPolicyStore.Resolve(project),")
rep(planner,
    "sb.AppendLine(\"- `canonical_concept` is a concise technical-English semantic concept, not a finished image prompt.\");",
    "sb.AppendLine(\"- `canonical_concept` is a concise technical-English semantic concept, not a finished image prompt.\");\n        sb.AppendLine(\"- `canonical_description` is an optional technical-English semantic description used by the compiler; it must preserve the same facts as `description_it`.\");")
rep(planner,
    "sb.AppendLine(\"{\\\"subjects\\\":[{\\\"display_name_it\\\":\\\"...\\\",\\\"canonical_concept\\\":\\\"...\\\",\\\"description_it\\\":\\\"...\\\"}]}\");",
    "sb.AppendLine(\"{\\\"subjects\\\":[{\\\"display_name_it\\\":\\\"...\\\",\\\"canonical_concept\\\":\\\"...\\\",\\\"description_it\\\":\\\"...\\\",\\\"canonical_description\\\":\\\"...\\\"}]}\");")
rep(planner,
    "var description = Read(item, \"description_it\");\n                if (display.Length == 0 || concept.Length == 0)",
    "var description = Read(item, \"description_it\");\n                var canonicalDescription = Read(item, \"canonical_description\");\n                if (display.Length == 0 || concept.Length == 0)")
rep(planner,
    "items.Add(new DiezVisualSubjectProposalItemDto(display, concept, description));",
    "items.Add(new DiezVisualSubjectProposalItemDto(display, concept, description, canonicalDescription));")

# Canonical subject model: visible Italian label and provider-neutral semantic concept are separate.
subjects = "src/Diez.Core/MultiSubjectProfileService.cs"
rep(subjects,
    "public string Name { get; set; } = string.Empty;\n    public string Description { get; set; } = string.Empty;",
    "public string Name { get; set; } = string.Empty;\n    public string CanonicalConcept { get; set; } = string.Empty;\n    public string Description { get; set; } = string.Empty;\n    public string CanonicalDescription { get; set; } = string.Empty;")
rep(subjects,
    "subject.Description ??= string.Empty;\n            subject.Consistency ??=",
    "subject.CanonicalConcept ??= string.Empty;\n            subject.Description ??= string.Empty;\n            subject.CanonicalDescription ??= string.Empty;\n            subject.Consistency ??=")

# If the user edits a planner-produced subject, stale planner semantics must not silently survive.
scene_bridge = "src/Diez.Core/DiezVisualSceneFrontendBridge.cs"
rep(scene_bridge,
    "subject.Description = (description ?? string.Empty).Trim();\n        model.ActiveSubjectId = subject.SubjectId;",
    "subject.Description = (description ?? string.Empty).Trim();\n        subject.CanonicalConcept = string.Empty;\n        subject.CanonicalDescription = string.Empty;\n        model.ActiveSubjectId = subject.SubjectId;")

# Renderer consumes the canonical concept produced by the semantic planner, while the UI keeps Italian labels.
hard = "src/Diez.Core/DiezVisualHardPromptFrontendBridge.cs"
rep(hard,
    "var subject = focal?.Name?.Trim();\n        var hasAtomicSubject = !string.IsNullOrWhiteSpace(subject);",
    "var subject = ProviderSubject(focal);\n        var hasAtomicSubject = !string.IsNullOrWhiteSpace(subject);")
rep(hard,
    "if (!string.IsNullOrWhiteSpace(subject.Description))\n            sb.AppendLine(\"SUBJECT IDENTITY — HARD LOCK: \" + subject.Description.Trim() + \" Preserve these identifying traits whenever this subject appears.\");",
    "var semanticDescription = string.IsNullOrWhiteSpace(subject.CanonicalDescription) ? subject.Description : subject.CanonicalDescription;\n        if (!string.IsNullOrWhiteSpace(semanticDescription))\n            sb.AppendLine(\"SUBJECT IDENTITY — HARD LOCK: \" + semanticDescription.Trim() + \" Preserve these identifying traits whenever this subject appears.\");")
rep(hard,
    "sb.AppendLine(\"SCENE PARTICIPANTS — HARD LOCK: \" + string.Join(\", \", participants.Select(x => x.Name)) + \". Every listed participant must visibly appear in this SAME scene. Do not omit, merge, replace or substitute any listed participant with another subject.\");",
    "sb.AppendLine(\"SCENE PARTICIPANTS — HARD LOCK: \" + string.Join(\", \", participants.Select(ProviderSubject)) + \". Every listed participant must visibly appear in this SAME scene. Do not omit, merge, replace or substitute any listed participant with another subject.\");")
rep(hard,
    "if (!string.IsNullOrWhiteSpace(participant.Description))\n                sb.AppendLine($\"PARTICIPANT IDENTITY — HARD LOCK [{participant.Name}]: {participant.Description.Trim()} Preserve these identifying traits in this scene.\");",
    "var participantDescription = string.IsNullOrWhiteSpace(participant.CanonicalDescription) ? participant.Description : participant.CanonicalDescription;\n            if (!string.IsNullOrWhiteSpace(participantDescription))\n                sb.AppendLine($\"PARTICIPANT IDENTITY — HARD LOCK [{ProviderSubject(participant)}]: {participantDescription.Trim()} Preserve these identifying traits in this scene.\");")
rep(hard,
    "    private static void AppendSubject(StringBuilder sb, MultiSubjectDefinition? subject, bool consistent)",
    "    private static string ProviderSubject(MultiSubjectDefinition? subject)\n    {\n        if (subject is null) return string.Empty;\n        var canonical = (subject.CanonicalConcept ?? string.Empty).Trim();\n        return canonical.Length > 0 ? canonical : (subject.Name ?? string.Empty).Trim();\n    }\n\n    private static void AppendSubject(StringBuilder sb, MultiSubjectDefinition? subject, bool consistent)")

# Uno adapter exposes planner + custom-style bridges without UI-owned canonical state.
adapter = "src/Diez.Uno/VisualBookDocumentAdapter.cs"
rep(adapter,
    "    public static DiezVisualSceneStateDto ReadVisualSceneState(this DiezProjectDocument document) =>\n        DiezVisualSceneFrontendBridge.Read(document.ExportProjectJson());\n",
    "    public static DiezVisualSceneStateDto ReadVisualSceneState(this DiezProjectDocument document) =>\n        DiezVisualSceneFrontendBridge.Read(document.ExportProjectJson());\n\n    public static DiezVisualSubjectPlannerStateDto ReadVisualSubjectPlanner(this DiezProjectDocument document) =>\n        DiezVisualSubjectPlannerFrontendBridge.Read(document.ExportProjectJson());\n\n    public static DiezVisualSubjectPlannerMutation PrepareVisualSubjectPlanner(this DiezProjectDocument document, string? mustDo, string? mustNotDo)\n    {\n        var result = DiezVisualSubjectPlannerFrontendBridge.Prepare(document.ExportProjectJson(), mustDo, mustNotDo);\n        if (result.Status == \"PREPARED\") ApplyCoreJson(document, result.ProjectJson);\n        return result;\n    }\n\n    public static DiezVisualSubjectPlannerMutation ApplyVisualSubjectPlannerProposal(this DiezProjectDocument document, Guid versionId)\n    {\n        var result = DiezVisualSubjectPlannerFrontendBridge.ApplyProposal(document.ExportProjectJson(), versionId);\n        if (result.Status == \"APPLIED\") ApplyCoreJson(document, result.ProjectJson);\n        return result;\n    }\n\n    public static DiezColoringCustomStyleStateDto ReadColoringCustomStyle(this DiezProjectDocument document) =>\n        DiezColoringCustomStyleFrontendBridge.Read(document.ExportProjectJson());\n\n    public static string ResolveColoringCustomLibraryDefinition(this DiezProjectDocument document, string? label) =>\n        DiezColoringCustomStyleFrontendBridge.ResolveLibraryDefinition(label);\n\n    public static DiezColoringCustomStyleMutation SaveColoringCustomStyle(this DiezProjectDocument document, bool active, string? definition, bool archiveInLibrary)\n    {\n        var result = DiezColoringCustomStyleFrontendBridge.Save(document.ExportProjectJson(), active, definition, archiveInLibrary);\n        if (result.Status == \"SAVED\") ApplyCoreJson(document, result.ProjectJson);\n        return result;\n    }\n")

# Ai Center: the selected planner TEXT Work Unit must be executable with today's manual copy/paste transport.
ai = "src/Diez.Uno/AiCenterWorkspace.cs"
rep(ai,
    "        root.Children.Add(Card(\"Attività AI\", Vertical(jobs, selectedJob)));",
    "        root.Children.Add(Card(\"Attività AI\", Vertical(\n            jobs,\n            selectedJob,\n            ActionButton(\"Copia Prompt attività selezionata\", () =>\n            {\n                if (!TrySelectedJob(jobs, jobModels, report, out var selected)) return;\n                CopyText(selected.Prompt ?? string.Empty);\n                report($\"Prompt {selected.Code} copiato.\");\n            }))));")

# Visual workspace: pass AI-center navigation into Definition.
workspace = "src/Diez.Uno/VisualBookWorkspace.cs"
rep(workspace,
    "BuildPhaseOne(root, document, type, save, report, refresh, GoToPhaseAsync);",
    "BuildPhaseOne(root, document, type, save, report, refresh, showAiCenter, GoToPhaseAsync);")
rep(workspace,
    "        Action<string> report,\n        Action refresh,\n        Func<int, Task> goToPhase)\n    {\n        var setup = document.ReadVisualSetup();",
    "        Action<string> report,\n        Action refresh,\n        Action showAiCenter,\n        Func<int, Task> goToPhase)\n    {\n        var setup = document.ReadVisualSetup();", 1)

# Custom style: include reusable archived labels, reveal definition + explicit archive decision only for Custom choices.
rep(workspace,
    "            var style = Combo(ColoringStyles, FirstNonBlank(document.GetUiString(\"Coloring.Style\"), p.Style));",
    "            var customStyleState = document.ReadColoringCustomStyle();\n            var styleOptions = ColoringStyles.Concat(customStyleState.LibraryLabels).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();\n            var initialStyle = customStyleState.IsActive ? \"Custom\" : FirstNonBlank(document.GetUiString(\"Coloring.Style\"), p.Style);\n            var style = Combo(styleOptions, initialStyle);\n            var customDefinition = Editor(customStyleState.Definition, \"Descrivi lo stile visivo che vuoi ottenere\", 100);\n            var customOnlyProject = new RadioButton\n            {\n                GroupName = \"ColoringCustomStyleStorage\",\n                Content = \"Usa solo in questo progetto\",\n                IsChecked = !customStyleState.ArchiveInLibrary\n            };\n            var customArchive = new RadioButton\n            {\n                GroupName = \"ColoringCustomStyleStorage\",\n                Content = \"Salva anche nella libreria degli stili\",\n                IsChecked = customStyleState.ArchiveInLibrary\n            };\n            var customStylePanel = Vertical(\n                Labeled(\"Definizione stile Custom\", customDefinition),\n                new TextBlock { Text = \"Decidi esplicitamente se questo stile resta solo nel progetto oppure diventa riutilizzabile anche negli altri progetti.\", TextWrapping = TextWrapping.Wrap },\n                WrapRow(customOnlyProject, customArchive));\n\n            bool IsCustomStyleChoice()\n            {\n                var selected = Selected(style, \"Clean Line Art\");\n                return string.Equals(selected, \"Custom\", StringComparison.OrdinalIgnoreCase) ||\n                       selected.StartsWith(\"Custom —\", StringComparison.OrdinalIgnoreCase);\n            }\n\n            void RefreshCustomStyle()\n            {\n                var selected = Selected(style, \"Clean Line Art\");\n                if (selected.StartsWith(\"Custom —\", StringComparison.OrdinalIgnoreCase))\n                {\n                    var resolved = document.ResolveColoringCustomLibraryDefinition(selected);\n                    if (!string.IsNullOrWhiteSpace(resolved)) customDefinition.Text = resolved;\n                    customArchive.IsChecked = true;\n                }\n                customStylePanel.Visibility = IsCustomStyleChoice() ? Visibility.Visible : Visibility.Collapsed;\n            }\n            style.SelectionChanged += (_, _) => RefreshCustomStyle();\n            RefreshCustomStyle();")
rep(workspace,
    "            var notes = Editor(p.Notes, \"Note stile / eccezioni\", 80);",
    "            var notes = Editor(customStyleState.IsActive ? string.Empty : p.Notes, \"Note stile / eccezioni\", 80);")
rep(workspace,
    "                Selected(style, \"Clean Line Art\"),\n                boldEasy.IsChecked == true,",
    "                IsCustomStyleChoice() ? \"Custom\" : Selected(style, \"Clean Line Art\"),\n                boldEasy.IsChecked == true,")
rep(workspace,
    "                Labeled(\"Stile\", style),\n                WrapRow(Labeled(\"Pubblico\", audience), Labeled(\"Difficoltà\", difficulty)),",
    "                Labeled(\"Stile\", style),\n                customStylePanel,\n                WrapRow(Labeled(\"Pubblico\", audience), Labeled(\"Difficoltà\", difficulty)),")

# Save project-local Custom decision immediately after canonical Coloring setup.
rep(workspace,
    "                document.SaveColoringSetup(\n                    parsed, subject.Text, environment.Text,\n                    consistent.IsChecked == true,\n                    consistent.IsChecked == true ? consistencyRules.Text : string.Empty,\n                    coloringProfile());",
    "                document.SaveColoringSetup(\n                    parsed, subject.Text, environment.Text,\n                    consistent.IsChecked == true,\n                    consistent.IsChecked == true ? consistencyRules.Text : string.Empty,\n                    coloringProfile());\n\n                var customChoice = IsCustomStyleChoice();\n                var customSaved = document.SaveColoringCustomStyle(\n                    customChoice,\n                    customChoice ? customDefinition.Text : string.Empty,\n                    customChoice && customArchive.IsChecked == true);\n                if (customSaved.Status == \"INVALID\")\n                {\n                    report(customSaved.Message);\n                    customDefinition.Focus(FocusState.Programmatic);\n                    return false;\n                }")

# Planner card: manual TEXT planner -> import candidate in AI Center -> explicit canonical apply.
marker = """        root.Children.Add(NavigationRow(\n            null,\n            AsyncButton(\"Salva e continua → Prompt\", async () =>\n"""
planner_card = """        var plannerState = document.ReadVisualSubjectPlanner();\n        var plannerSummary = new TextBlock\n        {\n            Text = plannerState.Required\n                ? plannerState.ProposalValid\n                    ? \"Proposta valida ricevuta:\\n\" + string.Join(\"\\n\", plannerState.Proposal.Select((x, i) => $\"{i + 1}. {x.DisplayName}\"))\n                    : $\"Tema di serie da risolvere: {plannerState.SeriesTheme}. {plannerState.ValidationMessage}\"\n                : \"Il soggetto corrente non richiede un piano AI: è già atomico oppure viene gestito da Soggetti/Scene strutturati.\",\n            TextWrapping = TextWrapping.Wrap\n        };\n        var acceptProposal = AsyncButton(\"Accetta proposta e congela i soggetti\", async () =>\n        {\n            var current = document.ReadVisualSubjectPlanner();\n            if (!current.ProposalValid || !current.ProposalVersionId.HasValue)\n            {\n                report(\"Non c'è ancora una proposta soggetti valida da accettare.\");\n                return;\n            }\n            var applied = document.ApplyVisualSubjectPlannerProposal(current.ProposalVersionId.Value);\n            await save();\n            report(applied.Message);\n            if (applied.Status == \"APPLIED\") refresh();\n        });\n        acceptProposal.IsEnabled = plannerState.ProposalValid && plannerState.ProposalVersionId.HasValue;\n\n        root.Children.Add(Card(\"Piano soggetti Diez · prima del Prompt\", Vertical(\n            new TextBlock\n            {\n                Text = \"Se hai descritto una serie (per esempio “3 soggetti di Halloween”), il generatore immagini non deve scegliere i soggetti. Diez prepara prima una proposta strutturata con un'attività AI testuale, la valida e la applica allo stato canonico solo dopo la tua accettazione.\",\n                TextWrapping = TextWrapping.Wrap\n            },\n            plannerSummary,\n            WrapRow(\n                AsyncButton(\"Prepara proposta soggetti con AI\", async () =>\n                {\n                    if (!await SaveSetupAsync()) return;\n                    var prepared = document.PrepareVisualSubjectPlanner(\n                        document.GetUiString(\"Prompt.MustDo\"),\n                        document.GetUiString(\"Prompt.MustNotDo\"));\n                    await save();\n                    report(prepared.Message);\n                    if (prepared.Status == \"PREPARED\") showAiCenter();\n                }),\n                acceptProposal,\n                ActionButton(\"Apri Produzione con AI\", showAiCenter)))));\n\n""" + marker
rep(workspace, marker, planner_card)

# Regression: planner creates TEXT work unit, validates structured JSON, applies fresh canonical subjects,
# then the same visual compiler that previously blocked now emits atomic Work Units.
test = "tests/Diez.VisualSemanticRegression/Program.cs"
append_before = 'Console.WriteLine("VISUAL_SEMANTIC_REGRESSION_OK");'
new_tests = r'''
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
Require(plannerReady.ProposalValid && plannerReady.Proposal.Count == 3,
    "Diez deve validare esattamente tre soggetti concreti prima dell'applicazione.");
var appliedPlan = DiezVisualSubjectPlannerFrontendBridge.ApplyProposal(plannerIngest.ProjectJson, plannerIngest.Version!.VersionId);
Require(appliedPlan.Status == "APPLIED", "L'accettazione utente deve congelare il piano soggetti: " + appliedPlan.Message);
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

'''
rep(test, append_before, new_tests + append_before)

print("ROUND54_PATCH_APPLIED")
