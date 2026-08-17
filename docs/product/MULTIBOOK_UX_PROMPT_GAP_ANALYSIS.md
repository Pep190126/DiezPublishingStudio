# Diez — gap analysis multi-libro: UX, Prompt e Response

Status: **AUDIT TECNICO / BACKLOG DI PRODOTTO — WORKING**

Data: 2026-08-18

Branch analizzato: `spike/uno-platform-ui`

Scopo: confrontare lo stato attuale della Uno Preview/Core con le specifiche multi-libro appena definite, evitando di implementare prematuramente le quattro pagine Coloring mentre l'utente le sta ancora valutando fisicamente.

---

## 1. Sintesi

La base tecnica attuale è migliore di una demo vuota:

- esiste un catalogo canonico dei Tipi libro;
- esiste un primo catalogo comune di opzioni AI;
- esistono pipeline reali per visuale/Prompt Pack/Response;
- Word Search e Cruciverba possiedono già alcuni modelli canonici;
- Uno ha superfici operative e persistenza `.diez`.

Il limite principale è però coerente con quanto osservato dall'utente:

> molte famiglie non visuali sono ancora rappresentate come **form di impostazioni + grandi caselle testo**, non come metodi editoriali guidati.

Inoltre il Prompt Core è asimmetrico:

- Coloring ha un profilo specializzato ricco;
- Raccolta immagini/Libro illustrato riusano un profilo visuale dedicato;
- le altre famiglie ricevono ancora un blocco editoriale generico e opzioni testuali.

Quindi la priorità corretta non è aggiungere altri prompt hard-coded: è fissare prima decisioni, capability e flow.

---

## 2. Catalogo Tipi libro — stato buono

`BookTypeCatalog` espone già:

- Word Search;
- Cruciverba;
- Quiz / trivia;
- Coloring book;
- Raccolta immagini;
- Romanzo / racconto;
- Saggio / manuale;
- Libro illustrato;
- Catalogo / raccolta dati;
- Altro.

Esistono anche classificazioni utili come `IsVisual` e `IsLongForm`.

### Gap

Il catalogo dice **che tipo è**, non ancora **quali capability monta**.

### Direzione

Introdurre in futuro un registry dichiarativo delle capability, senza sostituire le chiavi attuali.

Riferimento: `BOOK_FAMILY_CAPABILITY_REGISTRY_SPEC.md`.

---

## 3. `BookTypeAiOptionsCoreService` — buon prototipo, rischio mega-switch

Il servizio attuale ha un valore importante: raccoglie definizioni UI-neutral e mantiene una chiave stabile per molte opzioni.

Esempi già presenti:

- Word Search: puzzle count, parole/puzzle, lingua, no duplicates;
- Coloring: numero tavole, formato, orientamento, risoluzione;
- Romanzo: genere, target parole/pagine/capitoli, POV, tempo, tono;
- Saggio: target parole/pagine/capitoli, struttura, tono;
- Quiz: quantità, risposte, difficoltà, categorie;
- Catalogo dati: righe, colonne, deduplica, normalizzazione, provenance.

### Gap 1 — default semantici

Valori come `70000 parole`, `300 pagine`, `20 capitoli` nel Romanzo o `180 pagine`, `12 capitoli` nel Saggio possono diventare direttive anche quando l'utente non li ha realmente scelti.

### Gap 2 — stato decisione troppo povero

Esiste una prima distinzione `Known / FromProject` per alcune strutture, ma non il modello completo:

- Defined;
- Propose;
- Derive;
- Later;
- NotApplicable.

### Gap 3 — elenco piatto

Le definizioni non dichiarano ancora:

- passo del percorso;
- gruppo editoriale;
- dipendenze;
- visibilità;
- scope progetto/unità;
- Prompt section;
- validator;
- HARD/preference.

### Decisione

Non estendere il mega-switch campo per campo prima di congelare i flow.

---

## 4. `BookFamilyWorkspace` Uno — fallback utile, non UX finale

Il workspace generico renderizza automaticamente le definizioni Core come:

- TextBox;
- ComboBox;
- CheckBox;
- grande campo note;
- pulsanti `Salva`, `Prompt / AI`, `Testo principale`, `Esportazione`.

### Vantaggio

È un fallback coerente e permette di non perdere accesso ai dati mentre l'architettura evolve.

