# Diez Publishing Studio — Project State / Regression Baseline

> **Purpose**: this file is the persistent technical memory for Diez Publishing Studio.  Read it before making production changes, especially from a new chat/session.  Chat history is not the source of truth for regression status.
>
> **Rule**: CI success is evidence, but for Win32/Avalonia visual behavior the user's real-PC test is authoritative.  Never promote a UI behavior to **CONSOLIDATED** until it has been verified on the real Windows machine.

Last updated: **2026-08-15 (Europe/Rome)**  
Branch: `feature/ai-exchange-mvp`  
PR: **#37 — OPEN / DRAFT / UNMERGED — DO NOT MERGE**  
Head when this baseline was created: `f339c8afd90b69928c48a8d2a615d3a4a2147539`

---

## 1. Status vocabulary

| Status | Meaning |
|---|---|
| **CONSOLIDATED** | Proven in real use or an invariant that must not be changed casually. |
| **REAL-PC PASS** | Explicitly observed working on the user's Windows machine. |
| **REAL-PC FAIL** | Explicitly observed broken on the user's Windows machine. |
| **CI PASS** | Automated Windows/headless contract passes. This does **not** imply the real desktop pixels/input are correct. |
| **EXPERIMENT** | Present in current code, but not yet validated on the user's PC. |
| **UNKNOWN** | Not tested recently enough to make a claim. |

---

## 2. Non-negotiable core invariants — CONSOLIDATED

These are not part of the current desktop-navigation experiment. Do not rewrite or remove them while fixing UI behavior unless a failing test specifically proves they are involved.

### Structured Scene

- Scene is first-class and has a stable hidden `SceneId`.
- Visible scene number/name/description can change without changing identity.
- Scenes can be active/inactive; archived identities are not recycled.
- Subject participation is keyed by stable `SubjectId` + `SceneId`, not display names.
- Environment mode remains `Ambientazione generica / Definisci scene`.
- The same description editor is reused for the current scene.
- `+ Nuova scena` creates a fresh `SceneId`.
- Generic environment is stored separately from scene-local descriptions.

### Prompt Compiler 3.6 / rendering contract

- `VisualPromptIntentSynthesizer` creates synthesized art direction from the user's visual choices.
- Final visual renderer remains `VISUAL_ONLY`.
- Final prompt order keeps single-image anchor → synthesized art direction → HARD locks.
- Scene-local context wins over generic environment.
- Routing/retry/session/internal IDs must not leak into the image prompt.

### Vision HARD

- Style, Bold & Easy, Cozy, line weight and single-composition constraints stay HARD.
- `scene_participants_match` is promoted to HARD by the core even if a provider reports it as SOFT.
- HARD failure blocks approval and leaves the candidate incomplete/failing.

### Native editor / startup constraints

