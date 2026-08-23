from pathlib import Path


def replace_one(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"Expected source block not found in {path}: {old[:160]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")

# Round 5: quality-first visual execution.
# A Prompt Pack must use a native image generator, never a coded/vector fallback.
path = "src/Diez.Core/DiezVisualHardPromptFrontendBridge.cs"
replace_one(
    path,
    '        sb.AppendLine("COLORING OUTPUT — HARD: the final raster must contain exactly pure black #000000 and pure white #FFFFFF. No gray pixels, grayscale, antialiasing gray, color, gradients, shadows, glow, halftones, tonal texture or intermediate values. Threshold/binarize the final asset if necessary.");',
    '        sb.AppendLine("RENDERING METHOD — HARD: use a native generative IMAGE model/tool capable of producing finished illustration artwork. Do NOT construct the artwork with Python, Pillow, SVG, Canvas, plotting libraries, programmatic vector primitives, geometric shape assembly, diagram code or any other coded drawing fallback. If native image generation is unavailable, return FAILED/INCOMPLETE instead of fabricating an approximate drawing.");\n        sb.AppendLine("QUALITY FIRST — HARD: create the page as a professional organic illustration first. Low complexity, Bold & Easy and closed regions mean simplified ILLUSTRATION, never circles/rectangles/lines assembled as symbols, icons or a diagram.");\n        sb.AppendLine("COLORING APPEARANCE — HARD: visually use black ink on a clean white background with no intentional gray shading, color, gradients, shadows, glow, halftones or tonal texture. Do not sacrifice drawing quality or replace organic illustration with primitives merely to force exact binary pixels; pixel-level black/white normalization belongs to Diez post-generation processing.");')

path = "src/Diez.Core/DiezPromptPackBatchFrontendBridge.cs"
replace_one(
    path,
    '        sb.AppendLine("5. Prima di accettare un risultato, scartalo e rigeneralo se appare come bozza, scarabocchio, primitive geometriche, placeholder, icon sheet, diagramma o pagina non commercialmente pubblicabile, anche se il tema generale è intuibile.");',
    '        sb.AppendLine("5. ESECUZIONE OBBLIGATORIA: per ogni immagine usa un vero modello/tool nativo di GENERAZIONE IMMAGINI. È vietato sostituirlo con Python, Pillow, SVG, Canvas, plotting, primitive vettoriali/geometriche o disegno programmatico. Se un generatore immagini nativo non è disponibile, restituisci FAILED/INCOMPLETE: non creare un surrogato.");\n        sb.AppendLine("6. Prima di accettare un risultato, scartalo e rigeneralo se appare come bozza, scarabocchio, primitive geometriche, placeholder, icon sheet, diagramma o pagina non commercialmente pubblicabile, anche se il tema generale è intuibile.");')
# Renumber subsequent user-facing rules only where the old numbers are exact.
for old, new in [
    ('        sb.AppendLine("6. `prompt-manifest.json` contiene gli identificatori tecnici necessari a Diez per ricomporre i risultati. Usali soltanto nel Response Pack e non inserirli nelle immagini né nei prompt del renderer.");',
     '        sb.AppendLine("7. `prompt-manifest.json` contiene gli identificatori tecnici necessari a Diez per ricomporre i risultati. Usali soltanto nel Response Pack e non inserirli nelle immagini né nei prompt del renderer.");'),
    ('        sb.AppendLine("7. Eventuali reference/materiali sono sotto `inputs/` e vanno usati solo per i ruoli dichiarati nel manifest. Non inferire un uso diverso dal ruolo editoriale indicato dal publisher.");',
     '        sb.AppendLine("8. Eventuali reference/materiali sono sotto `inputs/` e vanno usati solo per i ruoli dichiarati nel manifest. Non inferire un uso diverso dal ruolo editoriale indicato dal publisher.");'),
    ('        sb.AppendLine("8. Ogni risultato rientra come Candidate. Non approvare implicitamente: Vision/review e `Porta nel libro` restano fasi Diez separate.");',
     '        sb.AppendLine("9. Ogni risultato rientra come Candidate. Non approvare implicitamente: Vision/review e `Porta nel libro` restano fasi Diez separate.");'),
    ('            ? "9. Al termine restituisci, quando il sistema lo consente, UN SOLO Response ZIP `diez-response` contenente un risultato distinto per ogni Work Unit del manifest."',
     '            ? "10. Al termine restituisci, quando il sistema lo consente, UN SOLO Response ZIP `diez-response` contenente un risultato distinto per ogni Work Unit del manifest."'),
    ('            : $"9. Al termine restituisci, quando il sistema lo consente, UN SOLO Response ZIP chiamato ESATTAMENTE `{expectedResponse}`, contenente un risultato distinto per ogni Work Unit del manifest.");',
     '            : $"10. Al termine restituisci, quando il sistema lo consente, UN SOLO Response ZIP chiamato ESATTAMENTE `{expectedResponse}`, contenente un risultato distinto per ogni Work Unit del manifest.");')
]:
    replace_one(path, old, new)

# Vision wording: make primitive/programmatic-looking output explicitly non-publishable.
path = "src/Diez.Core/DiezVisionFrontendBridge.cs"
replace_one(
    path,
    '                "La pagina deve sembrare un\'illustrazione finita che un editore potrebbe inserire in un libro da colorare a pagamento: niente scarabocchi, bozza, primitive, clip-art grezza, placeholder, esercizio tecnico o resa solo vagamente interpretabile.", true));',
    '                "La pagina deve sembrare un\'illustrazione finita che un editore potrebbe inserire in un libro da colorare a pagamento: niente scarabocchi, bozza, primitive, clip-art grezza, placeholder, esercizio tecnico, resa da SVG/diagramma/disegno programmatico o contenuto solo vagamente interpretabile. Semplice non significa geometrico o rudimentale.", true));')

# Provider manual instructions also state the no-coded-fallback rule.
path = "src/Diez.Core/DiezPromptPackBatchFrontendBridge.cs"
# Marker for regression/build inspection.
p = Path(path)
text = p.read_text(encoding="utf-8")
if "ESECUZIONE OBBLIGATORIA" not in text or "Python, Pillow, SVG" not in text:
    raise SystemExit("Round 5 batch execution guard missing after patch")

p = Path("src/Diez.Core/DiezVisualHardPromptFrontendBridge.cs")
text = p.read_text(encoding="utf-8")
if "RENDERING METHOD — HARD" not in text or "Threshold/binarize" in text:
    raise SystemExit("Round 5 renderer guard not applied correctly")

print("Round 5 native-image quality patch applied successfully.")