### Gap

Non crea una routine editoriale. Tutte le decisioni sono sullo stesso piano e la pagina non insegna il metodo della famiglia.

### Decisione

Mantenerlo come fallback temporaneo per famiglie non ancora migrate; non usarlo come modello finale.

---

## 5. Romanzo / Saggio — gap UX alto

Il workspace Uno attuale per long-form contiene principalmente:

- `Outline`;
- `Note editoriali`;
- `Piano illustrazioni`;
- `Salva workspace`.

### Mancanze rispetto al target

Romanzo:

- bussola narrativa;
- personaggi/relazioni;
- Bible;
- timeline;
- outline ad albero;
- scene dentro capitoli;
- POV/partecipanti per scena;
- stati di produzione;
- Prompt per progetto/capitolo/scena;
- Candidate text editor/versioning;
- continuity review.

Saggio/manuale:

- obiettivo didattico;
- fonti e policy;
- glossario;
- outline ad albero;
- piano contenuti per sezione;
- citazioni/provenienza;
- fact review.

### Priorità futura

Alta, ma soltanto dopo aver stabilizzato il modello comune Decision/Outline/Candidate.

---

## 6. Prompt profile Core — asimmetria da non ampliare

`BookTypePromptProfileService` costruisce un profilo realmente specializzato per Coloring.

Per Raccolta immagini e Libro illustrato delega a `ImageCollectionPromptProfileService`.

Per le altre famiglie il blocco è ancora sostanzialmente generico: Tipo libro + istruzione a mantenere struttura/tono/output coerenti.

### Conseguenza

Se si aggiungessero ora metodi `BuildNovelPrompt`, `BuildQuizPrompt`, `BuildEssayPrompt`, `BuildDataPrompt` direttamente a mano, si rischierebbe di creare esattamente la proliferazione di engine che si vuole evitare.

### Decisione

Prima:

1. flow;
2. decision schema;
3. capability registry;
4. unit model;
5. output contract.

Poi adattare il compiler unico.

---

## 7. Raccolta immagini — base Prompt più matura del workspace

`ImageCollectionPromptProfileService` possiede già concetti utili:

- uso editoriale;
- color mode;
- dettaglio;
- trattamento linee;
- rendering style;
- sfondo;
- viewpoint;
- leggibilità soggetto;
- no testo;
- chiarezza editoriale;
- coerenza scala/inquadratura.

### Gap

La UI Uno dedicata a Raccolta immagini oggi espone soprattutto:

- descrizioni;
- layout;
- modalità export;
- regole layout.

Questo non sfrutta ancora il profilo visuale disponibile nel Core né la pipeline visuale condivisa quanto potrebbe.

### Direzione

Quando il flow visuale sarà approvato, Raccolta immagini deve diventare una configurazione del `Visual Guided Workspace`, non una pagina laterale separata scollegata.

---

## 8. Libro illustrato — modello concettuale ancora da unificare

Il Core lo tratta come famiglia visuale per alcune opzioni/Prompt, ma il target richiede una struttura mista:

`nodo testuale ↔ scena ↔ illustrazione ↔ Candidate ↔ placement`.

### Gap

Manca ancora il legame operativo completo fra:

- outline/nodi;
- testo;
- piano illustrazioni;
- visual slots;
- Response visuale/testuale.

### Direzione

Non creare due app dentro la stessa famiglia. Riutilizzare `OutlineTreeEditor`, `UnitInspector`, componenti visuali e Candidate lifecycle comuni.

---

## 9. Word Search — base canonica presente, redesign operativo bloccato

Il workspace Uno attuale legge/scrive puzzle e lessico canonici e offre editing/import/export di base.

Questo è un buon fondamento dati.

### Gap UX

Non riflette ancora integralmente il metodo storico:

`DATABASE → FILTRI → GENERA → CONTROLLO → ESPORTA`.

### Gap funzionale preservato in specifica

Deve inoltre supportare:

- tassonomie dinamiche;
- scene/contesti semantici;
- variante anno/decade o altri assi;
- matrice scena × variante;
- unicità whole-book;
- sostituzione contestuale;
- export Titans.

### Gate

**Nessuna nuova implementazione Word Search** finché l'utente non autorizza esplicitamente il lavoro, come richiesto dalla ancestor spec.

