# Round 5.2 — existing vs fresh project semantic divergence (2026-08-24)

Status: **PHYSICAL TEST EVIDENCE / BUG — NON CONSOLIDATO**

## User reproduction

The user compared two real Coloring projects generated with the current manual Prompt Pack flow:

- `NuovoProgetto10`: an existing project reopened and regenerated.
- `NuovoProgetto17`: a new project created from scratch.

Files inspected:

- `NuovoProgetto10_20260824_v001_prompt-pack(3).zip` — SHA-256 `1358c2c7146e18e8a5b7c444679c1eb3f4f73258ae9553eac2506cb51095396e`
- `NuovoProgetto10_20260824_v001_response(2).zip` — SHA-256 `a9707b43b13a7fb92c7c5b43a734c5d0fa61e6b11000a73f11badef1bf1eba25`
- `NuovoProgetto17_20260824_v001_prompt-pack(3).zip` — SHA-256 `9aaef5db8a847b080a8064657545aba2aa98376dc647c5abbd31479a3ad7c883`
- `NuovoProgetto17_20260824_v001_response(2).zip` — SHA-256 `77be37f1a7b106cfa2a33f2d8862ea2eb37acedff26b87f797bfc4cb1a9c9234`

## Finding A — existing project keeps legacy semantic contamination

`NuovoProgetto10` contains the same aggregate phrase in every atomic IMAGE Work Unit:

`PRIMARY SUBJECT — HARD LOCK: 3 images di soggetti per Halloween.`

The synthesized art direction also treats `3 images di soggetti per Halloween` as the immediate focal subject.

This is invalid. A quantity/series request cannot be the atomic primary subject of IMG-001, IMG-002 and IMG-003.

This proves that reopening/regenerating an existing project is still capable of feeding legacy/persisted prompt-era text into the current compiler instead of rebuilding the semantic decision state from the user's visible choices.

## Finding B — fresh project improves aggregate-series recognition but is still not canonical enough

`NuovoProgetto17` correctly recognizes the user phrase `3 soggetti di Halloween` as a SERIES-level request and emits:

`SERIES SUBJECT ASSIGNMENT — HARD: ... Assign exactly ONE concrete, specific, immediately recognizable subject to this Work Unit before rendering.`

The atomic PRIMARY SUBJECT is improved to:

`one concrete, specific, immediately recognizable subject fitting the theme: Halloween`

However, Diez still delegates the actual semantic atomization to the external AI at execution time. The Prompt Pack itself does not freeze concrete subjects such as pumpkin / ghost / cat into IMG-001 / IMG-002 / IMG-003.

This violates the intended architecture:

**Diez must resolve canonical subjects/scenes before Prompt Pack compilation. The external renderer must receive an already-resolved Work Unit, not be asked to perform batch planning.**

The fact that an external chat later chose pumpkin, ghost and cat does not make those choices canonical Diez state.

## Finding C — raw UI/user Italian still leaks into provider-facing prompts

Both real Prompt Packs contain Italian/free-form UI-era strings embedded directly into otherwise-English renderer prompts.

Examples:

### Project 10

- `Libera ma pur essendo abbastanza dettagliata deve rimanere da colorare`
- `3 images di soggetti per Halloween`

### Project 17

- `3 scenari di Halloween different ognuno per una delle foto richieste`
- `generare un'unica image con 3 soggetti`

This conflicts with `UI_TO_PROMPT_SEMANTIC_COMPILATION_SPEC.md`.

The correct pipeline is:

`UI italiana -> canonical semantic decision -> prompt engineering -> provider/model-specific prompt`

not literal copying or near-literal translation of UI/free text into the provider-facing instruction.

Free text must preserve facts and HARD meaning, but it must be semantically classified and compiled into task-effective prompt engineering.

## Finding D — both providers failed image generation, so quality remains unresolved

The actual Response ZIPs contain no image assets. All six results are `FAILED`.

Project 10 descriptions report unrelated/non-compliant native image generation for all three Work Units.

Project 17 descriptions report:

- IMG-001: unrelated photorealistic landscape;
- IMG-002: unrelated photorealistic landscape;
- IMG-003: unrelated math worksheet image.

Therefore these runs do **not** demonstrate that Coloring quality has improved. They demonstrate that invalid outputs were rejected instead of returned as Candidate, which is better lifecycle behavior, but the generation route remains unusable for the requested visual task.

## Finding E — Response 17 is not protocol-complete

`NuovoProgetto17` Prompt Pack has:

- project_id `4b67673d-e2ec-4636-b4ee-09eb0b4e67c3`
- job_id `1d17c755-a506-4401-9ac8-421d70c50411`
- prompt_pack_id `0b2adfcb-121f-4d1b-916c-b562265271bd`
- request_snapshot_id `594ee1e1-92f6-49c0-b80e-2bb175828c06`

But its `diez-response.json` omits `transport`, `project_id`, `job_id`, `prompt_pack_id` and `request_snapshot_id` entirely.

The Response contains only protocol/version + result items.

Therefore the external statement that this Response Pack was "conforme al protocollo" is factually false for the actual ZIP.

By contrast, the Project 10 Response preserves all four transport identities and `transport: MANUAL`.

Diez must keep strict Response identity validation. A provider/chat must not be allowed to call an identity-incomplete Response "conforme".

## Required fixes

1. **Existing-project semantic migration:** on load/regeneration, rebuild prompt inputs from canonical user decisions; do not trust persisted prompt-era strings as semantic source-of-truth.
2. **Fresh/existing parity:** equivalent visible user choices must yield semantically equivalent canonical snapshots regardless of project age/history.
3. **Diez-owned atomization:** before Prompt Pack freeze, materialize concrete SubjectId/SceneId assignments per Work Unit. The renderer must not choose the batch plan.
4. **Semantic language boundary:** no UI label/free-text sentence is copied mechanically into provider-facing prompts; classify and compile meaning into prompt engineering.
5. **Response protocol strictness:** require project/job/prompt-pack/request-snapshot identity when the Prompt Pack provides them.
6. **Generation-quality route:** native image generation that returns unrelated landscapes/worksheets must remain FAILED and requires a provider/tool routing fix; do not weaken HARD gates to obtain a Candidate.

## Regression criteria

A future candidate passes only if:

- reopening Project 10 and regenerating no longer produces `PRIMARY SUBJECT: 3 images ...`;
- a fresh equivalent project and a reopened equivalent project compile the same semantic subject/scene plan apart from fresh technical IDs;
- IMG-001/002/003 each freeze a concrete subject before export;
- provider-facing prompts contain prompt-engineered semantics, not raw Italian UI prose;
- a Response missing Prompt Pack identity fields is rejected as protocol-incomplete;
- unrelated visual output is never imported as a valid Candidate.
