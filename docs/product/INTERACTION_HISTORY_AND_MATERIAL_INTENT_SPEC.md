# Diez interaction, history and material-intent specification

Status: WORKING PRODUCT SPEC. Not consolidated until installed-app validation.

## 1. Three state layers

Diez must distinguish three state layers.

### A. Control edit state

Scope: one editable control or one grid cell.

Examples: a title TextBox, one crossword definition cell, one Word Search word cell, one Romanzo scene-title field.

Requirements:

- Ctrl+Z = undo local edit.
- Ctrl+Y = redo local edit.
- Toolbar/context commands may expose the same actions.
- Local undo/redo must not create a project snapshot for every keystroke.
- Future editable grid cells must use the same editing contract.

### B. Workspace/navigation state

Scope: transient UI state such as selected tab, splitter position, collapsed sidebar and selected list row.

Requirements:

- Must not be treated as canonical editorial content.
- Must never recursively trigger itself.
- Must be isolated per open project/session where appropriate.
- It may be remembered as UI preference, but stale values must never trap project navigation.

### C. Project history

Scope: meaningful publishing state transitions.

Examples:

- material import completed;
- material role/intent changed;
- book type changed;
- visual definition saved;
- Prompt Pack issued;
- Response imported;
- Candidate approved;
- Candidate applied to book;
- Master changed/accepted;
- whole-book validation accepted;
- freeze/finalization created.

Requirements:

- Each meaningful checkpoint has timestamp, reason/action, source state and optional user note.
- Timeline is consultable.
- User can restore an earlier state.
- User can move forward again if no new divergent edit has been committed.
- If the user restores an old state and then edits, the previous forward branch must remain visible in history rather than silently disappearing.
- A restore action itself creates/audits a history event; no destructive invisible rollback.

## 2. Project history storage model

Recommended canonical package structures:

`ProjectHistory[]`

Each entry:

- `HistoryId`
- `CreatedAt`
- `ActionCode`
- `Label`
- `Note`
- `SnapshotHash`
- `SnapshotEntryPath` or equivalent embedded snapshot reference
- `ParentHistoryId`
- `BranchId`
- `IsCurrent`
- optional `AffectedEntityIds`

For early Uno implementation, full `project.json` snapshots may be embedded under `history/` in the `.diez` package. Later this can be optimized with deltas, but UX semantics must not change.

## 3. Sidebar and workspace geometry

### Sidebar

- Default expanded width: approximately 260–280 px.
- Small collapse/expand button always reachable.
- Collapsed state keeps recognizable icons/short labels where practical.
- Collapse is navigation preference, not editorial state.

### Resizable work area

Whenever the screen contains a navigator/editor plus preview, use a user-controlled splitter.

Examples:

- Materials list ↔ material preview.
- Prompt/job list ↔ prompt/asset preview.
- Candidate list ↔ Vision preview.
- Structured book tree ↔ editor.
- Word Search database/grid ↔ record/details/preview.

Requirements:

- minimum useful widths;
- no fixed 1050 px content ceiling;
- splitter drag updates layout without losing current selection;
- resize state may be remembered as UI preference.

## 4. Material intake

### Intake paths

Equivalent first-class paths:

- `Aggiungi materiali…` file picker;
- drag-and-drop one or many files into the Progetto/materials surface.

Both paths call the same canonical import service and duplicate detection.

### Immediate post-import experience

After import:

1. new material is selected;
2. preview is shown automatically;
3. Diez asks/exposes `Come vuoi usare questo materiale?`;
4. user may add a specific instruction;
5. intent is saved as structured material metadata.

The preview must remain visible after a refresh or subsequent import.

## 5. Canonical material intent

A file may serve several publishing purposes. File extension never determines its editorial meaning.

Recommended fields per material:

- `IntentCode`
- `IntentLabel`
- `Instruction`
- `AiUsePolicy`: `ALLOW`, `REFERENCE_ONLY`, `DIRECT_ASSET`, `NEVER_SEND`
- `Fidelity`: `EXACT`, `CLOSE`, `GUIDED`, `LOOSE`, `NOT_APPLICABLE`
- optional `Scope`: project / book position / scene / subject / chapter / dataset
- optional target IDs

### Image intent profiles

1. `DIRECT_BOOK_ASSET`
   - place/use the original file as-is in the book when approved for the target position.
   - AI should not regenerate it unless separately requested.

2. `SUBJECT_IDENTITY_REFERENCE`
   - authoritative visual identity/model for a recurring person, character, object or product.
   - best candidate for Consistent / Identity Anchor workflows.

3. `STYLE_REFERENCE`
   - reference for visual language, line treatment, rendering, palette or mood.
   - not permission to copy composition/subject literally.

4. `COMPOSITION_REFERENCE`
   - reference for framing, viewpoint, layout or arrangement.

