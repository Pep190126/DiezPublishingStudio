# Diez — architettura del sistema Prompt multi-libro

Status: **SPECIFICA DI PRODOTTO / ARCHITETTURA — WORKING, NON CONSOLIDATA**

Data: 2026-08-18

Branch di lavoro: `spike/uno-platform-ui`

## 1. Decisione architetturale

Diez **non deve avere un Prompt Engine indipendente per ogni Tipo libro**.

Deve avere un solo sistema di compilazione, composto da:

1. **stato editoriale canonico** scelto dall'utente;
2. **profilo della famiglia libro**;
3. **moduli/capacità riusabili**;
4. **contesto dell'elemento corrente** (capitolo, scena, immagine, puzzle, domanda, record...);
5. **regole HARD / validatori specifici**;
6. **renderer del Prompt**;
7. **snapshot esatto usato per Prompt Pack / copia manuale / API**.

Il Tipo libro cambia il profilo e i moduli attivi, **non il motore**.

Obiettivo: poter cambiare l'interfaccia, aggiungere una scelta o rinominare un passo senza dover riscrivere la logica di trasporto e senza moltiplicare engine paralleli.

---

## 2. Principio fondamentale: prima il metodo editoriale, poi il Prompt

La UI non deve essere un formulario che raccoglie campi perché “servono al prompt”.

Deve accompagnare il publisher attraverso decisioni editoriali sensate per quella famiglia di libro. Le scelte vengono salvate come dati strutturati; il Prompt è una **compilazione** di tali decisioni.

Quindi:

`Percorso guidato → stato canonico → Prompt leggibile → Prompt Pack / copia → Response → revisione → applicazione al libro`

Il Prompt non è la fonte di verità del progetto. La fonte di verità sono le scelte e i contenuti persistiti nel `.diez`.

---

## 3. Modello delle risposte utente

Ogni scelta significativa deve poter esprimere, dove appropriato, più stati oltre al semplice valore.

### 3.1 Stato di una decisione

Per i campi che possono essere ignoti all'inizio, usare semanticamente uno dei seguenti stati:

- **Definito dall'utente** — valore esplicito;
- **Da proporre con AI** — Diez chiede all'AI di proporlo;
- **Da derivare dal progetto/materiali** — non inventare prima di leggere ciò che esiste;
- **Non applicabile** — la famiglia o il progetto non richiede quella decisione;
- **Da decidere più avanti** — il flusso può proseguire senza un valore prematuro.

Esempio Romanzo: numero capitoli o pagine non deve essere obbligatorio. Un publisher può conoscere il target parole ma non i capitoli, oppure possedere già una struttura completa, oppure voler chiedere una proposta all'AI.

### 3.2 Niente default che diventano requisiti per errore

Un valore visualizzato come esempio non deve diventare automaticamente una direttiva del Prompt.

Esempio da evitare:

- UI mostra `20 capitoli` come default;
- utente non lo modifica;
- Prompt impone arbitrariamente 20 capitoli.

Il sistema deve distinguere **placeholder**, **suggerimento**, **default applicato consapevolmente** e **valore realmente scelto**.

---

## 4. Struttura del profilo di famiglia

Ogni Tipo libro è descritto da un profilo dichiarativo, concettualmente simile a:

- `BookType`
- `Capabilities`
- `GuidedSteps`
- `DecisionDefinitions`
- `PromptSections`
- `UnitModel`
- `ResponseModel`
- `ReviewModules`
- `ExportProfiles`

Non è necessario che questa sia una singola classe C#; è il contratto architetturale.

### 4.1 Capabilities riusabili

Esempi:

- `LongFormText`
- `StructuredOutline`
- `Chapters`
- `Scenes`
- `CharactersSubjects`
- `BibleContinuity`
- `VisualAssets`
- `VisualConsistency`
- `ImageCount`
- `TextPerPosition`
- `SourceDatabase`
- `TaxonomyFilters`
- `PuzzleGeneration`
- `QuestionGeneration`
- `FactChecking`
- `DataSchema`
- `Provenance`
- `ExternalToolHandoff`

