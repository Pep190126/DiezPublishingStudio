# Round 5.1 — Legacy Prompt Pack migration / renderer brief compatibility

Date: 2026-08-23
Status: IMPLEMENTATION CANDIDATE — physical installed-app validation required

## Observed physical regression

Opening an existing visual project and regenerating the Prompt Pack can fail with:

`Prompt non disponibile: renderer visual brief contaminato da istruzione orchestrativa/layout: 3 images`

This is a compatibility defect, not a valid reason to make the publisher rebuild the project.

## Root cause

Two visual compilation paths had diverged:

1. the Phase 2 / visual plan preview and ready-job synchronization still used an older atomic visual-source builder;
2. the final Prompt Pack recompile used `VisualHardPromptContractCompiler`.

A legacy `MustDo` or persisted instruction such as `3 images: 1 per ogni soggetto` could therefore survive in the older path. `PromptPackRendererVisualBriefService` then correctly detected the batch/layout phrase at the final image-renderer boundary, but it rejected the whole prompt instead of migrating the obsolete orchestration away.

Round 5 also introduced an execution-method instruction containing `FAILED/INCOMPLETE`; the renderer-only boundary must normalize that operational fallback into a positive native-image rendering requirement rather than reject its own new contract.

## Product / architecture decision

There is ONE authoritative visual atomic compiler: `VisualHardPromptContractCompiler`.

Phase 2 preview, ready image jobs and final Prompt Pack must derive from that same compiler. A second independent atomic prompt builder is not allowed to evolve separately.

## Existing-project migration behavior

When an old `.diez` project is opened and a visual Prompt Pack is regenerated:

- current canonical project choices are authoritative;
- current visual Work Units are recompiled from canonical subject, Scene, Consistent, profile and HARD user decisions before the package is frozen;
- obsolete batch/orchestration fragments are removed from the single-image renderer brief;
- examples removed/migrated include `3 images`, `3 images: 1 per ogni soggetto`, `one image per subject` and equivalent Italian/English legacy forms when they are clearly quantity/routing instructions;
- meaningful user requirements on the same field must be preserved where separable;
- stable WorkUnitId / job identities are not renumbered or recycled;
- no subject, Scene, style, Consistent rule or valid HARD editorial requirement is silently deleted.

## Renderer-boundary behavior

`PromptPackRendererVisualBriefService` remains a strict final gate after migration.

It must:

- accept and normalize legacy bullet prefixes;
- strip only recognizable batch/per-item orchestration from the atomic renderer brief;
- convert the Round 5 execution method into a positive requirement to use the provider's native generative image capability, without leaking Response status routing into the image-model brief;
- continue to reject real layout contamination such as triptych, collage, contact sheet, grid or multi-panel instructions for one atomic Work Unit;
- avoid treating an arbitrary visual phrase containing a number plus the word `images` as orchestration unless the surrounding syntax actually expresses batch quantity/routing.

## Defense in depth at Prompt Pack freeze

`DiezPromptPackBatchFrontendBridge.BuildManualPackageAsync` must refresh visual Work Unit instructions from `DiezVisualHardPromptFrontendBridge.Recompile` before freezing the package when possible. This makes the migration work for Core callers as well as the Uno adapter.

## Regression gates

Automated regression must prove at least:

1. `USER REQUIREMENT — HARD: 3 images: 1 per ogni soggetto` no longer blocks an atomic visual brief and does not survive in it;
2. a meaningful sibling requirement is preserved when the legacy batch clause is removed;
3. the Round 5 native-image execution directive no longer conflicts with the renderer-only gate;
4. a genuine triptych/contact-sheet style layout instruction is still rejected;
5. `DiezVisualBookFrontendBridge.BuildPromptPack` can build three visual items when an old-style `3 images: 1 per ogni soggetto` MustDo is supplied.

## Physical acceptance test

Using the installed Round 5.1 candidate:

1. open the same existing project that produced the physical error;
2. do not recreate it and do not manually delete the old `3 images` instruction;
3. regenerate Phase 2 Prompt / ready image jobs / Prompt Pack;
4. expected: no `renderer visual brief contaminato ... 3 images` error;
5. inspect the generated atomic prompts: the batch count is absent from each renderer brief, while subject/style/Scene/HARD requirements remain;
6. generate the new Coloring batch and continue the Round 5 quality-first test.

Only the user's physical installed-app confirmation can move this fix to CONSOLIDATED.