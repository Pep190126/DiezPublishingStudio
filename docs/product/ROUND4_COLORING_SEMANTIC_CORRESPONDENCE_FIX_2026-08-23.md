# Round 4 — Coloring semantic correspondence fix — 2026-08-23

Status: TECHNICALLY IN VERIFICATION / NOT CONSOLIDATED until physical installed-app test.

## Physical finding that triggered this change
A real Coloring Prompt Pack requested three Halloween images, one per subject, with Cute & Playful style, Bold & Easy, Cozy, pure black/white output and professional coloring-page craft. The returned candidates were rudimentary/diagrammatic and the imported provider descriptions sounded more compliant than the actual pixels.

Two separate defects were identified:

1. A series-level phrase such as `3 soggetti di Halloween` was being copied verbatim into every atomic Work Unit as `PRIMARY SUBJECT`, so the renderer received a series count/theme where it needed one concrete focal subject.
2. Vision already exposed HARD semantic checks, but the provider description could look authoritative in the UI even though it is not evidence that the actual pixels satisfy the brief. The editorial question “would this be acceptable in a paid coloring book?” was not an explicit named HARD gate.

## Prompt / Work Unit correction
When the generic subject field is recognized as a counted series theme (examples: `3 soggetti di Halloween`, `three Halloween subjects`):

- the phrase is treated as SERIES metadata, not as the subject of one canvas;
- every Work Unit receives an atomic instruction to use exactly ONE concrete, specific, immediately recognizable subject;
- sibling Work Units must use distinct concrete subjects unless repetition was explicitly requested;
- the numeric series count must never be rendered as multiple alternatives, a contact sheet, a collage, or several sibling illustrations in one canvas;
- batch-count statements such as `3 images: 1 per ogni soggetto` are kept out of the renderer-facing single-image HARD requirement;
- a user exclusion meaning “do not put multiple requested illustrations in one image” is normalized to the unambiguous atomic constraint `Do not combine multiple requested series illustrations into one canvas.`

Explicit structured Scene/Subject assignments and explicit per-image overrides remain stronger than this fallback rule.

## Batch execution correction
`PROMPT.md` now instructs the executing AI to create a batch-wide subject assignment plan first when the publisher supplied only a counted theme rather than concrete names. It also requires regeneration rather than acceptance when an output looks like a scribble, rough draft, geometric primitive construction, placeholder, icon sheet, diagram, or otherwise non-publishable page.

## Vision correction
A new HARD gate is added:

`editorial_readiness` — **Qualità editoriale / pubblicabile**

Expected behavior for Coloring:

- the candidate must look like a finished publishing asset suitable for a paid coloring book;
- scribbles, rough sketches, primitive clip-art, placeholders, exercise sheets, or merely interpretable drafts are FAIL;
- the actual pixels are authoritative;
- the provider-generated description is descriptive metadata only and must not be used as proof of compliance.

The Vision UI labels the provider/Candidate description accordingly and explicitly tells the reviewer to check the real preview against every HARD requirement.

## Regression gate
The visual pianist must verify at minimum:

- a counted generic series subject creates `SERIES SUBJECT ASSIGNMENT — HARD`;
- `PRIMARY SUBJECT — HARD LOCK: 3 soggetti di Halloween` no longer appears in the atomic prompt;
- the Work Unit position (for example `item 1 of 3`) is explicit;
- batch image-count prose does not leak into the single-image `USER REQUIREMENT — HARD`;
- the no-multi-illustration request becomes an unambiguous atomic exclusion;
- `editorial_readiness` is HARD.

## Physical acceptance test
Use an installed Round 4 candidate and regenerate a small Coloring batch comparable to the Halloween test.

Acceptance requires all of the following:

1. Inspect the generated `PROMPT.md`: the three Work Units must no longer each claim `3 soggetti di Halloween` as their primary subject.
2. Generate the batch with the same or equivalent publisher settings.
3. Each returned image must have one clear focal subject and a coherent, professional coloring-page composition.
4. Primitive/diagrammatic/scribble-like results are not acceptable merely because the subject can be guessed.
5. In Vision, `Qualità editoriale / pubblicabile` is visible and remains unchecked unless the real pixels are genuinely publication-ready.
6. The provider description must be visibly presented as non-authoritative metadata.

Only after a successful physical test may these Round 4 behaviors be marked CONSOLIDATED.
