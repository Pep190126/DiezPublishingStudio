# Round 5.3 — Coloring / immagini: semantic freeze prima del provider

Status: **IMPLEMENTAZIONE IN CORSO / NON CONSOLIDATA**

Data: 2026-08-24

Branch: `spike/uno-platform-ui`

## Obiettivo

Correggere insieme i difetti emersi dai test fisici dei progetti 10 e 17 senza introdurre scorciatoie che contraddicano l'architettura approvata.

## Decisioni HARD

1. **UI italiana; provider-facing prompt engineering in inglese tecnico.**
   - Etichette, messaggi, errori e spiegazioni restano italiani a video.
   - `PROMPT.md`, `instructions.md` e i visual prompt destinati all'AI non devono essere una trascrizione/traduzione quasi letterale della UI.

2. **Il renderer immagini non pianifica la serie.**
   - Un valore come `3 soggetti di Halloween` è un tema aggregato, non il soggetto di IMG-001/002/003.
   - Un residuo legacy come `3 images di soggetti per Halloween` deve essere riconosciuto come lo stesso significato aggregato.
   - Il Prompt Pack non può chiedere al provider di scegliere zucca/fantasma/gatto o altri soggetti al posto di Diez.

3. **Freeze semantico prima del Prompt Pack.**
   - Se esistono soggetti/scene strutturati concreti, Diez li congela nelle Work Unit.
   - Se il tema aggregato non è ancora risolto, il Prompt Pack viene bloccato con messaggio italiano chiaro.
   - È preferibile un blocco esplicito a una delega implicita al renderer.

4. **Nessun hardcode tematico nel compiler.**
   - Non esiste `Halloween => zucca, fantasma, gatto` nel Core.
   - Il futuro planner AI interno produrrà un piano strutturato, validato e persistito prima del Prompt Compiler.

5. **Response header completo obbligatorio.**
   Il Response manuale deve copiare esattamente dal manifest:
   - `project_id`
   - `job_id`
   - `prompt_pack_id`
   - `request_snapshot_id`
   - `transport = MANUAL`

   Questi campi sono obbligatori anche se tutte le Work Unit sono `FAILED`/`INCOMPLETE`.

6. **FAILED e INCOMPLETE assetless sono tentativi provider incompleti validi.**
   - Non devono generare asset Candidate fittizi.
   - Devono poter essere registrati come tentativi incompleti senza far fallire l'import solo perché non esiste `primary_asset`.

## Compatibilità progetto nuovo / progetto riaperto

A parità di decisioni visibili, progetto nuovo e progetto legacy riaperto devono arrivare allo stesso piano semantico, esclusi gli ID tecnici nuovi.

In particolare:

- `3 soggetti di Halloween`
- `3 images di soggetti per Halloween`

sono entrambi **non atomici** e non possono diventare `PRIMARY SUBJECT` di una singola immagine.

## Stato del planner automatico

Il contratto `DIEZ_SEMANTIC_SUBJECT_RESOLUTION_SPEC.md` resta autorevole.

In questa tranche non viene simulato un planner automatico tramite liste hardcoded o regole tematiche fragili. Finché un provider testuale/API interno a Diez non è realmente disponibile, l'assenza di un piano concreto è un gate di preflight.

Il futuro planner dovrà inserirsi così:

`decisioni utente -> planner semantico Diez -> validazione -> stato canonico -> Prompt Compiler -> renderer immagini`

senza cambiare il contratto di freeze introdotto qui.

## Regression gate

Nuovo progetto di test: `tests/Diez.VisualSemanticRegression`.

Deve verificare almeno:

- tema aggregato nuovo bloccato;
- residuo aggregato legacy bloccato allo stesso modo;
- tre soggetti strutturati concreti producono tre Work Unit atomiche;
- nessun `SERIES SUBJECT ASSIGNMENT` arriva al renderer;
- `PROMPT.md` non delega al provider la scelta dei soggetti;
- contratto Response richiama gli header Diez obbligatori.

## Consolidamento

Round 5.3 resta **NON CONSOLIDATO** finché:

1. CI Windows non produce un installer TECHNICALLY_VERIFIED;
2. il progetto installato viene provato fisicamente;
3. un progetto nuovo e uno riaperto mostrano comportamento semantico coerente;
4. Prompt Pack e Response reali vengono nuovamente ispezionati.
