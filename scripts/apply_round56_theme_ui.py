from pathlib import Path


def replace_once(path: str, old: str, new: str):
    p = Path(path)
    text = p.read_text(encoding='utf-8')
    if old not in text:
        raise SystemExit(f'pattern not found in {path}: {old[:120]!r}')
    text = text.replace(old, new, 1)
    p.write_text(text, encoding='utf-8')


# 1) Uno document adapter: bridge the new theme service.
adapter = 'src/Diez.Uno/VisualBookDocumentAdapter.cs'
anchor = '''    public static DiezColoringCustomStyleStateDto ReadColoringCustomStyle(this DiezProjectDocument document) =>
        DiezColoringCustomStyleFrontendBridge.Read(document.ExportProjectJson());
'''
theme_methods = '''    public static DiezVisualThemeStateDto ReadVisualTheme(this DiezProjectDocument document) =>
        DiezVisualThemeFrontendBridge.Read(document.ExportProjectJson());

    public static DiezVisualThemeMutation SelectVisualTheme(this DiezProjectDocument document, string? themeId, string? customThemeName, bool archiveCustomTheme)
    {
        var result = DiezVisualThemeFrontendBridge.SelectTheme(document.ExportProjectJson(), themeId, customThemeName, archiveCustomTheme);
        if (result.Status == "SELECTED") ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualThemeMutation ProposeVisualThemeSubjects(
        this DiezProjectDocument document,
        string? themeId,
        string? customThemeName,
        bool archiveCustomTheme,
        bool regenerate,
        string? mustDo,
        string? mustNotDo)
    {
        var result = DiezVisualThemeFrontendBridge.Propose(
            document.ExportProjectJson(), themeId, customThemeName, archiveCustomTheme, regenerate, mustDo, mustNotDo);
        if (result.Status is "PROPOSED" or "POOL_TOO_SMALL") ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualThemeMutation SaveVisualThemeProposalEdits(
        this DiezProjectDocument document,
        IEnumerable<DiezVisualThemeProposalEditDto> edits)
    {
        var result = DiezVisualThemeFrontendBridge.SaveProposalEdits(document.ExportProjectJson(), edits);
        if (result.Status == "EDITED") ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualThemeMutation AddVisualThemeCustomSubject(
        this DiezProjectDocument document,
        string? themeId,
        string? customThemeName,
        bool archiveCustomTheme,
        string? displayName,
        string? description,
        bool archiveInThemeLibrary)
    {
        var result = DiezVisualThemeFrontendBridge.AddCustomSubject(
            document.ExportProjectJson(), themeId, customThemeName, archiveCustomTheme,
            displayName, description, archiveInThemeLibrary);
        if (result.Status == "SUBJECT_ADDED") ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

    public static DiezVisualThemeMutation AcceptVisualThemeProposal(this DiezProjectDocument document)
    {
        var result = DiezVisualThemeFrontendBridge.AcceptProposal(document.ExportProjectJson());
        if (result.Status == "ACCEPTED") ApplyCoreJson(document, result.ProjectJson);
        return result;
    }

''' + anchor
replace_once(adapter, anchor, theme_methods)


# 2) Visual workspace: remove manual TEXT-planner UX and mount the theme library instead.
workspace = 'src/Diez.Uno/VisualBookWorkspace.cs'
p = Path(workspace)
text = p.read_text(encoding='utf-8')
start = text.find('        var plannerState = document.ReadVisualSubjectPlanner();')
end = text.find('        root.Children.Add(NavigationRow(', start)
if start < 0 or end < 0:
    raise SystemExit('planner UI block not found in VisualBookWorkspace.cs')
replacement = '''        root.Children.Add(VisualThemeWorkspace.Build(
            document,
            save,
            report,
            refresh,
            SaveSetupAsync));

'''
text = text[:start] + replacement + text[end:]
p.write_text(text, encoding='utf-8')


# 3) AI Center: hide legacy planner jobs from normal UX and point the semantic gate back to Definition/themes.
aicenter = 'src/Diez.Uno/AiCenterWorkspace.cs'
replace_once(
    aicenter,
    '        var jobModels = document.AiJobs().ToList();\n',
    '''        var jobModels = document.AiJobs()\n            .Where(x => !string.Equals(x.Title, "Diez · Piano soggetti visuali", StringComparison.OrdinalIgnoreCase))\n            .ToList();\n''')