- Production startup does not depend on AvaloniaEdit / `VisibleEditorBridgeUi`.
- Essential editable surfaces are native Avalonia controls (`TextBox`, etc.).
- Crash logs remain under `%LOCALAPPDATA%\Diez Publishing Studio\logs\`.
- The production app uses one native `DiezNativeBookFlowEntry`; do not create duplicate legacy entries.

---

## 3. Dispatcher bootstrap — CONSOLIDATED / DO NOT TOUCH CASUALLY

`DispatcherBootstrapProbe.PinAfterPlatformServicesSetup()` is a targeted repair for Avalonia 11.3.18. It clears **only** a prematurely cached UI dispatcher whose implementation is `Avalonia.Threading.NullDispatcherImpl`, then reacquires the real Win32 dispatcher.

Expected healthy trace includes:

```text
dispatcher-bootstrap | repair=cleared-premature-null-dispatcher
dispatcher-bootstrap | stage=after-platform-after-repair | impl=Avalonia.Win32.Win32DispatcherImpl | ... | supportsRunLoops=True
```

Do not replace this with generic dispatcher reinitialization, do not clear a valid Win32 dispatcher, and do not move unrelated UI work ahead of platform initialization.

Current relevant package/runtime history:

- Avalonia core: `11.3.18`
- AvaloniaEdit package may still exist historically at `11.3.0`, but it is not a production-startup dependency.

---

## 4. MainWindow lifetime — CONSOLIDATED

Production startup currently follows the direct completed MainWindow path:

1. start classic desktop lifetime in `OnExplicitShutdown`;
2. construct `MainWindow`;
3. attach production modules;
4. assign `desktop.MainWindow`;
5. switch to `ShutdownMode.OnMainWindowClose`;
6. complete framework initialization.

Do not reintroduce a Safe Shell transition or another temporary top-level window as a UI-navigation workaround.

---

## 5. Home Windows file dialogs — REAL-PC PASS

The current Home commands are owned by `WindowsHomeFileDialogUi` and use one Win32 common-dialog path with the MainWindow HWND as owner:

- `DiezOwnedNewProject` — Nuovo progetto
- `DiezOwnedOpenProject` — Apri progetto
- `DiezOwnedImportMaterials` — Aggiungi materiali

**Real-PC result:** the Open/Save dialogs now open correctly. Preserve the owned HWND behavior and the `before-call / returned` trace markers.

Observed real-PC timings show the Windows shell dialog itself can take roughly 9–12 seconds before returning. The application work after the dialog is much shorter. Do not misdiagnose this shell delay as project serialization or UI refresh without phase evidence.

---

## 6. Material import — DATA PASS / HOME VISUAL FAIL

### What is proven on the real PC

The most recent real trace reported:

```text
home-file-dialog | operation=materials | phase=returned | selected=1 | error=0
home-file-dialog | operation=materials | phase=completed | selected=1 | imported=1 | duplicates=0 | errors=0
```

Therefore:

- the picker returned a file;
- the importer accepted it;
- the project model gained the material;
- no import exception was reported;
- the import path saved/refreshed according to current code.

### What is broken

**REAL-PC FAIL:** after import, the newly imported material is not visibly reflected in `Materiali del progetto` on the Home screen (or the Home frame remains visually stale).

### Rule for the next fix

Do **not** rewrite `MaterialImporter` or the owned file dialog first. Instrument/verify the Home list refresh path:

- `project.Materials.Count` after import;
- `_materialsList.ItemsSource` count after `RefreshViews`;
- selected index after `SelectMaterial`;
- realized/list visual state after a render turn.

Treat this as a Home visual-refresh problem until evidence proves otherwise.

---

## 7. Book workflow navigation — LOGICAL PASS / VISUAL FAIL on last real test

### Real-PC trace facts

On the last real-PC test before the ANGLE renderer experiment:

- `Percorso libro` received the physical click.
- Workflow root mounted.
- `Tipo libro` obtained non-zero `overlayBounds`, `pageHostBounds` and `pageBounds`.
- `DiezNativeBookTypeApply` was visible, enabled and had non-zero bounds.
- Physical pointer trace reported `pointerOverButtons=DiezNativeBookTypeApply:Usa questo Tipo libro`.
- The click handler ran.
- The logical title advanced to `Coloring Book · 1/4 Quantità · 1 immagine`.
- Quantità page controls had non-zero bounds.
- `Indietro` logically returned to `Tipo libro`.
- `Home progetto` logically restored the Home root.

This proves the navigation handlers and state transitions are substantially alive.

### Real-PC failure

Despite the logical transition, the pixels shown by the Windows desktop can remain one step behind or appear blocked. Historically, minimizing/restoring the MainWindow forced a visible update.

**Current diagnosis:** the dominant unresolved problem is the relationship between Avalonia visual/render state and what Win32 actually presents, not a missing click handler.

Do not claim this is solved because `pageHealthy=True`, because `RedrawWindow` returned true, or because a raster test passed.

---

## 8. Current workflow surface architecture — NOT CONSOLIDATED

Current production module order includes:

```text
Layout principale
Host single-window
Workflow detached prima del layout
Percorso nativo SW-FLOW-12
Ingresso percorso nativo
...
Progetto attivo e ripresa percorso
Avvio guidato SW-FLOW-12
Dialoghi Home Windows owned
```

The current navigation architecture still uses:

- `SingleWindowOverlayFlowUi` to create the workflow visual tree;
- `DetachedWorkflowRootUi` to detach it before normal layout and recover zero-size pages;
- `SingleWindowNativeEntryBridgeUi` to swap `Border.Child` between Home and workflow;
- explicit Win32 repaint attempts (`RedrawWindow`) after workflow layout recovery.

This architecture is **under investigation**, not a protected invariant.

### Decision gate

First test the current renderer experiment on the real PC.

If the real-PC frame still becomes stale while the trace shows successful logical navigation and healthy bounds, stop adding more `Invalidate*`, `Measure/Arrange`, `RedrawWindow` or Z-index patches to the root-swap model. The next architectural move should be:

> Keep Home and Workflow permanently inside one stable visual tree from startup, both parented and measurable; switch only visibility/opacity/Z-order/hit-testing. Do not reparent them during navigation.

If that change is made, the UI contracts/raster assertions must be updated so CI no longer enforces the obsolete `Border.Child == overlay` root-swap architecture.

---

## 9. Renderer policy — EXPERIMENT, NOT REAL-PC VERIFIED YET

Current head `f339c8af...` changed Windows rendering from software-only to:

```csharp
RenderingMode = new[]
{
    Win32RenderingMode.AngleEgl,
    Win32RenderingMode.Software
};
```

Expected startup marker:

```text
rendering-policy | preferred=AngleEgl | fallback=Software
```

Reason for the experiment: on the previous real-PC build, Avalonia state/hit-testing advanced correctly while the visible Win32 frame could remain stale until minimize/restore.

**Do not mark this renderer policy CONSOLIDATED until the user tests it on the real machine.** Software remains the fallback.

---

## 10. Tipo libro / Titolo del libro

The title field is owned by `SingleWindowVisualBookIdentityUi` and must remain part of the Tipo libro page.

Current experiment adds an explicit `Border` named `DiezBookTitleFrame` around the native `DiezBookTitle` TextBox because the Fluent TextBox outline could be visually absent even when the control had valid bounds.

Current automated raster shows the frame and title field physically present, but this is still **CI PASS**, not yet a **REAL-PC PASS** for head `f339c8af...`.

Do not remove the title field as a navigation simplification.

---

## 11. Real-PC regression matrix

This table must be updated after every user test. A future chat should read this table before touching code.

| Area | Last real-PC result | State | Notes |
|---|---|---|---|
| Installer starts | Starts / installs | **REAL-PC PASS** | Earlier compression experiment caused trouble; conservative installer restored. |
| MainWindow startup | Opens without crash | **REAL-PC PASS** | Dispatcher repair healthy. |
| Nuovo progetto dialog | Opens | **REAL-PC PASS** | Owned Win32 dialog. |
| Nuovo progetto create | Completes | **REAL-PC PASS** | Post-dialog work observed ~197–222 ms in recent traces. |
| Apri progetto dialog | Opens | **REAL-PC PASS** | Owned Win32 dialog. |
| Aggiungi materiali dialog | Opens | **REAL-PC PASS** | Multi-select common dialog. |
| Material import data | `imported=1`, `errors=0` | **REAL-PC PASS** | Do not rewrite importer without evidence. |
| Material visible in Home list | Not visibly updated | **REAL-PC FAIL** | Diagnose Home/list rendering/refresh. |
| Percorso libro physical click | Works | **REAL-PC PASS** | Handler reached. |
| Tipo libro page logical mount | Works | **REAL-PC PASS** | Healthy bounds in trace. |
| Titolo libro field | Border/box appeared missing on last tested build | **REAL-PC FAIL** | Current explicit frame is unverified experiment. |
| Usa questo Tipo libro click | Handler reached | **REAL-PC PASS** | Pointer-over and click traced. |
| Tipo libro → Quantità logical transition | Happens | **REAL-PC PASS** | Trace title and controls advance. |
| Visual transition Tipo libro → Quantità | Appears blocked/stale | **REAL-PC FAIL** | Main unresolved UI defect. |
| Indietro logical action | Executes | **REAL-PC PASS** | Returned logically to Tipo libro in trace. |
| Home progetto logical restore | Executes | **REAL-PC PASS** | Root restore traced. |
| Visual Home/Back navigation | Can remain stale/blocked | **REAL-PC FAIL** | Do not confuse logical success with visible success. |
| ANGLE renderer experiment | Not yet tested by user | **UNKNOWN / EXPERIMENT** | Head `f339c8af...`. |

---

## 12. CI snapshot at baseline creation

For head `f339c8afd90b69928c48a8d2a615d3a4a2147539` at the time this file was created:

| Workflow | Snapshot |
|---|---|
| Structured Scene Contract | completed / success |
| Core Headless Dotnet | completed / success |
| Core Apphost Regression | completed / success |
| Windows Installer Deterministic | completed / success |
| Windows Interactive Installer Probe | completed / success |
| UI Contract Diagnostic | still in progress at snapshot time |

Again: this table is provenance, not proof of correct pixels on the user's Windows desktop.

---

## 13. Known historical regression traps

- Do not use CI green as permission to replace a real-PC working path.
- Do not resurrect ownerless or mixed file-dialog implementations now that owned Win32 dialogs work.
- Do not create duplicate `Percorso libro` entries.
- Do not reintroduce AvaloniaEdit into the production startup path.
- Do not change stable Scene/Subject IDs to display-name keys.
- Do not flatten Prompt Compiler 3.6 back into a raw option list.
- Do not weaken `scene_participants_match` from HARD.
- Do not touch dispatcher bootstrap while diagnosing page navigation unless the trace shows the dispatcher itself is unhealthy.
- Do not add another UI-layer workaround before checking whether it invalidates a previously REAL-PC PASS behavior.

---

## 14. Change protocol for future work

Before changing production code:

1. Read this file.
2. Identify the exact failing row(s) in the real-PC regression matrix.
3. State which **CONSOLIDATED** areas are out of scope and will not be touched.
4. Change one causal layer at a time.
5. Add/adjust CI only to represent the intended architecture; do not let old tests force an obsolete workaround.
6. Build installer + portable from the exact same head SHA.
7. After the user tests, update the real-PC matrix **before** starting the next fix.
8. If a previously passing real-PC row regresses, treat that regression as first priority.

---

## 15. Handoff instructions for a new ChatGPT conversation

At the start of a new chat about this repository:

> Read `PROJECT_STATE.md` on branch `feature/ai-exchange-mvp` before proposing or making changes. Treat the Real-PC regression matrix as authoritative for desktop behavior. Keep PR #37 open, draft and unmerged. Never merge it. Do not promote CI-only results to real-PC success.

Then fetch current PR #37 head and compare it with the `Head when this baseline was created` above. If the head changed, inspect commits/files since this baseline before editing.

---

## 16. PR safety

PR #37 must remain:

```text
state: open
draft: true
merged: false
```

**Never merge PR #37 unless the user explicitly changes this instruction in the future.**
