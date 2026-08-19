# Physical validation — Uno round 2 — 2026-08-19

Status: PHYSICAL TEST FINDINGS. These findings are authoritative evidence from the installed Windows candidate, but the requested fixes are not CONSOLIDATED until a new installed build is physically validated and explicitly confirmed.

## Candidate under test

- Uno Windows candidate run: 6
- Run ID: 32203259001
- Source SHA: e9259d8e240e335ec53788c2119680353e9a5292
- CI status before physical test: TECHNICALLY_VERIFIED

## Findings reported by the publisher

### Materials intake

1. After loading project materials, the expected preview is not visible/reliably retained.
2. Add drag-and-drop as a first-class intake path in addition to the file picker.
3. A material must not be treated as a passive attachment. The user must be able to state what the material is for.

### AI Exchange file naming

Prompt Pack and expected Response naming must use a deterministic publishing convention based on:

`project-name + date + version + role`

Example:

- `MioProgetto_20260819_v001_prompt-pack.zip`
- `MioProgetto_20260819_v001_response.zip`
- regeneration on the same project/date: `v002`, `v003`, ...

The Prompt Pack must carry the expected Response filename/convention so the external AI has an explicit handoff target. Import must remain robust if a provider changes the filename, but Diez must show the mismatch and preserve the canonical internal name/version.

### Undo / redo and project history

Two different levels are required and must not be confused:

1. Control-level edit history
   - Undo/redo inside editable controls and future editable grid cells.
   - Keyboard: Ctrl+Z undo, Ctrl+Y redo.
   - This is local editing and must not create a project-wide rollback for every keystroke.

2. Project-level timeline
   - Named/automatic project snapshots around meaningful publishing actions.
   - Consultable chronological history.
   - Ability to move backward to an earlier project state and forward again, like a project-level undo/redo timeline.
   - Restoring a historical state must itself be safe and reversible; history must not be destroyed by moving backward.

### Window layout

1. Add a small sidebar collapse/expand control.
2. Main workspace and preview must be resizable by the user inside the application window.
3. Resizing must not lose selection, edit state or current project state.

### Material intent / publisher semantics

When a publisher adds their own material, Diez must ask or expose what the material should do in the project.

For images, required use families include at least:

- direct book asset / place as-is;
- identity reference / model for a recurring subject;
- style reference;
- composition/layout reference;
- environment/background reference;
- replicate closely;
- transform/reinterpret;
- modify only specified details, with a free-text field describing the allowed/required changes;
- inspiration only, do not copy closely;
- archive/source only, never send to AI.

For text/documents, useful roles include:

- authoritative factual source;
- source to summarize/transform;
- writing/tone reference;
- structure/index reference;
- terminology/glossary authority;
- original text to preserve and edit into the Master;
- archive only / do not send to AI.

For tables/data, useful roles include:

- canonical dataset;
- import into a book-family database (for example Word Search lexicon);
- schema/model only;
- lookup/reference table;
- source to normalize/deduplicate;
- archive only.

Material intent must be structured canonical state. Prompt generation may consume it, but the prompt text itself is not the source of truth.

### Visual production navigation defect

Severe defect observed in Coloring production:

- clicking a phase tab that is not the immediately next phase can enter a loop;
- focus repeatedly returns to the image-count field;
- the UI stops allowing normal interaction;
- the anomalous phase state appears to be persisted when the project is saved;
- after reopening that project, phase 1/4 can loop;
- the behavior can then appear while opening other projects in the same application session.

Required fix:

- Tab selection must not recursively rebuild/reselect itself.
- Rendering a phase must not write/persist a navigation phase as canonical project content.
- Transient UI navigation state must be isolated per open project/session.
- Initial tab selection must never fire business navigation recursively.
- The same re-entrancy protection must be used for other TabView-based workspaces/review tabs.
- A malformed or stale legacy `Visual.ActivePhase` value must not be able to trap the user.

## Consolidation status

The previous candidate remains physically useful evidence, but these findings block consolidation of the affected interaction layer. A new Windows installer must pass CI and then be physically validated for these fixes before the corresponding behaviors are marked CONSOLIDATED.