p = Path(aicenter)
text = p.read_text(encoding='utf-8')
old_block = '''            var job = jobModels[jobs.SelectedIndex];
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
'''
new_block = '''            var job = jobModels[jobs.SelectedIndex];
            selectedJob.Text = $"{job.Code} · {job.DisplayType} · {job.DisplayStatus}\\n{job.Title}";
            var image = string.Equals(job.OutputType, "Image", StringComparison.OrdinalIgnoreCase);
            response.IsEnabled = !image;
            response.PlaceholderText = image
                ? "Per le immagini importa il Response ZIP e usa Vision per l'approvazione HARD."
                : "Incolla qui la risposta ricevuta dall’AI per questa attività editoriale.";
'''
if old_block not in text:
    raise SystemExit('planner selected-job block not found in AiCenterWorkspace.cs')
text = text.replace(old_block, new_block, 1)

old_select = '''        jobs.SelectionChanged += (_, _) => RefreshSelectedJob();
        var preferredPlannerIndex = visualPlannerState?.Required == true && visualPlannerState.PlannerJobId.HasValue
            ? jobModels.FindIndex(x => x.JobId == visualPlannerState.PlannerJobId.Value)
            : -1;
        jobs.SelectedIndex = preferredPlannerIndex >= 0 ? preferredPlannerIndex : jobModels.Count > 0 ? 0 : -1;
        RefreshSelectedJob();
'''
new_select = '''        jobs.SelectionChanged += (_, _) => RefreshSelectedJob();
        jobs.SelectedIndex = jobModels.Count > 0 ? 0 : -1;
        RefreshSelectedJob();
'''
if old_select not in text:
    raise SystemExit('planner preferred-selection block not found in AiCenterWorkspace.cs')
text = text.replace(old_select, new_select, 1)

old_gate = '''                Text = visualPlannerState?.Required == true
                    ? visualPlannerState.ProposalValid
                        ? "Prompt Pack immagini BLOCCATO: la proposta AI è arrivata ma deve ancora essere verificata e accettata in Definizione."
                        : "Prompt Pack immagini BLOCCATO: completa prima il Piano soggetti Diez. Seleziona l'attività planner, copia il Prompt, eseguilo con l'AI, incolla il JSON qui sotto e scegli “Importa come candidato”."
                    : "Piano soggetti: risolto. Il Prompt Pack immagini può usare soggetti atomici congelati.",
'''
new_gate = '''                Text = visualPlannerState?.Required == true
                    ? "Prompt Pack immagini BLOCCATO: torna in Definizione e completa Tema → Proponi soggetti → controlla/modifica → Accetta e congela i soggetti. Non serve copiare Prompt planner né incollare JSON."
                    : "Piano soggetti: risolto. Il Prompt Pack immagini può usare soggetti atomici congelati.",
'''
if old_gate not in text:
    raise SystemExit('planner gate text not found in AiCenterWorkspace.cs')
text = text.replace(old_gate, new_gate, 1)
p.write_text(text, encoding='utf-8')


# 4) Semantic regression: keep old planner coverage as migration guard, add Round 5.6 local theme path.
test = 'tests/Diez.VisualSemanticRegression/Program.cs'
p = Path(test)
text = p.read_text(encoding='utf-8')
needle = 'Console.WriteLine("VISUAL_SEMANTIC_REGRESSION_OK");'
if needle not in text:
    raise SystemExit('semantic regression completion marker not found')
