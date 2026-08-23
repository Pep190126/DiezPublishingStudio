# Round 5.3 — Coloring / immagini: semantic freeze prima del provider

Status: **TECHNICALLY_VERIFIED / NON CONSOLIDATA**

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

Verificato insieme al gate visuale storico:

- tema aggregato nuovo bloccato;
- residuo aggregato legacy bloccato allo stesso modo;
- tre soggetti strutturati concreti producono tre Work Unit atomiche;
- nessun `SERIES SUBJECT ASSIGNMENT` arriva al renderer;
- `PROMPT.md` non delega al provider la scelta dei soggetti;
- contratto Response richiama gli header Diez obbligatori;
- gli scenari storici del Visual Book Pianist restano verdi dopo l'allineamento delle aspettative al nuovo semantic freeze.

## Installer Windows autorevole Round 5.3

La pipeline finale è stata ripulita da ogni patch/apply one-shot: il run autorevole costruisce esclusivamente sorgente già committato.

- Workflow: `Uno Windows Consolidation Candidate`
- Run number: **32**
- Run ID: **32674072201**
- Source SHA: **`77cd6686034f91d84e8b3765df9f04382db975e4`**
- Candidate status: **TECHNICALLY_VERIFIED**
- Visual book gate: `success`
- Round 5.3 semantic regression: `success`
- Restore: `success`
- Publish: `success`
- Verify executable: `success`
- Package: `success`
- Smoke install/launch/uninstall: `success`
- Artifact upload: `success`
- Artifact ID: **9502209022**
- Artifact name: `DiezPublishingStudio-UnoPreview-Windows-x64`
- Artifact ZIP bytes (GitHub metadata): **94447334**
- Artifact ZIP SHA-256: **`2f5031ce84ca2bbd5defeaab0fafc5e08c7fd51259caba9d5e2b4b23f7e50bdd`**
- Setup file: `DiezPublishingStudio-UnoPreview-Setup.exe`
- Setup bytes: **94975930**
- Setup SHA-256: **`1d52b19beaa36e698510bf04e2abecbd791226e4784bfd95bceb6d193463e1a2`**

Il download locale dell'artifact e l'estrazione del Setup hanno confermato entrambi gli SHA-256 sopra.

## Test fisico richiesto

Round 5.3 resta **NON CONSOLIDATO** finché il publisher non prova l'app installata.

Test prioritari:

1. riaprire un progetto legacy equivalente al progetto 10 e tentare la rigenerazione del Prompt Pack;
2. creare un progetto nuovo equivalente al progetto 17;
3. con solo un tema aggregato (`3 soggetti di Halloween`) verificare che Diez non crei prompt che chiedono al renderer di scegliere i soggetti, ma mostri il gate italiano di piano soggetti non risolto;
4. definire invece tre soggetti concreti strutturati e verificare che il Prompt Pack venga creato con una Work Unit atomica per soggetto;
5. controllare che `PROMPT.md` / `instructions.md` siano provider-facing in inglese tecnico e non contengano la vecchia istruzione di pianificazione esterna;
6. verificare che un Response privo di `request_snapshot_id` o `transport=MANUAL` venga rifiutato;
7. verificare che un Response `FAILED`/`INCOMPLETE` senza asset venga registrato come tentativo incompleto e non trasformato in Candidate fittizia.

Solo dopo questi test fisici il Round 5.3 potrà essere considerato consolidabile.
