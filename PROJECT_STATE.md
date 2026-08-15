# Diez Publishing Studio — Project State / Regression Baseline

> **Purpose**: persistent technical memory for Diez Publishing Studio. Read this file before production changes, especially in a new chat/session. Chat history is not the source of truth for regression status.
>
> **Rule**: CI is evidence, but for Win32/Avalonia visual behavior the user's real-PC test is authoritative. Never promote a UI behavior to **CONSOLIDATED** until it has been verified on the real Windows machine.

Last updated: **2026-08-15 (Europe/Rome)**  
Branch: `feature/ai-exchange-mvp`  
PR: **#37 — OPEN / DRAFT / UNMERGED — DO NOT MERGE**  
Current diagnostic/cleanup head at this update: `a9ac761cc5fa0b22375f923fb4d0eeaba890a5fb`

---

## 1. Status vocabulary

| Status | Meaning |
|---|---|
| **CONSOLIDATED** | Proven in real use or an invariant that must not be changed casually. |
| **REAL-PC PASS** | Explicitly observed working on the user's Windows machine. |
| **REAL-PC FAIL** | Explicitly observed broken on the user's Windows machine. |
| **CI PASS** | Automated Windows/headless evidence. Does **not** imply the pixels/input are correct on the user's PC. |
| **EXPERIMENT** | Present in code, not yet validated on the user's PC. |
| **UNKNOWN** | Not tested recently enough to make a claim. |

---

## 2. Non-negotiable core invariants — CONSOLIDATED

Do not rewrite these while fixing desktop navigation/layout unless a failing test specifically proves they are involved.

### Structured Scene

- Scene is first-class with stable hidden `SceneId`.
- Visible scene number/name/description can change without changing identity.
- Scenes can be active/inactive; archived IDs are never recycled.
- Participation is keyed by stable `SubjectId + SceneId`, never display names.
- Environment mode remains `Ambientazione generica / Definisci scene`.
- The same description editor is reused for the current scene.
- `+ Nuova scena` creates a fresh `SceneId`.
- Generic environment remains stored separately from scene-local descriptions.

### Prompt Compiler 3.6

- `VisualPromptIntentSynthesizer` creates synthesized art direction from the user's choices.
- Final image renderer remains `VISUAL_ONLY`.
- Prompt order remains single-image anchor → synthesized art direction → HARD locks.
- Scene-local context wins over generic environment.
- Routing/retry/session/internal IDs must not leak into the image prompt.

### Vision HARD

- Style, Bold & Easy, Cozy, line weight and single-composition remain HARD.
- `scene_participants_match` is promoted to HARD by core even if provider reports SOFT.
- HARD fail blocks approval and leaves candidate incomplete/failing.

### Native editor / startup