Nel frattempo le capacità sono state fissate in specifica per non perderle.

---

## 10. Quiz — gap medio/alto

Esistono opzioni base Core, ma la UI generica non rappresenta ancora:

- QuestionBank;
- editor opzioni;
- risposta corretta;
- spiegazione;
- fonte;
- distribuzione difficoltà;
- review di ambiguità/distrattori/duplicati.

### Direzione

È un candidato naturale per `StructuredCandidateTable` + `ReviewIssuesPanel`.

Non richiede un editor long-form.

---

## 11. Catalogo / raccolta dati — gap medio/alto

Esistono opzioni base:

- target rows;
- required columns;
- deduplicate;
- normalize;
- keep provenance.

### Gap

Manca un vero `DataSchema editor` e un lifecycle dei record Candidate.

### Direzione

Riutilizzare:

- schema editor;
- tabella Candidate;
- validation;
- provenance;
- deduplica;
- export.

Queste capacità potranno essere riusate anche da lessici/puzzle e raccolte strutturate.

---

## 12. Cruciverba — componente dedicato già esistente, da riallineare al modello comune

Uno possiede un `CrosswordWorkspace` dedicato.

### Direzione futura

Conservare le peculiarità:

- parole;
- definizioni;
- ruoli tematici;
- Candidate definizione;
- compatibilità handoff Qxw.

Ma far convergere:

- Candidate lifecycle;
- Prompt workbench;
- issue review;
- provenance;
- export/handoff.

Non serve duplicare infrastruttura AI.

---

## 13. Sidebar Uno — debito UX noto

La shell attuale contiene ancora molte voci specifiche:

- Visual 1/4;
- Visual 2/4;
- Visual 3/4;
- Visual 4/4;
- Scene/Soggetti;
- Word Search;
- Cruciverba;
- Raccolta immagini;
- Narrativa/Manuale;
- Editable Master;
- Content Graph/Bible;
- Consistency Review;
- AI Production/Exchange;
- Export;
- Libreria finalizzati.

Questo è utile nello spike perché rende tutte le superfici raggiungibili, ma non rispetta ancora la gerarchia prodotto desiderata.

### Decisione

Non rifare ora la sidebar mentre l'utente sta valutando il flusso; il target resta una shell con macrovoci e percorsi famiglia nel workspace.

---

## 14. Lavoro autonomo sicuro adesso

Si può procedere senza interferire con le note Coloring su:

### A. Specifiche e contratti

- capability registry;
- component inventory Uno;
- unit/candidate model;
- response contracts per famiglie;
- acceptance tests;
- migration map dalle chiavi correnti.

### B. Audit

- individuare chiavi duplicate/incoerenti;
- distinguere dati canonici da `UnoUiState` transitorio;
- documentare quali componenti esistenti sono riusabili;
- individuare dipendenze Avalonia residue.

### C. Test di regressione non-UX

Solo dove non impone decisioni di prodotto:

- round-trip `.diez`;
- preservazione ID;
- Candidate provenance;
- Prompt snapshot identity;
- Response import identity;
- nessuna perdita sezioni sconosciute.

---

## 15. Lavoro da NON fare autonomamente adesso

Fino alle note dell'utente evitare:

- ridefinire il layout Coloring 1–4;
- cambiare ordine/semantica delle scelte Coloring;
- modificare i HARD Prompt Coloring salvo bug dimostrato;
- consolidare UX non fisicamente approvata;
- implementare il redesign Word Search;
- moltiplicare builder Prompt specifici per famiglia;
- migrare dati canonici con schema distruttivo.

---

## 16. Ordine proposto dopo le note Coloring

1. congelare il nuovo metodo Coloring;
2. verificare che i componenti comuni coprano il caso reale;
3. definire `DecisionState` e schema decisioni persistito;
4. costruire `GuidedWorkspaceShell` e `PromptWorkbench` comuni;
5. migrare Coloring come prima famiglia reale;
6. applicare lo stesso framework a Raccolta immagini;
7. poi Libro illustrato;
8. long-form;
9. Quiz/Catalogo;
10. Word Search soltanto dopo autorizzazione esplicita.

Questo ordine usa Coloring come banco di prova del framework senza trasformarlo in una struttura speciale irripetibile.
