from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def rep(path, old, new, count=1):
    p = ROOT / path
    text = p.read_text(encoding='utf-8')
    actual = text.count(old)
    if actual < count:
        raise SystemExit(f'{path}: expected {count}, found {actual}: {old[:100]!r}')
    p.write_text(text.replace(old, new, count), encoding='utf-8')

workspace = 'src/Diez.Uno/VisualBookWorkspace.cs'
rep(workspace,
    '        Func<DiezColoringProfileDto>? coloringProfile = null;\n        Func<DiezImageProfileDto>? imageProfile = null;\n',
    '        Func<DiezColoringProfileDto>? coloringProfile = null;\n        Func<DiezImageProfileDto>? imageProfile = null;\n        Func<bool>? customStyleChoice = null;\n        TextBox? customDefinitionBox = null;\n        RadioButton? customArchiveChoice = null;\n')
rep(workspace,
    '            style.SelectionChanged += (_, _) => RefreshCustomStyle();\n            RefreshCustomStyle();\n            var audience = Combo(',
    '            style.SelectionChanged += (_, _) => RefreshCustomStyle();\n            RefreshCustomStyle();\n            customStyleChoice = IsCustomStyleChoice;\n            customDefinitionBox = customDefinition;\n            customArchiveChoice = customArchive;\n            var audience = Combo(')
rep(workspace,
    '                IsCustomStyleChoice() ? "Custom" : Selected(style, "Clean Line Art"),',
    '                customStyleChoice?.Invoke() == true ? "Custom" : Selected(style, "Clean Line Art"),')
rep(workspace,
    '                var customChoice = IsCustomStyleChoice();\n                var customSaved = document.SaveColoringCustomStyle(\n                    customChoice,\n                    customChoice ? customDefinition.Text : string.Empty,\n                    customChoice && customArchive.IsChecked == true);\n                if (customSaved.Status == "INVALID")\n                {\n                    report(customSaved.Message);\n                    customDefinition.Focus(FocusState.Programmatic);\n                    return false;\n                }',
    '                var customChoice = customStyleChoice?.Invoke() == true;\n                var customSaved = document.SaveColoringCustomStyle(\n                    customChoice,\n                    customChoice ? customDefinitionBox?.Text : string.Empty,\n                    customChoice && customArchiveChoice?.IsChecked == true);\n                if (customSaved.Status == "INVALID")\n                {\n                    report(customSaved.Message);\n                    customDefinitionBox?.Focus(FocusState.Programmatic);\n                    return false;\n                }')

subjects = 'src/Diez.Core/MultiSubjectProfileService.cs'
rep(subjects,
    '        sb.AppendLine($"Subject identity [{subject.Name}] — LOCKED: preserve the same recognizable identity, core physical traits and silhouette across appearances.");',
    '        var semanticName = string.IsNullOrWhiteSpace(subject.CanonicalConcept) ? subject.Name : subject.CanonicalConcept;\n        sb.AppendLine($"Subject identity [{semanticName}] — LOCKED: preserve the same recognizable identity, core physical traits and silhouette across appearances.");')

hard = 'src/Diez.Core/DiezVisualHardPromptFrontendBridge.cs'
rep(hard,
    '            sb.AppendLine($"PARTICIPANT CONSISTENT — AUTHORITATIVE [{participant.Name}]:");',
    '            sb.AppendLine($"PARTICIPANT CONSISTENT — AUTHORITATIVE [{ProviderSubject(participant)}]:");')

print('ROUND54_COMPILE_SEMANTIC_FIX_APPLIED')
