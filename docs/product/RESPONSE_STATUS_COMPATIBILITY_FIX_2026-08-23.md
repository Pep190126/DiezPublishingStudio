# Diez Publishing Studio — Response status compatibility fix · 2026-08-23

Status: **TECNICAMENTE VERIFICATO — IN ATTESA DI PROVA FISICA DELL'IMPORT REALE**

## Riscontro fisico

Durante la prova installata della Round 3, l'import del Response reale `NuovoProgetto14_20260823_v001_response(1).zip` è stato bloccato con:

`[RESULT_STATUS_INVALID]: IMG-001: status “CANDIDATE” non riconosciuto.`

Il Prompt Pack e il Response reali sono stati confrontati. Project ID, Job ID, Prompt Pack ID, Work Unit, Candidate version e asset risultano coerenti; il blocco osservato è il vocabolario dello status del risultato.

## Causa

Diez usa due livelli distinti:

- **status di trasporto Response**: `COMPLETE`, `INCOMPLETE`, `FAILED`;
- **status interno della versione**: una versione importata entra nel lifecycle come `CANDIDATE` prima di review/approval/application.

Il producer ha usato `CANDIDATE` nel manifest Response, cioè il nome dello stato interno al posto dello status canonico di trasporto.

Inoltre il normalizzatore visuale non contemplava esplicitamente `INCOMPLETE`, benché previsto dal protocollo.

## Fix

Alla frontiera di import visuale:

- `CANDIDATE` viene accettato esclusivamente come alias compatibile di `COMPLETE`;
- `INCOMPLETE` viene accettato e mantenuto come `INCOMPLETE`;
- `COMPLETE`, `COMPLETED`, `SUCCESS`, `SUCCEEDED` continuano a normalizzare a `COMPLETE`;
- `FAILED`, `FAIL` continuano a normalizzare a `FAILED`;
- qualsiasi altro status sconosciuto continua a essere rifiutato con `RESULT_STATUS_INVALID`.

La normalizzazione avviene prima dell'ingest. Non vengono rilassati i controlli su Project/Job/Prompt Pack/Work Unit/Candidate version/asset.

Il Prompt Pack resta canonico e deve continuare a chiedere al producer `COMPLETE / INCOMPLETE / FAILED`; `CANDIDATE` è solo compatibilità in ingresso, non un nuovo status transport consigliato.

## Regression gate

`tests/Diez.VisualBook.Pianist/Program.cs` verifica esplicitamente:

- `CANDIDATE → COMPLETE`;
- `INCOMPLETE → INCOMPLETE`;
- uno status ignoto resta rifiutato.

## Candidata Windows

Round 3.1 source SHA:

`105afdc2bf9824ca180d6b345b903eda828f397c`

Workflow `Uno Windows Consolidation Candidate`, run `#18`, run ID `32650599413`.

CI: Visual gate, restore, publish, verifica EXE, packaging, smoke install/launch/uninstall e artifact upload tutti `success`.

Setup SHA-256:

`6cac41ec91f7bda5f269a427e36608ef6b740a0e9d072bfd380e7c78783e9d2c`

La correzione diventa **CONSOLIDATA** solo dopo che lo stesso Response reale viene importato con successo nell'app installata e compare la relativa Candidate/anteprima in Vision.
