# Round 5 — Native image generation / quality-first Coloring fix

Date: 2026-08-23
Status: TECHNICALLY_VERIFIED in Windows CI; physical installed-app validation required.

## Empirical trigger

The Round 4 test batch `NuovoProgetto15` correctly differentiated three Halloween subjects (ghost, witch, cat), but all three returned PNGs were still visually primitive: geometric/vector-like construction, icon/diagram quality, and not commercially publishable coloring-book illustrations.

This demonstrates that subject differentiation alone was not the remaining root cause.

## Root cause

The manual Prompt Pack did not explicitly forbid a provider/assistant from satisfying an IMAGE Work Unit by constructing a raster through code or vector primitives instead of invoking a native generative image model/tool.

The provider-facing Coloring prompt also required exact #000000/#FFFFFF pixels and suggested threshold/binarization during generation. That technical constraint could encourage premature geometric/post-processing behavior instead of quality-first illustration.

## Round 5 contract

### Native image generation is mandatory

For IMAGE Work Units:
- use a native generative IMAGE model/tool capable of finished illustration artwork;
- do not use Python, Pillow, SVG, Canvas, plotting libraries, programmatic vector primitives, geometric-shape assembly or diagram code as a generation fallback;
- if native image generation is unavailable, return FAILED/INCOMPLETE instead of fabricating an approximate drawing.

### Quality first

`Low complexity`, `Bold & Easy`, closed regions and child-friendly simplification mean simplified professional ILLUSTRATION, never circles/rectangles/straight lines assembled into icons or diagrams.

The renderer must create an organic, authored, commercially publishable coloring illustration before technical raster normalization.

### Black/white separation of concerns

Provider-facing requirement:
- visually black line art on clean white background;
- no intentional gray shading, color, gradients, shadows, glow, halftones or tonal texture.

Removed from renderer-facing generation:
- forced exact binary pixels as the mechanism that shapes the illustration;
- `Threshold/binarize the final asset if necessary`.

Pixel-level black/white normalization belongs to Diez post-generation processing and must not degrade or replace the artistic generation stage.

### Vision

`Qualità editoriale / pubblicabile` remains HARD and now explicitly includes rejection of:
- programmatic/SVG/diagram-like rendering;
- primitive clip-art;
- scribbles/rough drafts/placeholders;
- content that is merely semantically interpretable but not suitable for a paid publication.

## Round 5 Windows candidate

- Source SHA: `5eeadc2eb8aac6bffe00dce567c2973382516b73`
- Workflow: `Uno Windows Consolidation Candidate`
- Run number: `23`
- Run ID: `32658618322`
- Artifact ID: `9498161126`
- Setup bytes: `94956125`
- Setup SHA-256: `cbe34892c3ecd60108818f9dcf785b76c990230735c24f245b67b8ef0e51b773`
- Artifact ZIP SHA-256: `9372b6e1de047a2b04d6fc355839521326fe113d947e1053a86ae68a81c62885`
- Gate / restore / publish / EXE verify / package / smoke install-launch-uninstall / upload: SUCCESS.

This candidate is TECHNICALLY_VERIFIED, not CONSOLIDATED until physical testing confirms the generated Coloring output improves materially.