round56 = r'''
// Round 5.6: ordinary UX resolves themes locally. No planner prompt, JSON or AI Exchange job is required.
var themeProject = Save(NewProject(), "3 soggetti di Halloween");
var themeInitial = DiezVisualThemeFrontendBridge.Read(themeProject);
Require(themeInitial.Themes.Any(x => x.ThemeId == "halloween") && themeInitial.Themes.Any(x => x.ThemeId == "jungle") && themeInitial.Themes.Any(x => x.ThemeId == "christmas"),
    "La libreria temi deve esporre almeno Halloween, Animali della giungla e Natale/Christmas.");
var jobsBeforeTheme = DiezAiExchangeBridge.ReadJobs(themeProject).Count;
var localTheme = DiezVisualThemeFrontendBridge.Propose(themeProject, "halloween", null, false, regenerate: false);
Require(localTheme.Status == "PROPOSED", "Halloween deve produrre una proposta locale senza AI: " + localTheme.Message);
Require(localTheme.State.Proposal.Count == 3 && localTheme.State.Proposal.Select(x => x.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3,
    "Diez deve proporre esattamente tre soggetti Halloween distinti.");
Require(DiezAiExchangeBridge.ReadJobs(localTheme.ProjectJson).Count == jobsBeforeTheme,
    "La proposta di un tema BUILTIN non deve creare Work Unit planner o altri job AI.");
var stableTheme = DiezVisualThemeFrontendBridge.Propose(localTheme.ProjectJson, "halloween", null, false, regenerate: false);
Require(string.Join("|", stableTheme.State.Proposal.Select(x => x.DisplayName)) == string.Join("|", localTheme.State.Proposal.Select(x => x.DisplayName)),
    "Senza Rigenera, lo stesso stato semantico deve produrre una proposta stabile.");
var regeneratedTheme = DiezVisualThemeFrontendBridge.Propose(stableTheme.ProjectJson, "halloween", null, false, regenerate: true);
Require(string.Join("|", regeneratedTheme.State.Proposal.Select(x => x.DisplayName)) != string.Join("|", stableTheme.State.Proposal.Select(x => x.DisplayName)),
    "Rigenera proposta deve poter scegliere una combinazione diversa dal pool, non una tripletta fissa hardcoded.");

var customWord = "Zebra editoriale " + Guid.NewGuid().ToString("N")[..6];
var addedThemeSubject = DiezVisualThemeFrontendBridge.AddCustomSubject(
    regeneratedTheme.ProjectJson, "halloween", null, false, customWord, "Una zebra singola e riconoscibile.", archiveInThemeLibrary: false);
Require(addedThemeSubject.Status == "SUBJECT_ADDED" && addedThemeSubject.State.Pool.Any(x => x.DisplayName == customWord),
    "Ogni tema deve accettare un soggetto/parola Custom solo-progetto.");

var acceptedTheme = DiezVisualThemeFrontendBridge.AcceptProposal(addedThemeSubject.ProjectJson);
Require(acceptedTheme.Status == "ACCEPTED" && acceptedTheme.State.Resolved,
    "L'accettazione della proposta locale deve congelare i soggetti canonici.");
Require(!DiezVisualSubjectPlannerFrontendBridge.Read(acceptedTheme.ProjectJson).Required,
    "Dopo il freeze locale il vecchio gate planner deve risultare risolto, senza copy/paste.");
var themePack = DiezVisualBookFrontendBridge.BuildPromptPack(acceptedTheme.ProjectJson);
Require(themePack.Items.Count == 3, "Il piano temi accettato deve sbloccare tre Work Unit immagini.");

var customThemeProject = Save(NewProject(), "3 soggetti di Robot vintage");
var emptyCustom = DiezVisualThemeFrontendBridge.Propose(customThemeProject, "custom", "Robot vintage", false, regenerate: false);
Require(emptyCustom.Status == "POOL_TOO_SMALL",
    "Un tema Custom senza pool non deve inventare soggetti né riproporre un planner copy/paste.");
var customWorking = emptyCustom.ProjectJson;
foreach (var name in new[] { "Robot latta", "Robot antenna", "Robot a ruote" })
{
    var added = DiezVisualThemeFrontendBridge.AddCustomSubject(
        customWorking, "custom", "Robot vintage", false, name, name + " come singolo soggetto.", archiveInThemeLibrary: false);
    Require(added.Status == "SUBJECT_ADDED", "Il tema Custom deve accettare soggetti manuali: " + added.Message);
    customWorking = added.ProjectJson;
}
var customThemeProposal = DiezVisualThemeFrontendBridge.Propose(customWorking, "custom", "Robot vintage", false, regenerate: false);
Require(customThemeProposal.Status == "PROPOSED" && customThemeProposal.State.Proposal.Count == 3,
    "Il tema Custom alimentato manualmente deve funzionare senza API.");
Require(!customThemeProposal.State.ApiExpansionAvailable,
    "Finché il catalogo non dichiara una vera API text diretta, l'espansione AI Custom deve restare non operativa.");
Require(DiezAiExchangeBridge.ReadJobs(customThemeProposal.ProjectJson).Count == 0,
    "Tema Custom manuale non deve creare planner TEXT nascosti o copy/paste.");

'''
text = text.replace(needle, round56 + needle, 1)
p.write_text(text, encoding='utf-8')

print('ROUND56_THEME_UI_PATCH_OK')