Una famiglia combina capacità esistenti e aggiunge solo ciò che è realmente specifico.

---

## 5. Componenti UI riusabili, non schermate clonate

Uno Platform deve offrire componenti editoriali riusabili:

- stepper / percorso guidato;
- scelta `Lo so / Proponilo / Derivalo`;
- editor struttura ad albero;
- editor elenco ordinabile;
- editor capitoli / sezioni;
- editor scene;
- editor soggetti/personaggi + Consistent;
- editor Prompt con anteprima compilata;
- preview immagine;
- editor testo AI con confronto versione;
- pannello Response/import;
- pannello controlli e problemi;
- approvazione/applicazione separati;
- pannello export/handoff.

Coloring, Raccolta immagini e Cataloghi visuali possono condividere gran parte della superficie visuale, ma il profilo stabilisce quali controlli sono visibili e quali regole vengono compilate.

---

## 6. Pipeline unica di compilazione Prompt

### 6.1 Livello A — contesto editoriale

Dati leggibili dall'utente:

- tipo libro;
- obiettivo;
- pubblico;
- lingua;
- materiali/fonti;
- struttura nota o da proporre;
- scelte specifiche della famiglia.

### 6.2 Livello B — profilo famiglia

Trasforma le scelte in istruzioni editoriali coerenti con il tipo libro.

Esempio:

- Romanzo → arco narrativo, POV, tempo verbale, continuità, scene;
- Coloring → soggetto, stile, colorabilità, line weight, HARD visuali;
- Word Search → dataset filtrato, quantità puzzle, regole di unicità, formato lista;
- Catalogo dati → schema, fonti, normalizzazione, provenienza.

### 6.3 Livello C — contesto unità

La stessa configurazione genera Prompt diversi per unità quando necessario.

Esempi:

- una tavola Coloring;
- una scena illustrata;
- un capitolo;
- una scena narrativa;
- un blocco di domande;
- un puzzle Word Search;
- un lotto di record.

### 6.4 Livello D — output contract

Definisce esattamente cosa deve tornare:

- testo continuo;
- JSON/manifest strutturato;
- immagini;
- lista parole;
- domande/risposte;
- record tabellari;
- più file coordinati.

### 6.5 Livello E — HARD locks / QA

I vincoli non negoziabili restano separati dalle preferenze.

Esempi:

- Coloring: nero/bianco puro, no testo, colorabilità;
- Quiz: una sola risposta corretta quando richiesto;
- Data: schema obbligatorio e provenienza;
- Word Search: unicità whole-book quando attiva;
- Romanzo: nomi/relazioni canoniche quando marcate Consistent.

---

## 7. Prompt Preview: deve essere lo stesso Prompt che parte davvero

Regola obbligatoria:

> Il testo mostrato nella pagina Prompt deve provenire dallo stesso compilatore e dallo stesso snapshot che verrà usato per creare il Prompt Pack o per la copia manuale.

Non devono esistere:

- anteprima costruita da una funzione;
- ZIP costruito da una seconda funzione con regole diverse.

Quando l'utente crea il Prompt Pack:

1. Diez salva/valida le scelte correnti;
2. ricompila lo snapshot;
3. mostra/identifica la versione corrente;
4. usa esattamente quello snapshot per le Work Unit.

Questo principio generalizza il comportamento già reso necessario nel percorso Coloring.

---

## 8. Editing manuale del Prompt

Il Prompt deve restare modificabile, ma l'editing manuale non deve distruggere il modello strutturato.

Prevedere due livelli:

### 8.1 Prompt compilato

Rigenerabile sempre dalle scelte canoniche.

### 8.2 Override editoriale

Testo aggiunto/modificato consapevolmente dall'utente.

Azioni minime:

- `Rigenera dalle scelte`;
- `Mantieni le mie modifiche`;
- `Ripristina compilato`;
- `Confronta modifiche`;
- `Copia Prompt`;
- `Crea Prompt Pack`.

