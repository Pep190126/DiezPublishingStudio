from pathlib import Path

path = Path('src/Diez.Core/DiezVisualHardPromptFrontendBridge.cs')
text = path.read_text(encoding='utf-8')
duplicate = 'using System.Text.RegularExpressions;\nusing System.Text.RegularExpressions;\n'
if duplicate in text:
    text = text.replace(duplicate, 'using System.Text.RegularExpressions;\n', 1)
if 'SERIES SUBJECT ASSIGNMENT — HARD' not in text:
    raise SystemExit('Round 4 semantic marker missing; refusing cleanup on unexpected source.')
path.write_text(text, encoding='utf-8')
print('Round 4 source cleanup complete.')
