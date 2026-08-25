from pathlib import Path


def replace_once(path: str, old: str, new: str):
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"expected block not found in {path}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


core = "src/Diez.Core/DiezVisualThemeFrontendBridge.cs"
replace_once(
    core,
    '''public sealed record DiezVisualThemeMutation(\n    string ProjectJson,\n    string Status,\n    string Message,\n    DiezVisualThemeStateDto State);\n\n/// <summary>''',
    '''public sealed record DiezVisualThemeMutation(\n    string ProjectJson,\n    string Status,\n    string Message,\n    DiezVisualThemeStateDto State);\n\n/// <summary>\n/// Shared semantic policy for whether a persisted theme proposal is ready for explicit user acceptance.\n/// The Uno UI must derive button state from this policy plus actual unsaved editor differences, never from\n/// TextChanged events alone.\n/// </summary>\npublic static class DiezVisualThemeAcceptancePolicy\n{\n    public static bool IsAcceptableProposal(\n        int requestedCount,\n        IReadOnlyList<DiezVisualThemeSubjectDto>? proposal)\n    {\n        var count = Math.Clamp(requestedCount, 1, MultiSubjectProfileService.MaxSubjects);\n        if (proposal is null || proposal.Count != count) return false;\n        if (proposal.Any(x => string.IsNullOrWhiteSpace(x.DisplayName))) return false;\n        if (proposal.Select(x => x.DisplayName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != count) return false;\n        return proposal.All(x =>\n            VisualSemanticResolutionGuard.IsConcrete(x.DisplayName) ||\n            VisualSemanticResolutionGuard.IsConcrete(x.CanonicalConcept));\n    }\n}\n\n/// <summary>''')
replace_once(
    core,
    '''        var proposalReady = state.Proposal.Count == count &&\n                            state.Proposal.Select(x => x.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == count;''',
    '''        var proposalDto = state.Proposal.Select(ToDto).ToList();\n        var proposalReady = DiezVisualThemeAcceptancePolicy.IsAcceptableProposal(count, proposalDto);''')
replace_once(
    core,
    '''            pool.Select(ToDto).ToList(),\n            state.Proposal.Select(ToDto).ToList(),\n            proposalReady,''',
    '''            pool.Select(ToDto).ToList(),\n            proposalDto,\n            proposalReady,''')

ui = "src/Diez.Uno/VisualThemeWorkspace.cs"
replace_once(
    ui,
    '''            accept.IsEnabled = state.ProposalReady;\n            foreach (var editor in proposalEditors.SelectMany(x => new[] { x.Name, x.Description }))\n            {\n                editor.TextChanged += (_, _) =>\n                {\n                    if (accept is not null) accept.IsEnabled = false;\n                    editInfo.Text = "Hai modifiche non salvate. Salva la proposta prima di accettarla.";\n                };\n            }''',
    '''            bool HasActualUnsavedProposalEdits()\n            {\n                if (proposalEditors.Count != state.Proposal.Count) return true;\n                for (var i = 0; i < proposalEditors.Count; i++)\n                {\n                    var currentName = (proposalEditors[i].Name.Text ?? string.Empty).Trim();\n                    var currentDescription = (proposalEditors[i].Description.Text ?? string.Empty).Trim();\n                    var savedName = (state.Proposal[i].DisplayName ?? string.Empty).Trim();\n                    var savedDescription = (state.Proposal[i].Description ?? string.Empty).Trim();\n                    if (!string.Equals(currentName, savedName, StringComparison.Ordinal) ||\n                        !string.Equals(currentDescription, savedDescription, StringComparison.Ordinal))\n                        return true;\n                }\n                return false;\n            }\n\n            void RefreshAcceptState()\n            {\n                var persistedReady = DiezVisualThemeAcceptancePolicy.IsAcceptableProposal(\n                    state.RequestedCount, state.Proposal);\n                var dirty = HasActualUnsavedProposalEdits();\n                if (accept is not null) accept.IsEnabled = persistedReady && !dirty;\n                editInfo.Text = dirty\n                    ? "Hai modifiche non salvate. Salva la proposta prima di accettarla."\n                    : persistedReady\n                        ? "Proposta completa e salvata: puoi accettare e congelare i soggetti."\n                        : "La proposta non è ancora completa o semanticamente accettabile.";\n            }\n\n            foreach (var editor in proposalEditors.SelectMany(x => new[] { x.Name, x.Description }))\n                editor.TextChanged += (_, _) => RefreshAcceptState();\n            RefreshAcceptState();''')

tests = "tests/Diez.VisualSemanticRegression/Program.cs"
replace_once(
    tests,
    '''Require(localTheme.State.Proposal.Count == 3 && localTheme.State.Proposal.Select(x => x.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3,\n    "Diez deve proporre esattamente tre soggetti Halloween distinti.");''',
    '''Require(localTheme.State.Proposal.Count == 3 && localTheme.State.Proposal.Select(x => x.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3,\n    "Diez deve proporre esattamente tre soggetti Halloween distinti.");\nRequire(localTheme.State.ProposalReady,\n    "Round 5.6.2: una proposta BUILTIN completa deve risultare pronta per l'accettazione nella state DTO.");\nRequire(DiezVisualThemeAcceptancePolicy.IsAcceptableProposal(localTheme.State.RequestedCount, localTheme.State.Proposal),\n    "Round 5.6.2: la policy condivisa deve abilitare l'accettazione della proposta Halloween persistita.");''')

print("ROUND562_THEME_ACCEPT_ENABLE_FIX_APPLIED")
