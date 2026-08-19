# Diez Publishing Studio — Guida prova fisica Round 3

**Candidata:** Uno Platform Windows x64 — Round 3  
**Source SHA installer:** `4dea375d25d211c74bf90245562c0be48d43f1c6`  
**Workflow:** Uno Windows Consolidation Candidate — run `#17`, ID `32314403062`  
**Stato:** `TECHNICALLY_VERIFIED`; non consolidare le novità Round 3 fino alla prova fisica dell'app installata e conferma esplicita.

## Obiettivo della Round 3

Questa candidata mantiene le parti già risultate buone nella prova precedente — area Progetto/materiali, ripristino, Undo/Redo, sidebar collassabile e resize — e modifica soprattutto il percorso visuale e la comprensibilità delle decisioni sui materiali.

## Materiali — Uso dell'AI e Fedeltà

I codici tecnici restano interni al `.diez` e ai manifest ma non vengono mostrati come etichette principali.

### Uso dell'AI

- **Può usarlo e modificarlo** — l'AI può usare il materiale come input operativo e trasformarlo secondo ruolo e istruzioni.
- **Usalo solo come riferimento** — guida identità, stile, composizione, ambiente o contenuto senza diventare automaticamente un asset da copiare/inserire.
- **Usa il file direttamente** — il file è già un asset editoriale e non va rigenerato automaticamente.
- **Non inviare all'AI** — il materiale resta nel progetto ma viene escluso dai Prompt Pack.

### Fedeltà

- **Da rispettare esattamente** — contenuto/dati/termini sono vincolanti.
- **Molto fedele** — mantenere identità e caratteristiche molto vicine all'originale salvo modifiche esplicite.
- **Fedele ma guidata** — base riconoscibile con libertà controllata dalle istruzioni editoriali.
- **Solo ispirazione** — conta idea/stile/atmosfera, non la replica.
- **Non applicabile** — il ruolo non richiede un livello di fedeltà.

Sotto entrambi i selettori deve comparire una breve descrizione dinamica del comportamento selezionato.

## Produzione AI — quattro fasi

La sidebar deve mostrare **Produzione AI**.

Per Coloring Book, Raccolta immagini e Libro illustrato esistono soltanto quattro fasi:

**1/4 Definizione → 2/4 Prompt → 3/4 Produzione AI → 4/4 Revisione**

I cinque Tab superiori della Round 2 non devono più comparire. I quattro indicatori ovali già presenti in testa al main sono ora cliccabili e costituiscono la navigazione fra le fasi.

La selezione della fase è stato di sessione/workspace, non contenuto editoriale canonico. Un progetto non deve contaminare la fase di un altro progetto.

## 1/4 · Definizione

La Definizione è il luogo in cui il publisher partecipa alla costruzione del Prompt.

Ordine concettuale:

1. quantità/posizioni e parametri generali;
2. profilo del tipo libro;
3. scelta del modo di descrivere il contenuto;
4. Consistent e relative regole;
5. **DEVE FARE · HARD**;
6. **NON DEVE FARE · HARD**;
7. passaggio al Prompt.

### Due modi alternativi di descrivere il contenuto

**Soggetto + ambientazione generici** — per serie in cui è sufficiente una descrizione generale.

**Scene + soggetti strutturati** — per descrivere in modo meccanico soggetti/personaggi, scene e partecipazioni.

Le due modalità devono essere chiaramente alternative a video; non devono creare due fonti concorrenti ambigue.

### Consistent

Consistent deve combinarsi con la modalità scelta. Con Scene/Soggetti può mantenere identità e altri LOCK per soggetto, anche attraverso lotti diversi; pose/azioni/composizioni possono variare quando non bloccate.

### DEVE FARE / NON DEVE FARE

Sono gli **ultimi campi della Definizione** e sono testo libero dell'utente con semantica HARD.

- `DEVE FARE` → requisito obbligatorio;
- `NON DEVE FARE` → esclusione obbligatoria.

Non devono essere reinterpretati come semplici preferenze. Il Prompt atomico li traduce rispettivamente in `USER REQUIREMENT — HARD` e `USER EXCLUSION — HARD`.

## 2/4 · Prompt

La fase Prompt non deve chiedere nuovamente DEVE FARE/NON DEVE FARE. Mostra il Prompt compilato dalla Definizione, permette di copiarlo e di ricompilarlo dopo una modifica della Definizione.

Controllare in particolare che Scene/Soggetti, Consistent e i due HARD siano realmente riflessi nel Prompt visualizzato.

## 3/4 · Produzione AI

Contiene Prompt atomici, job/piano, accesso alla Produzione AI, Prompt Pack/AI Exchange e anteprime applicabili.

Il naming del trasporto resta:

`NomeProgetto_YYYYMMDD_vNNN_prompt-pack.zip`

con Response atteso:

`NomeProgetto_YYYYMMDD_vNNN_response.zip`

## 4/4 · Revisione

Controllare Candidate/Vision e mantenere separati:

**importata ≠ approvata ≠ portata nel libro**.

## Scene e riuso

Le Scene operative appartengono al progetto `.diez` corrente. Non vengono condivise automaticamente fra progetti.

Il riuso futuro deve essere esplicito, tramite una funzione tipo **Salva come modello scena**: nel nuovo progetto si copiano contenuto/regole ma vengono creati nuovi `SceneId` e, quando necessario, nuovi `SubjectId`.

## Checklist fisica Round 3

1. Aprire un progetto Coloring esistente e un progetto nuovo.
2. Verificare la voce **Produzione AI** nella sidebar.
3. Verificare che non esistano cinque Tab superiori.
4. Cliccare gli ovali `1 → 3 → 2 → 4 → 1` e verificare assenza di loop/focus bloccato.
5. In Definizione scegliere **Soggetto + ambientazione generici**.
6. Passare a **Scene + soggetti strutturati** e verificare l'editor meccanico.
7. Verificare Consistent con entrambe le modalità.
8. Verificare che **DEVE FARE · HARD** e **NON DEVE FARE · HARD** siano gli ultimi campi della Definizione.
9. Inserire istruzioni riconoscibili nei due campi e salvarle.
10. Aprire Prompt e verificare che i due HARD compaiano nel Prompt compilato senza essere richiesti nuovamente.
11. Passare a Produzione AI e verificare che Prompt/Prompt atomici restino coerenti.
12. In Progetto selezionare un materiale e verificare etichette italiane + descrizione sotto **Uso dell'AI** e **Fedeltà**.
13. Salvare/chiudere/riaprire lo stesso progetto.
14. Aprire subito un secondo progetto e verificare che fase/modalità/focus del precedente non lo contaminino.
15. Verificare che import Response già validato, Undo/Redo, cronologia e anteprima materiali non siano regrediti.

## Esito CI della candidata

Run `32314403062`:

- Visual book gate: success;
- Restore Windows runtime: success;
- Publish Uno Windows x64: success;
- verifica EXE: success;
- packaging Inno Setup: success;
- smoke install/launch/uninstall: success;
- artifact upload: success.

Setup prodotto: `DiezPublishingStudio-UnoPreview-Setup.exe`  
Dimensione: `94966739` byte  
SHA-256: `b9ab025849ca2d8ec701b77e15126b4337cc036b0ee18fa908affff6c00cf219`

La CI rende questa build **TECNICAMENTE VERIFICATA**. La prova fisica e la conferma dell'utente restano necessarie per promuovere le novità Round 3 a **CONSOLIDATO**.
