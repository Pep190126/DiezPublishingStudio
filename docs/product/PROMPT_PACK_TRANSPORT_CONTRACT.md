# Contratto Prompt Pack — trasporto AI manuale e API

Status: **DIRETTIVA DI PRODOTTO — NON CONSOLIDATA FINO A TEST FISICO**

Vale la regola di `docs/product/SPEC_CONSOLIDATION_MEMO.md`: CI/build/pianisti verificano tecnicamente, ma solo la prova fisica della build installata sul PC dell'utente può promuovere il comportamento a CONSOLIDATO.

## Principio storico da preservare

Nel percorso Coloring della Diez precedente il **Prompt Pack è un file ZIP da consegnare all'AI**. Non è, come percorso principale, un testo che l'utente deve copiare e incollare manualmente una volta per ogni contenuto.

La filosofia viene mantenuta per tutte le famiglie che usano produzione AI.

## Due strade di produzione

Dopo la preparazione del Prompt Pack esistono due trasporti distinti ma convergenti:

1. **Manuale** — Diez crea un Prompt Pack ZIP; l'utente consegna/upload-a quel singolo ZIP al sistema AI scelto.
2. **Via API** — Diez invia lo stesso lavoro canonico attraverso il provider/API configurato.

Le due strade cambiano il trasporto, non il modello editoriale. Devono convergere sulle stesse Work Unit, Candidate, review/Vision, approvazione e successivo `Porta nel libro`.

## Lotto visuale

Per un libro con N immagini:

- Diez conserva **N Work Unit** indipendenti per versioning, audit, Vision e applicazione editoriale;
- il percorso manuale normale crea **UN SOLO Prompt Pack ZIP** per il lotto selezionato;
- lo ZIP contiene il prompt/orchestrazione del lotto, i prompt visuali delle singole immagini, manifest tecnico, snapshot e gli eventuali materiali/reference necessari;
- l'utente non deve essere obbligato ad aprire N chat o creare N Prompt Pack;
- l'AI deve produrre N asset distinti, non un collage/griglia/contact sheet;
- quando supportato, il rientro preferito è **UN SOLO Response ZIP** con un risultato distinto per ciascuna Work Unit.

## Struttura minima del Prompt Pack ZIP manuale

Il pacchetto deve includere almeno:

- `PROMPT.md` — ingresso principale leggibile dall'AI per eseguire l'intero lotto;
- `prompt-manifest.json` — identità tecniche, Work Unit, candidate version e ricomposizione;
- `instructions.md` — protocollo/istruzioni di rientro;
- `inputs/` — paradigmi/reference/base image quando applicabili.

`PROMPT.md` deve contenere i prompt provider-facing effettivi delle N immagini e spiegare che l'intero ZIP è la consegna da eseguire.

## Separazione prompt visuale / metadata

Gli ID tecnici servono a Diez ma non devono contaminare il renderer. In particolare WorkUnitId, PromptPackId, RequestSnapshotId, routing, retry, session ID, hash e nomi tecnici restano nel manifest/protocollo e non dentro il prompt visuale inviato al generatore di immagini.

Restano autoritativi gli invarianti del Prompt Compiler corrente: ART DIRECTION sintetizzata, HARD locks, Scene locale prioritaria rispetto all'ambiente generico e partecipazione dei soggetti risolta semanticamente.

## Esito del primo test fisico Windows — 17 agosto 2026

Il primo giro installato realmente sul PC ha validato la creazione del Prompt Pack, ma ha individuato due problemi **NON CONSOLIDATI**:

1. Il Response ZIP realmente prodotto da ChatGPT non usava esattamente il dialetto sintetico della CI. Conteneva `diez-response.json`, `source_prompt_pack_id`, `results`, status `COMPLETED` e nessun `package_id`. La prima build Uno accettava soltanto `response-manifest.json`, `prompt_pack_id`, `items`, `COMPLETE` e `package_id`, quindi la selezione del Response non produceva Candidate visibili.
2. I prompt del lotto Coloring conservavano Kawaii / Cozy / Bold & Easy, ma erano troppo generici per singola immagine e non trasportavano in modo sufficientemente esplicito Scene, partecipanti e alcuni vincoli HARD della pipeline Avalonia. Gli output risultavano tecnicamente B/W e colorabili ma semanticamente troppo geometrici/schematici e non affidabili come Kawaii + Cozy.

Il contratto corretto da verificare nella prossima installazione è quindi:

- l'importer accetta sia il dialetto canonico sia il dialetto provider osservato, **senza** allentare ProjectId, JobId, PromptPackId, WorkUnitId e candidate version;
- se `package_id` manca, Diez usa l'hash SHA-256 dell'intero ZIP come identità idempotente del package;
- `COMPLETED` viene normalizzato a `COMPLETE`;
- asset sotto `assets/` e `content/` vengono risolti in modo sicuro;
- il Prompt Pack viene ricompilato immediatamente prima del freeze ZIP usando lo stato corrente di Subject, Scene, partecipazione e Consistent;
- per Coloring tornano autoritativi i vincoli Avalonia su stile semantico, Bold & Easy, Cozy, line weight, qualità del disegno/anatomia, pure black/white, aree colorabili, contorni, micro-dettaglio, leggibilità, niente testo/watermark, una sola composizione e cast della Scena;
- gli stessi vincoli applicabili devono ricomparire nella checklist Vision e ogni HARD non-PASS deve bloccare l'approvazione.

Queste correzioni restano **PENDING PHYSICAL RETEST**. Anche una CI completamente verde non le promuove a CONSOLIDATE.

## Fallback clean-room

La modalità storica "una chat/render context pulito per Work Unit" resta disponibile come **fallback di sicurezza** quando una piattaforma dimostra contaminazione fra rendering successivi o non riesce a mantenere separati i risultati.

Non è il percorso manuale predefinito e non deve costringere normalmente l'utente ad aprire N chat per N immagini.

## UX prevista nella fase 3 dei libri visuali

La sequenza da rendere nella UI è:

`Piano → Prompt → Crea Prompt Pack → Manuale / Via API → Candidate → Vision/review → Approva → Porta nel libro`

Per la strada Manuale la UI deve offrire esplicitamente **Crea Prompt Pack ZIP** e chiarire che quel file va consegnato all'AI. Il copia-prompt può esistere come utility/fallback, ma non sostituisce il Prompt Pack.

Per Coloring, Raccolta immagini e Libro illustrato la stessa infrastruttura viene riutilizzata con profili e gate specifici della famiglia.
