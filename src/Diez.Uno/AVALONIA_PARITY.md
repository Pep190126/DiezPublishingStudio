# Diez Publishing Studio — Parità Avalonia → Uno

Questo documento è la checklist di migrazione funzionale. Il repository e `PROJECT_STATE.md` restano la fonte di verità tecnica; gli indizi storici emersi durante la ricostruzione vengono registrati qui quando il loro significato è stato confermato.

## Identità del prodotto

- Nome originario del framework: **Gold**.
- Nome attuale: **Diez Publishing Studio**.
- Firma visiva: **Diez ∞ Publishing Studio**.
- Colore principale dell'interfaccia: **Azzurro Napoli `#007FFF`**.
- Gli altri elementi grafici usano sfumature di blu coerenti con il colore principale.
- I campi editabili e i selettori restano bianchi per distinguere chiaramente ciò che l'utente può modificare.
- Tutto ciò che l'utente legge — titoli, etichette, pulsanti, menu a discesa, placeholder, messaggi e descrizioni — deve essere comprensibile e user friendly.
- Filosofia terminologica: **italiano per spiegare, terminologia standard per nominare**. Termini comunemente riconosciuti come Prompt, Prompt Pack, Cozy, Bold & Easy, Consistent e i nomi standard degli stili non vanno tradotti artificialmente.
- I valori interni persistiti possono restare canonici/tecnici quando servono alla compatibilità; la UI deve presentarli senza alterare il significato dei dati.

## Parità multipiattaforma

Diez Desktop deve restare una singola applicazione di framework verificata su **Windows, macOS e Linux**.

La CI della migrazione Uno deve quindi trattare come gate obbligatori:

- Windows: build, pianist harness, build Uno e publish self-contained `win-x64`;
- Linux/Ubuntu: build, pianist harness e build Uno desktop;
- macOS: build, pianist harness, build Uno desktop e publish di bundle `.app` per `osx-arm64` e `osx-x64`.

Una funzionalità desktop non è considerata cross-platform solo perché compila su Windows. I servizi comuni, il routing e gli invarianti del framework devono attraversare gli stessi pianist harness sui tre sistemi.

## Principio del framework multi-libro

Diez non è un'app per una singola tipologia di libro. È un framework editoriale capace di instradare famiglie diverse sullo stesso nucleo progetto/materiali/AI/consistency/pubblicazione. Nessuna funzione può essere considerata migrata se funziona solo per Coloring o puzzle.

Tipologie canoniche attualmente consolidate:

- Coloring book
- Raccolta immagini
- Libro illustrato
- Saggio / manuale
- Word Search
- Cruciverba
- Quiz / trivia
- Romanzo / racconto
- Catalogo / raccolta dati
- Altro

Famiglie funzionali:

1. **Visuali / illustrate** — Coloring, raccolta immagini, libro illustrato.
2. **Long-form** — romanzo/racconto, saggio/manuale.
3. **Puzzle / attività** — Word Search, Cruciverba, Quiz/trivia.
4. **Strutturate / reference** — Catalogo/raccolta dati.
5. **Estensibili** — Altro e tipologie future.

Progetto, materiali, Editable Master, Content Graph/Bible, Consistency, AI, edition/export e finalizzazione sono servizi del framework e non proprietà di una singola famiglia.

## Marker storici confermati

### Gold → Diez

**Gold** era il nome originariamente dato al framework; in seguito è stato rinominato **Diez Publishing Studio**. Questo chiarisce che la visione multi-book precede l'attuale UI e non va ricostruita come somma accidentale di feature recenti.

### Test del pianista

Il **test del pianista** significa "premere tutti i tasti": verificare il programma come se un utente, anche sotto stress, provasse sequenze inattese, rapide, ripetute o contraddittorie. Non è quindi un semplice happy-path test. I pianist harness devono cercare attivamente:

- cambi rapidi di tipo libro;
- azioni ripetute/idempotenza;
- salvataggi ripetuti o concorrenti;
- ID obsoleti o elementi archiviati;
- passaggi avanti/indietro tra workflow;
- contaminazione di dati tra famiglie;
- input incompleti, duplicati o valori limite;
- invarianti che devono sopravvivere a reload e round-trip.

