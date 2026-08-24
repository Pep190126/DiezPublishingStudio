from pathlib import Path
p = Path('src/Diez.Core/DiezVisualSubjectPlannerFrontendBridge.cs')
text = p.read_text(encoding='utf-8')
old = '        sb.AppendLine("{"subjects":[{"display_name_it":"...","canonical_concept":"...","description_it":"...","canonical_description":"..."}]}");\n'
new = '        sb.AppendLine("{\\\"subjects\\\":[{\\\"display_name_it\\\":\\\"...\\\",\\\"canonical_concept\\\":\\\"...\\\",\\\"description_it\\\":\\\"...\\\",\\\"canonical_description\\\":\\\"...\\\"}]}");\n'
if text.count(old) != 1:
    raise SystemExit(f'Expected one malformed reconciliation schema line, found {text.count(old)}')
p.write_text(text.replace(old, new, 1), encoding='utf-8')
print('Round 5.5 compile fix applied.')