Se cambia una scelta a monte, Diez deve segnalare che il Prompt manualmente modificato è **stale** e offrire una ricomposizione, non sovrascriverlo silenziosamente.

---

## 9. Prompt Pack e Prompt da incollare sono due trasporti dello stesso lavoro

Il publisher deve poter scegliere:

- copia/incolla manuale;
- Prompt Pack ZIP;
- API/provider integrato in futuro.

La logica editoriale non cambia.

Il trasporto aggiunge soltanto:

- identità Job/Work Unit;
- manifest;
- asset/reference;
- formato Response atteso;
- eventuale batching.

Routing, hash, ID e metadata tecnici non devono contaminare il testo provider-facing salvo quanto necessario al protocollo esterno.

---

## 10. Response comune, revisori specializzati

Il concetto di Response è comune, ma il contenuto varia.

### Visuale

- Candidate immagini;
- Vision;
- approvazione;
- Porta nel libro.

### Testo long-form

- Candidate testo per capitolo/scena/sezione;
- editor differenze;
- accetta / scarta / combina;
- controlli continuità;
- applica al Master.

### Puzzle / quiz

- elementi generati strutturati;
- controllo duplicati, lunghezze, correttezza;
- sostituzione chirurgica;
- applicazione alla raccolta canonica.

### Dati

- record Candidate;
- schema validation;
- deduplica/normalizzazione;
- provenance review;
- applicazione dataset.

Quindi **una pipeline di scambio**, con revisori e applicatori per capacità.

---

## 11. Regola per quantità, pagine e struttura

Il Tipo libro decide quali quantità hanno senso.

### Visual books

`ImageCount` può essere una decisione primaria.

### Romanzo / racconto

Non mostrare `Numero immagini` se non è stato attivato un piano illustrazioni.

Pagine, parole, parti, capitoli e scene devono essere indipendenti e opzionali. Il publisher può:

- fissarne alcuni;
- lasciare gli altri liberi;
- chiedere una proposta;
- importarli da un outline esistente.

### Saggio / manuale

Struttura e lunghezza possono derivare da materiali, obiettivo didattico e profondità; anche qui non imporre numeri prematuri.

### Puzzle / quiz

Quantità di puzzle/domande è invece normalmente una decisione primaria e deve essere esplicita.

---

## 12. Evoluzione di `BookTypeAiOptionsCoreService`

L'attuale servizio è utile come primo catalogo, ma non deve diventare un mega-switch infinito.

Direzione prevista:

1. mantenere le chiavi canoniche compatibili;
2. introdurre definizioni per sezioni/passaggi;
3. aggiungere stato della decisione (`Defined / Propose / Derive / Later`);
4. spostare le famiglie verso profili dichiarativi separati;
5. mantenere un registry unico;
6. far consumare lo stesso registry a Uno e al Prompt Compiler.

Il risultato non deve essere “un engine per famiglia”, ma **un registry di profili per un engine comune**.

---

## 13. Strategia di implementazione

Ordine raccomandato:

1. congelare insieme all'utente i percorsi e le decisioni;
2. definire lo schema canonico delle decisioni;
3. costruire i componenti Uno riusabili;
4. migrare prima le famiglie con maggiore chiarezza UX;
5. collegare il compilatore unico;
6. soltanto dopo stabilizzare Prompt Pack/Response specifici per testo, puzzle e dati;
7. aggiungere QA/validatori di famiglia.

Evitare di rafforzare ora gli attuali form generici se dovranno essere sostituiti dal percorso guidato.

---

## 14. Criterio di successo

Un nuovo Tipo libro deve poter essere aggiunto principalmente dichiarando:

- quali passi mostra;
- quali decisioni raccoglie;
- quali capability usa;
- come tali decisioni alimentano le sezioni del Prompt;
- quale Response attende;
- quali controlli esegue.

Se aggiungere un Tipo libro richiede copiare un Prompt Engine completo, l'architettura è sbagliata.