## Invarianti non negoziabili

### Scene strutturate

- `SceneId` stabile e non riciclabile.
- La partecipazione usa `SubjectId + SceneId`.
- Rinominare nomi/numeri non rompe la membership.
- Gli ID delle scene archiviate non vengono riutilizzati.

### Prompt Compiler 3.6

Il renderer visuale riceve `ART DIRECTION — SYNTHESIZED` e HARD locks. L'ambientazione della scena corrente prevale sul contesto generico. Routing, retry, session/request ID e metadata interni non devono finire nel prompt visuale.

### Vision

Stile, Bold & Easy, Cozy, line weight, composizione singola e `scene_participants_match` sono gate HARD quando applicabili. Un FAIL HARD blocca l'approvazione.

### PR #37

La PR #37 resta draft, aperta e non va mergiata finché non viene richiesto esplicitamente.

## Superficie Uno già esposta

La shell Uno mantiene un unico root visivo e contiene aree per:

- Home / progetto
- percorso tipo libro
- flusso visuale 1/4 → 4/4
- scene / soggetti
- Word Search
- Cruciverba
- Quiz / trivia
- Catalogo / raccolta dati
- Altro / tipologie future
- raccolta immagini
- narrativa / manuale
- Editable Master
- Content Graph / Bible
- Consistency Review
- AI Production / Exchange
- export / finalizzazione
- libreria finalizzati

Il routing Uno usa ora il catalogo canonico del Core: Quiz, Catalogo e Altro non ricadono più nel workspace Narrativa; Romanzo e Saggio/manuale restano long-form distinti, mentre le famiglie visuali usano il percorso immagini.

La shell deve evitare gli hack di layout Avalonia (`RedrawWindow`, layout pump, reflection del compositor, root swapping/manual template recovery).

## Servizi già estratti in Diez.Core durante la migrazione

La migrazione sta trasformando i servizi condivisi in una libreria UI-neutral consumabile sia dall'Avalonia legacy sia da Uno. Sono già entrati nel Core, tra gli altri:

- progetto, materiali e persistenza `.diez` tipizzata;
- Editable Master, Content Graph e consistency;
- catalogo pubblico e profili dei tipi libro;
- opzioni AI/editoriali per tipo libro;
- soggetti stabili e scene strutturate;
- profili Coloring / Bold & Easy / Cozy;
- profilo raccolta immagini;
- Word Search e Cruciverba;
- contratto long-form;
- provider AI e AI Production;
- Prompt Engineering e nucleo deterministico Prompt Compiler 3.6;
- policy Vision HARD;
- metadata edizione, Edition Freeze, Publication Candidate;
- handoff CSV/XLSX/ZIP di dominio;
- contratti, stato, Work Unit, versioni, snapshot e Prompt Pack dell'AI Exchange.

## Pianist harness attivi

La CI di migrazione deve verificare, oltre alla build Core/Avalonia/Uno:

- stress generale del Core;
- long-form;
- scene strutturate;
- Cruciverba;
- Word Search;
- Prompt Compiler 3.6;
- Vision HARD;
- pubblicazione cross-family su tutte le dieci tipologie;
- routing multi-book e isolamento delle opzioni per tutte le dieci tipologie.

## Lavoro ancora aperto

- Il resto dell'AI Exchange provider/request/response ingest va separato dai file che contengono ancora integrazione legacy/UI.
- La libreria finalizzati va separata in archivio Core e adapter locali/Google/DOCX.
- DOCX, Google e production package vanno collegati al frontend Uno attraverso servizi condivisi.
- La UI Uno deve usare progressivamente i servizi Core anche per la persistenza delle opzioni, eliminando lo stato di transizione `UnoUiState` dove esiste già un'entità canonica.
- La parità macOS deve includere, oltre al build/publish CI, smoke test runtime mirati su apertura/salvataggio `.diez`, picker file e clipboard quando avremo un harness UI desktop automatizzabile sul runner macOS.