5. `ENVIRONMENT_REFERENCE`
   - reference for place/background/setting.

6. `REPLICATE_CLOSELY`
   - preserve the supplied composition/subject closely while recreating according to output constraints.
   - must be explicit because fidelity is materially different from inspiration.

7. `TRANSFORM_REINTERPRET`
   - use source content but produce a materially transformed version.

8. `MODIFY_SPECIFIC_DETAILS`
   - preserve all unspecified aspects as much as possible and change only listed details.
   - requires free-text `Instruction`, e.g. `cambia solo il cappello da rosso a blu; lascia volto, posa e sfondo invariati`.

9. `INSPIRATION_ONLY`
   - use as loose inspiration; do not imitate closely.

10. `ARCHIVE_NEVER_SEND`
    - keep in project for publisher reference; do not include in AI packs.

### Text/document intent profiles

- `AUTHORITATIVE_SOURCE`
- `TRANSFORM_SOURCE`
- `STYLE_TONE_REFERENCE`
- `STRUCTURE_REFERENCE`
- `TERMINOLOGY_AUTHORITY`
- `MASTER_SOURCE_TEXT`
- `ARCHIVE_NEVER_SEND`

### Table/data intent profiles

- `CANONICAL_DATASET`
- `BOOK_FAMILY_DATABASE_IMPORT`
- `SCHEMA_REFERENCE`
- `LOOKUP_REFERENCE`
- `NORMALIZE_DEDUP_SOURCE`
- `ARCHIVE_NEVER_SEND`

The UI should filter/sort the choices by material kind and book family while storing stable intent codes.

## 6. Prompt compiler consumption

Material intent is canonical input to the single compositional prompt system.

Examples:

- `SUBJECT_IDENTITY_REFERENCE` becomes identity/reference constraints.
- `STYLE_REFERENCE` enters the style/reference capability only.
- `MODIFY_SPECIFIC_DETAILS` generates a preservation + scoped-change instruction.
- `ARCHIVE_NEVER_SEND` is excluded from Prompt Pack transport.
- `DIRECT_BOOK_ASSET` is not silently sent to generation.

This must not create a second prompt engine. It is a material-capability input to the same compiler.

## 7. Prompt Pack / Response naming

Canonical basename:

`{SafeProjectName}_{yyyyMMdd}_v{NNN}`

Roles:

- Prompt Pack: `{base}_prompt-pack.zip`
- Expected Response: `{base}_response.zip`

Version rules:

- version increments for every newly issued/regenerated Prompt Pack for the same logical production cycle;
- the Response expected for that pack carries the same version;
- version is stored in the project and in the Prompt Pack manifest/instructions;
- imported provider filename mismatch must be visible as a warning, not a destructive hard failure, provided package identity/manifest verification succeeds;
- internal audit records preserve both provider filename and canonical expected filename.

## 8. Tab/navigation safety

Reusable TabView contract:

- initial `SelectedIndex` assignment must be suppressed from business navigation callbacks;
- one user selection produces at most one navigation action;
- route/render methods must not write canonical state simply because a tab is displayed;
- navigation callbacks must be re-entrancy guarded;
- changing project resets transient tab/navigation state safely;
- stale legacy phase values are clamped/ignored without forced focus loops;
- same helper used for Production and Review tabs.

For guided production, phase access policy may distinguish:

- completed/available phase: freely revisit;
- future phase with prerequisites: show the phase and explain missing prerequisites, or keep it disabled;
- never enter a focus/navigation loop.

## 9. Book-family review

The same interaction principles apply across families:

- Coloring/Image Collection/Illustrated: splitter lists ↔ preview; material intents feed visual/reference behavior.
- Romanzo/Saggio: tree ↔ text editor; control undo/redo; project history around structural edits and accepted AI text.
- Quiz: editable question/options grid with cell undo/redo and project snapshots around bulk generation/import/review.
- Data collection: editable schema/data grid with cell undo/redo, history before normalization/dedup/bulk changes.
- Word Search: database/filter/generate/control/export grid actions support local cell undo/redo and whole-project history before generation/replacement batches.
- Crossword: entry/definition grid supports local undo/redo; project history before bulk AI definition generation/import and Qxw handoff changes.

## 10. Validation gates for the next installer

Before physical delivery:

- no recursive TabView navigation;
- opening one project cannot inherit another project's transient visual phase;
- material picker and drag-drop both import through the same service;
- preview auto-selects a newly imported supported material;
- image material intent can be saved/reloaded;
- Prompt Pack filename uses project/date/version convention;
- expected Response filename is shown/stored;
- sidebar collapse works;
- materials list/preview splitter works;
- Ctrl+Z/Ctrl+Y works in the representative text editor/control implemented for the candidate;
- project history UI can create at least checkpoints and restore backward/forward safely for the candidate scope.