- Production startup does not depend on AvaloniaEdit / `VisibleEditorBridgeUi`.
- Essential editable surfaces remain native Avalonia controls.
- Logs remain under `%LOCALAPPDATA%\Diez Publishing Studio\logs\`.
- There is one native `DiezNativeBookFlowEntry`; never create a duplicate legacy entry.

---

## 3. Dispatcher bootstrap — CONSOLIDATED / DO NOT TOUCH CASUALLY

`DispatcherBootstrapProbe.PinAfterPlatformServicesSetup()` clears **only** a prematurely cached `Avalonia.Threading.NullDispatcherImpl`, then reacquires the real Win32 dispatcher.

Healthy trace:

```text
dispatcher-bootstrap | repair=cleared-premature-null-dispatcher
dispatcher-bootstrap | stage=after-platform-after-repair | impl=Avalonia.Win32.Win32DispatcherImpl | ... | supportsRunLoops=True
```

Do not replace this with generic dispatcher reinitialization, do not clear a valid Win32 dispatcher, and do not move unrelated UI work ahead of platform initialization.

Runtime history:
- Avalonia core `11.3.18`.
- AvaloniaEdit `11.3.0` may exist historically but is not a production-startup dependency.

---

## 4. MainWindow lifetime — CONSOLIDATED

Production startup remains the direct completed MainWindow path:

1. classic lifetime starts in `OnExplicitShutdown`;
2. construct `MainWindow`;
3. attach production modules;
4. assign `desktop.MainWindow`;
5. switch to `OnMainWindowClose`;
6. complete framework initialization.

Do not reintroduce Safe Shell or another temporary top-level window.

---

## 5. Home Windows file dialogs — REAL-PC PASS

`WindowsHomeFileDialogUi` owns:
- `DiezOwnedNewProject` — Nuovo progetto
- `DiezOwnedOpenProject` — Apri progetto
- `DiezOwnedImportMaterials` — Aggiungi materiali

They use the MainWindow HWND as owner. Real-PC result: Open/Save dialogs open correctly. Preserve this path and its `before-call / returned` trace markers.

---

## 6. Material import — DATA PASS / HOME VISUAL FAIL

Latest real trace proved:

```text
home-file-dialog | operation=materials | phase=returned | selected=1 | error=0
home-file-dialog | operation=materials | phase=completed | selected=1 | imported=1 | duplicates=0 | errors=0
```

Therefore picker/import/model/save path works. **REAL-PC FAIL**: the imported material is not visibly reflected in `Materiali del progetto` on Home, or Home remains visually stale.

Do not rewrite `MaterialImporter` or the owned dialog first. Once navigation architecture is stable, instrument `_materialsList.ItemsSource`/selection/realized visual state.

---

## 7. Book workflow — LOGICAL PASS / VISUAL FAIL on last real test

Last real-PC trace proved:
- physical `Percorso libro` click reaches handler;
- Tipo libro mounts with non-zero page/root bounds;
- `DiezNativeBookTypeApply` is physically pointer-over/clicked;
- logical transition reaches `Coloring Book · 1/4 Quantità`;
- Quantità controls receive non-zero bounds in the initial page pass;
- Indietro and Home restore execute logically.

**REAL-PC FAIL**: the Windows pixels can remain one step behind / appear frozen. Historically minimize/restore forces a visible update.

Do not confuse logical navigation, healthy model state, `pageHealthy=True`, a successful `RedrawWindow`, or CI raster with real visible navigation.

---

## 8. Renderer policy — EXPERIMENT

Current production code uses:

```csharp
RenderingMode = new[]
{
    Win32RenderingMode.AngleEgl,
    Win32RenderingMode.Software
};
```

Expected marker:

```text
rendering-policy | preferred=AngleEgl | fallback=Software
```

This replaced software-only rendering after stale-frame evidence. **Still not REAL-PC verified.** Keep Software as fallback.

---

## 9. Tipo libro / Titolo libro

`SingleWindowVisualBookIdentityUi` owns `DiezBookTitle`. Current code wraps it in explicit `DiezBookTitleFrame` because the Fluent TextBox outline could disappear while bounds remained valid.

CI raster shows the frame. Real-PC status for this latest frame remains **UNKNOWN/EXPERIMENT**.

Do not remove the title field as a navigation simplification.

---

## 10. Current root-swap architecture — REJECTED AS NEXT FIX TARGET

Current production still uses:
- `SingleWindowOverlayFlowUi` to construct workflow;
- `DetachedWorkflowRootUi` to detach it before first layout;
- `SingleWindowNativeEntryBridgeUi` to swap `Border.Child` between Home and workflow;
- layout recovery / native repaint workarounds around that swap.

This architecture is **not consolidated**.

### New evidence from classic desktop CI — 2026-08-15

The diagnostic workflow was repaired so structural contracts remount workflow through the real `DiezNativeBookFlowEntry` before making physical layout assertions. Headless and classic results are stored separately.

With the workflow physically mounted, the classic desktop probe repeatedly reproduces:

```text
Il controllo 'Note Consistent' ha dimensioni insufficienti: 0 × 0.
```

The Safe Startup Trace proves the containing page is healthy while a dynamically revealed subtree remains stale:

```text
pageHostBounds=6,6,960,665
pageBounds=0,0,960,665
panelParentBounds=0,0,960,1756
panelVisible=True
panelBounds=0,0,0,0
notesBounds=0,0,0,0
```

Three deliberately local strategies were tested and **all failed** in classic desktop:

1. invalidate measure/arrange/visual on panel → parent → page → pageHost;
2. keep panel mounted with zero height/opacity while OFF, restore Auto while ON;
3. explicitly Measure/Arrange only the Quantity ScrollViewer after visibility change;
4. keep panel mounted and hide/restore its direct children instead of collapsing the panel.

In every case the parent StackPanel retained the old extent and the Consistent panel remained 0×0. These temporary product workarounds have been removed from the production module list and the temporary module file has been deleted.

**Decision:** stop adding local `Invalidate*`, `Measure/Arrange`, `RedrawWindow`, opacity/height, or child-visibility workarounds to this root-swap model.

### Next architecture

The next production change must replace runtime root swapping with a stable visual tree:

> Home and Workflow must both be parented from startup under one permanent MainWindow root. Navigation switches active surface by input/visibility state without replacing `Border.Child` and without detaching/reparenting the workflow root during navigation.

Requirements for the stable-root change:
- one MainWindow only;
- Home and Workflow created/parented before normal interaction;
- no runtime `Border.Child = home/overlay` swapping;
- inactive surface cannot receive pointer/keyboard input;
- preserve owned Win32 file dialogs;
- preserve one `DiezNativeBookFlowEntry`;
- update CI contracts so they no longer enforce obsolete `Border.Child == overlay` semantics;
- remove/disable `DetachedWorkflowRootUi` and root-level repaint recovery only after replacement contracts cover the stable-root architecture.

---

## 11. Diagnostic CI facts

`UI Contract Diagnostic` now has:
- durable `START/OK` breadcrumbs per sub-contract;
- process timeout to prevent permanent hangs;
- separate `ui-headless-flow-contract.txt` and `ui-classic-flow-contract.txt`;
- uploaded `ui-classic-safe-startup-trace.log`.

Important: classic step has historically used `continue-on-error`, so the GitHub step badge alone is **not authoritative**. Always inspect `ui-classic-flow-contract.txt` or process exit code.

A separate intermittent headless-only failure has also been observed:

```text
The given key 'fonts:SystemFonts' was not present in the dictionary.
```

That originates in Avalonia's synthetic headless font service and must not be confused with the classic Win32 layout defect.

---

## 12. Real-PC regression matrix

Update this table after every user test, before starting the next product fix.

| Area | Last real-PC result | State | Notes |
|---|---|---|---|
| Installer starts | Starts / installs | **REAL-PC PASS** | Conservative installer path restored. |
| MainWindow startup | Opens without crash | **REAL-PC PASS** | Dispatcher repair healthy. |
| Nuovo progetto dialog | Opens | **REAL-PC PASS** | Owned Win32 dialog. |
| Nuovo progetto create | Completes | **REAL-PC PASS** | Post-dialog work short. |
| Apri progetto dialog | Opens | **REAL-PC PASS** | Owned Win32 dialog. |
| Aggiungi materiali dialog | Opens | **REAL-PC PASS** | Multi-select owned dialog. |
| Material import data | `imported=1`, `errors=0` | **REAL-PC PASS** | Do not rewrite importer without evidence. |
| Material visible in Home | Not visibly updated | **REAL-PC FAIL** | Separate visual refresh problem. |
| Percorso libro click | Works | **REAL-PC PASS** | Handler reached. |
| Tipo libro logical mount | Works | **REAL-PC PASS** | Healthy bounds in trace. |
| Titolo libro field latest explicit frame | Not yet retested | **UNKNOWN / EXPERIMENT** | CI raster visible. |
| Usa questo Tipo libro | Handler reached | **REAL-PC PASS** | Pointer-over/click traced. |
| Tipo libro → Quantità logical | Happens | **REAL-PC PASS** | State advances. |
| Visual Tipo libro → Quantità | Appears blocked/stale | **REAL-PC FAIL** | Main navigation defect. |
| Indietro logical | Executes | **REAL-PC PASS** | Trace confirms. |
| Home progetto logical | Executes | **REAL-PC PASS** | Trace confirms. |
| Visual Home/Back | Can remain stale/blocked | **REAL-PC FAIL** | Root architecture under replacement. |
| ANGLE renderer experiment | Not yet real-PC tested | **UNKNOWN / EXPERIMENT** | Keep fallback Software. |

---

## 13. Known regression traps

- Never use CI green as permission to replace a real-PC working path.
- Never resurrect ownerless/mixed file dialogs.
- Never create duplicate Percorso libro entries.
- Never reintroduce AvaloniaEdit into production startup.
- Never change Scene/Subject stable IDs to display-name keys.
- Never flatten Prompt Compiler 3.6 back into a raw option list.
- Never weaken `scene_participants_match` from HARD.
- Never touch dispatcher bootstrap while diagnosing navigation unless dispatcher trace itself is unhealthy.
- Do not add another root/layout workaround to the old root-swap architecture after the failed experiments recorded above.

---

## 14. Change protocol

Before production changes:

1. Read this file.
2. Identify exact REAL-PC FAIL rows / CI evidence being addressed.
3. State which CONSOLIDATED areas are out of scope.
4. Change one causal layer at a time.
5. Update CI to represent the intended architecture; do not let old tests force obsolete behavior.
6. Build installer + portable from the exact same head SHA.
7. After user test, update this matrix **before** the next fix.
8. If a REAL-PC PASS regresses, that regression becomes first priority.

---

## 15. New-chat handoff

At the start of a new ChatGPT conversation:

> Read `PROJECT_STATE.md` on `feature/ai-exchange-mvp` before proposing or making changes. Treat the Real-PC matrix as authoritative. Keep PR #37 open, draft and unmerged. Never merge it. Do not promote CI-only evidence to real-PC success.

Then fetch current PR #37 head and inspect commits since this file's last update before editing.

---

## 16. PR safety

PR #37 must remain:

```text
state: open
draft: true
merged: false
```

**Never merge PR #37 unless the user explicitly changes this instruction.**
