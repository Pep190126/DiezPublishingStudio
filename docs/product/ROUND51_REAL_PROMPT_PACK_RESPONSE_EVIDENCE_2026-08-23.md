# Round 5.1 — real Prompt Pack / Response evidence (2026-08-23)

## Reproduction inputs

User supplied a real manual Prompt Pack and its Response for project `NuovoProgetto16`.

Observed Prompt Pack identifiers:
- project_id: `7bef5ba7-b2ba-41d0-87fd-e2586f7808fa`
- job_id: `2d09c56a-e5f3-4895-a7e7-87a09038f1aa`
- prompt_pack_id: `0c72b0a1-efa6-482d-ad97-df123dae1f77`
- request_snapshot_id: `68af7c30-aeb6-456b-81c7-eb6cd5b05e7c`
- work units: 3 (`IMG-001`, `IMG-002`, `IMG-003`)

## Finding 1 — batch orchestration is legitimate only at package level

`PROMPT.md` contains the batch sentence:

`Il lotto contiene ESATTAMENTE 3 immagini da generare come asset separati...`

This is correct package/executor orchestration. It must never be passed as renderer-facing content for an atomic Work Unit.

The real `prompt-manifest.json` does **not** contain the forbidden literal `3 images` / `3 immagini` as a renderer directive. Each Work Unit instead contains atomic instructions plus explicit series-assignment context such as `item 1 of 3`.

Therefore the Round 5.1 boundary remains:

**batch/package orchestration -> executor only**

**atomic visual brief -> renderer only**

A legacy or existing-project regeneration must normalize/recompile before the renderer safety gate. The safety gate must not reject legitimate package-level quantity metadata, but it must continue to reject true multi-image layout requests (`triptych`, `collage`, `grid`, contact sheet, multiple sibling illustrations on one canvas).

## Finding 2 — Response identity mapping is correct

The supplied Response uses the same `project_id`, `job_id`, `prompt_pack_id`, `request_snapshot_id` and the same three `work_unit_id` values as the Prompt Pack. The transport identity contract is therefore coherent for this case.

## Finding 3 — all three generation results are INCOMPLETE

The Response contains three results and all are:
- `content_type`: `IMAGE`
- `candidate_version`: `1`
- `status`: `INCOMPLETE`
- no primary asset returned

Each description reports that native image generation was invoked but the output failed final compliance checks.

This is a separate production-quality/provider-compliance outcome from the earlier `renderer visual brief contaminato ... 3 images` regeneration bug. Do not conflate the two defects.

## Regression requirement

Round 5.1 must preserve all of the following simultaneously:
1. existing/legacy projects can regenerate visual Prompt Packs without failing solely because old batch-count prose is present in persisted prompt state;
2. Prompt preview, Ready IMAGE jobs and final Prompt Pack use the same canonical visual compiler;
3. `PROMPT.md` may describe the full batch quantity;
4. individual renderer-facing briefs must remain atomic;
5. true multi-image layout contamination remains a hard failure;
6. Prompt Pack/Response IDs and Work Unit IDs remain unchanged through transport;
7. `INCOMPLETE` provider results remain non-Candidate assets unless a valid primary asset is actually returned.
