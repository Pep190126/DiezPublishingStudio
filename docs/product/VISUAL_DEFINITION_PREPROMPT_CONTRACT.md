# Diez Publishing Studio — Contratto pre-Prompt per i libri visuali

Status: **DIRETTIVA DI PRODOTTO UNO — NON CONSOLIDATA FINO A TEST FISICO**

Questa specifica chiarisce l'ordine e la semantica della fase **1/4 · Definizione** per Coloring Book, Raccolta immagini e Libro illustrato.

## 1. Principio editoriale

La costruzione del Prompt deve essere partecipata dall'utente prima che Diez compili il Prompt finale.

L'utente può descrivere le proprie desiderata in due modi complementari:

1. **meccanicamente / strutturato**, tramite controlli, soggetti, scene, partecipazioni e Consistent;
2. **testo libero HARD**, tramite i campi finali `DEVE FARE` e `NON DEVE FARE`.

Il Prompt Compiler resta unico e composizionale: non esiste un motore separato per ogni schermata o per ogni tipo libro.

## 2. Ordine obbligatorio della fase 1/4 · Definizione

La schermata Definizione deve essere ordinata così:

1. quantità / posizioni e parametri generali del tipo libro;
2. profilo specifico della famiglia (es. Coloring: stile, audience, difficoltà, Bold & Easy, Cozy, line weight, ecc.);
3. scelta della modalità contenuto:
   - **Soggetto + ambientazione generici**, oppure
   - **Scene + soggetti strutturati**;
4. Consistent e relative regole, combinati con la modalità contenuto scelta;
5. eventuali vincoli tecnici / materiali/reference applicabili prima del Prompt;
6. **DEVE FARE — testo libero HARD**;
7. **NON DEVE FARE — testo libero HARD**;
8. salvataggio Definizione e passaggio a **2/4 · Prompt**.

`DEVE FARE` e `NON DEVE FARE` devono quindi essere gli **ultimi campi editoriali della schermata Definizione**.

## 3. Soggetto/ambientazione generici vs Scene/Soggetti strutturati

Le due modalità sono alternative a livello di authoring principale.

### Modalità A — Soggetto + ambientazione generici

Per serie semplici o quando non serve controllare singole scene/personaggi.

L'utente definisce:

- soggetto/tema generale;
- ambientazione generale;
- Consistent opzionale;
- regole di consistenza della serie.

### Modalità B — Scene + soggetti strutturati

Per serie in cui l'utente vuole partecipare in modo più meccanico alla costruzione delle singole immagini.

L'utente può definire:

- uno o più soggetti/personaggi con identità stabile;
- descrizione dei soggetti;
- scene;
- descrizione/ambientazione locale della scena;
- partecipanti per scena;
- regole Consistent per soggetto e/o serie;
- libertà/lock su outfit, espressione, azione, framing e co-scena secondo il tipo libro.

La UI deve rendere evidente quale modalità è attiva; checkbox, radio/pulsanti segmentati o altro controllo equivalente sono accettabili purché non risultino ambigue due sorgenti concorrenti.

Quando Scene/Soggetti è attivo, la semantica strutturata prevale sui campi generici per le posizioni a cui si applica.

## 4. Consistent

`Consistent` non è una modalità separata e non è una quinta fase.

Deve essere combinabile con la modalità contenuto scelta:

- generico + Consistent;
- Scene/Soggetti + Consistent;
- Scene/Soggetti senza Consistent quando il tipo libro o il progetto lo consente.

Il significato concreto delle regole dipende dal tipo libro.

Esempi:

- Coloring: identità del personaggio, stile, line weight e HARD del libro; pose/composizioni possono variare se non locked;
- Raccolta immagini: stile di resa, scala, viewpoint, palette o soggetto possono essere locked/preferred/free;
- Libro illustrato: identità personaggi e mondo possono essere persistenti, mentre la scena cambia per posizione narrativa.

La consistenza di un personaggio deve poter sopravvivere a lotti di generazione differenti attraverso il profilo/identity anchor canonico del progetto, non dipendere dal batch.

## 5. DEVE FARE / NON DEVE FARE — HARD

I due campi sono testo libero dell'utente e vengono interpretati come vincoli **HARD**.

### DEVE FARE

Tutto ciò che è espresso chiaramente qui deve comparire o essere rispettato nell'output, salvo impossibilità tecnica esplicita.

### NON DEVE FARE

Tutto ciò che è espresso chiaramente qui deve essere escluso dall'output.

Gerarchia minima del compiler per una Work Unit visuale:

1. sicurezza e impossibilità tecniche;
2. vincoli HARD del tipo libro;
3. `NON DEVE FARE` dell'utente;
4. `DEVE FARE` dell'utente;
5. scena/soggetti/partecipazioni e identity locks della posizione;
6. Consistent LOCKED;
7. altre preferenze strutturate;
8. libertà creativa dell'AI.

Se un requisito HARD è impossibile o contraddittorio, il sistema deve segnalare il conflitto invece di reinterpretarlo silenziosamente come preferenza.

## 6. Fase 2/4 · Prompt

La fase Prompt non deve chiedere nuovamente `DEVE FARE` e `NON DEVE FARE` come input principali.

Deve invece mostrare il Prompt compilato a partire dalla Definizione canonica e consentire:

- anteprima;
- editing/override esplicito del Prompt compilato;
- copia;
- rigenerazione dalla Definizione;
- passaggio al Prompt Pack.

Il Prompt visualizzato e il Prompt Pack devono derivare dallo stesso snapshot compilato.

## 7. Navigazione Produzione AI

Per i libri visuali restano **esattamente quattro fasi**:

`1/4 Definizione → 2/4 Prompt → 3/4 Produzione AI → 4/4 Revisione`

Non deve esistere un quinto tab `Scene e soggetti`.

I quattro indicatori ovali già presenti in testa al main diventano il controllo di navigazione cliccabile fra le quattro fasi. Le Scene/Soggetti appartengono alla fase 1, prima del Prompt.

La macrovoce laterale deve chiamarsi **Produzione AI**.

## 8. Persistenza e riuso delle Scene

Decisione corrente:

- le **Scene operative** appartengono al progetto `.diez` corrente;
- non vengono condivise automaticamente fra progetti;
- il riuso futuro deve essere esplicito tramite un concetto tipo `Salva come modello scena` / libreria modelli;
- importando un modello in un nuovo progetto si copiano contenuti e regole ma si generano nuovi `SceneId` e, quando necessario, nuovi `SubjectId`.

Questo evita dipendenze globali accidentali fra libri diversi mantenendo possibile una libreria publisher riutilizzabile.

## 9. Acceptance test futuro

La prossima build visuale deve verificare almeno:

1. solo quattro indicatori ovali, cliccabili;
2. nessun quinto tab Scene/Soggetti;
3. `Produzione AI` nella sidebar;
4. fase 1: scelta chiara fra generico e Scene/Soggetti;
5. Consistent combinabile con la modalità scelta;
6. `DEVE FARE` e `NON DEVE FARE` ultimi campi della Definizione;
7. i due testi entrano nel Prompt come HARD;
8. fase 2 mostra il Prompt già compilato senza richiedere nuovamente gli stessi campi;
9. click diretto 1→3→2→4 non crea loop/focus lock;
10. salvataggio/riapertura non persiste anomalie di navigazione fra progetti.
