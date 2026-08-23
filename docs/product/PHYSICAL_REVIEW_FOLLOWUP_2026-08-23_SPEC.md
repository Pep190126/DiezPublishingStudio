# Diez Publishing Studio — Follow-up prova fisica 23 agosto 2026

**Stato:** DIRETTIVA DI PRODOTTO / DA IMPLEMENTARE E VALIDARE FISICAMENTE

Questo documento registra le decisioni emerse dalla revisione fisica successiva alla Round 3/3.1. Non marca come CONSOLIDATE le parti nuove: la consolidazione richiede sempre prova fisica dell'app installata e conferma esplicita dell'utente.

---

## 1. Brand sidebar — Diez / ∞ / Publishing Studio

La composizione attuale del marchio è corretta come concetto, ma va rifinita otticamente:

- mantenere il nome `Diez` così com'è;
- ridurre lo spazio verticale/interlinea tra `Diez` e il simbolo `∞`;
- rendere il simbolo `∞` della **stessa larghezza visiva di `Diez`**, mantenendo le proporzioni originali e senza deformazioni;
- ridurre lo spazio verticale anche tra `∞` e `Publishing Studio`;
- aumentare leggermente la dimensione di `Publishing Studio`;
- mantenere il blocco complessivamente centrato e leggibile nella sidebar.

La regola è ottica: non è richiesto che font-size o bounding box numerici siano identici; a video `Diez` e `∞` devono apparire della stessa larghezza.

### 1.1 Sidebar non scrollabile

La barra laterale principale **non deve essere scrollabile**.

Devono restare sempre visibili, senza scrollbar verticale:

- blocco brand;
- pulsante di espansione/contrazione;
- Progetto;
- Tipo libro;
- Produzione AI;
- Controlli e revisione;
- Esportazione;
- Libri finalizzati;
- eventuali controlli globali essenziali già approvati.

Se l'altezza della finestra è limitata, Diez deve ridurre in modo responsivo spaziatura, padding e dimensioni secondarie. Non deve trasformare la sidebar in un elenco scrollabile. Il main resta invece libero di utilizzare ScrollViewer dove il contenuto editoriale lo richiede.

---

## 2. Scelta dell'AI per testo e testo strutturato

Quando Diez richiede contenuto testuale all'AI, l'utente deve poter scegliere fra più provider/modelli adatti al compito, senza essere costretto a conoscere i model ID tecnici.

### 2.1 UI

Il controllo deve offrire almeno:

- `Automatico (consigliato)`;
- `OpenAI`;
- `Claude`;
- `Gemini`.

Quando utile, aggiungere un secondo selettore semplice:

- `Qualità massima`;
- `Bilanciato`;
- `Rapido / economico`.

Sotto la scelta mostrare una breve descrizione del comportamento atteso, nello stesso stile usato per i materiali AI.

Per output strutturati mostrare un badge `Output strutturato` e il nome/descrizione del contratto richiesto (es. JSON Schema, record, tabella canonica, piano capitoli, elenco quiz, ecc.).

### 2.2 Catalogo provider iniziale — fotografia 23 agosto 2026

Il catalogo deve essere **capability-based e aggiornabile**, quindi i nomi modello sotto sono default iniziali, non costanti hard-coded nella UX.

#### OpenAI

- GPT-5.6 Sol — qualità massima / compiti editoriali e di ragionamento complessi;
- GPT-5.6 Terra — profilo bilanciato;
- GPT-5.6 Luna — alto volume / costo ridotto.

I modelli GPT-5.6 supportano Structured Outputs e sono quindi candidati forti per testo strutturato con schema.

#### Anthropic Claude

- Claude Opus 5 — qualità massima per long-form, sviluppo editoriale, revisione e compiti testuali complessi;
- Claude Sonnet 5 — profilo bilanciato per produzione editoriale ordinaria.

Claude deve restare disponibile anche per testo strutturato, ma Diez deve sempre passare il risultato attraverso il proprio parser/validator canonico e non deve assumere che JSON apparentemente valido equivalga a contenuto semanticamente conforme.

#### Google Gemini

- Gemini 3.1 Pro — compiti complessi, ragionamento e sintesi con contesto ampio;
- Gemini 3.7 Flash — produzione ad alto volume, rapida e adatta anche a output strutturati.

Gemini supporta output strutturati basati su JSON Schema; Diez deve comunque validare semanticamente i valori prodotti.

### 2.3 Routing automatico suggerito

`Automatico` non significa un unico provider fisso. Diez sceglie in base al tipo di operazione.

Default iniziale:

- **scrittura creativa / long-form / voce editoriale:** Claude Opus 5 (qualità) o Claude Sonnet 5 (bilanciato);
- **ragionamento editoriale complesso, manuali, sintesi vincolate, trasformazioni con molti HARD:** GPT-5.6 Sol / Terra;
- **testo strutturato con schema rigoroso:** GPT-5.6 Terra/Sol oppure Gemini 3.7 Flash; Gemini 3.1 Pro quando la struttura richiede ragionamento più complesso;
- **alto volume, classificazioni, estrazioni e trasformazioni ripetitive:** GPT-5.6 Luna o Gemini 3.7 Flash.

Questi default devono essere modificabili nel catalogo provider senza cambiare il Prompt Compiler.

### 2.4 Un solo Prompt Compiler, adapter per provider

Non creare un prompt engine indipendente per OpenAI, Claude o Gemini.

Architettura obbligatoria:

`decisioni canoniche → prompt compiler unico → snapshot semantico → adapter provider → richiesta → validator → Candidate/Response`.

L'adapter provider può modificare solo aspetti di trasporto e forma:

- system/user packaging;
- sintassi di output;
- JSON Schema/structured output quando supportato;
- tool/function schema;
- token budget e parametri ammessi;
- istruzioni di formato più efficaci per il provider.

Non può cambiare o perdere:

- HARD dell'utente;
- decisioni canoniche;
- vincoli editoriali;
- quantità;
- identità di scene/soggetti;
- criteri di validazione.

Prompt preview, Prompt Pack e Response lineage devono registrare provider + profilo/modello effettivamente utilizzato.

### 2.5 Provider-specific prompting

Lo stesso snapshot deve generare una variante provider-aware **semanticamente equivalente**.

- OpenAI: usare Structured Outputs/JSON Schema per output strutturati quando applicabile.
- Gemini: usare structured output/response schema quando applicabile.
- Claude: usare istruzioni e packaging ottimizzati per Claude; per strutturato il risultato passa sempre da parser + schema validator + semantic validator Diez.

In nessun caso un provider può ottenere requisiti diversi da un altro per lo stesso snapshot.

---

## 3. Consistent — granularità per soggetto e scena

`Consistent` non deve essere un semplice checkbox globale.

Deve diventare una configurazione granulare composta da:

1. consistenza dei soggetti;
2. consistenza delle scene;
3. relazione immagini ↔ scene;
4. eventuali Identity Anchor/reference;
5. hard lock per singola Work Unit.

### 3.1 Consistenza dei soggetti

L'utente deve poter applicare Consistent a:

- un solo soggetto;
- più soggetti selezionati;
- tutti i soggetti.

Ogni soggetto deve poter essere marcato, in modo comprensibile, per esempio:

- `Mantieni coerente`;
- `Può variare`.

Per un soggetto coerente Diez deve mantenere gli attributi canonici pertinenti al tipo libro, tra cui quando applicabili:

- identità/aspetto;
- proporzioni;
- elementi distintivi;
- abbigliamento/accessori;
- palette;
- stile/resa;
- caratteristiche definite dall'utente come HARD.

La consistenza può usare Identity Anchor/reference quando disponibile, ma non deve propagarsi automaticamente ai soggetti che l'utente ha lasciato variabili.

### 3.2 Consistenza delle scene

Ogni scena deve poter essere configurata indipendentemente:

- `Mantieni la stessa scena`;
- `La scena può variare`.

Se una scena deve essere sempre la stessa, tutte le immagini associate devono ricevere un **scene consistency lock** derivato dalla definizione canonica della scena.

Il lock può includere, secondo il tipo libro e le scelte utente:

- luogo/ambiente;
- architettura;
- elementi fissi;
- disposizione spaziale di base;
- tempo/periodo;
- palette;
- punti di riferimento;
- altri attributi marcati HARD.

Consistenza soggetto e consistenza scena sono indipendenti e combinabili:

- soggetto coerente + scena variabile;
- soggetto variabile + scena coerente;
- entrambi coerenti;
- entrambi variabili.

### 3.3 Relazione HARD immagini ↔ scene

Invariante obbligatoria:

`Numero immagini >= Numero scene`

Diez non deve permettere la compilazione/produzione se il numero di immagini richieste è inferiore al numero di scene definite.

Esempi validi:

- 3 scene / 3 immagini;
- 3 scene / 6 immagini;
- 2 scene / 10 immagini;
- 1 scena / N immagini.

Quando `Numero immagini > Numero scene`, più immagini possono derivare dalla stessa scena.

### 3.4 Assegnazione immagini alle scene

Offrire due modalità:

1. `Distribuisci automaticamente` — Diez assegna le immagini alle scene in modo bilanciato e mostra la mappa risultante;
2. `Assegna manualmente` — l'utente imposta quante immagini appartengono a ciascuna scena.

Vincoli HARD:

- ogni scena definita deve avere almeno una immagine associata;
- la somma delle immagini assegnate alle scene deve coincidere col numero immagini del progetto;
- Diez non deve inventare scene aggiuntive implicitamente;
- se una scena è `Mantieni la stessa scena`, tutte le Work Unit collegate ereditano il scene lock;
- se uno o più soggetti sono `Mantieni coerente`, solo quei soggetti ricevono subject/identity lock;
- prima della compilazione del prompt atomico ogni Work Unit deve avere scene e soggetti risolti, non descrizioni aggregate ambigue.

### 3.5 Impatto sui Prompt Pack

Ogni Work Unit visuale deve dichiarare e congelare almeno:

- `SceneId` effettiva;
- posizione/indice nell'insieme delle immagini della scena;
- soggetti partecipanti;
- soggetti consistency-locked;
- scena consistency-locked sì/no;
- Identity Anchor/reference applicabili;
- variazioni consentite;
- HARD specifici della Work Unit.

La compilazione non deve produrre prompt del tipo generico `3 soggetti di Halloween` per tre immagini diverse. Se sono richiesti tre soggetti distinti, Diez deve materializzare prima i tre soggetti/scene canonici e assegnarli alle Work Unit.

---

## 4. Collegamento con la prova Vision corrente

La revisione delle tre Candidate Halloween ha mostrato che la descrizione visiva non può essere usata come sinonimo di conformità editoriale.

Vision deve distinguere:

- **descrizione di ciò che vede**;
- **verifica requisito-per-requisito rispetto agli HARD**;
- **giudizio di qualità editoriale/pubblicabilità**.

Scene/soggetti atomici e Consistent granulare devono alimentare direttamente questi gate, in modo che Vision possa verificare la conformità della singola Work Unit e non soltanto produrre una descrizione plausibile.

---

## 5. Gate di implementazione

Queste decisioni sono specificate ma non CONSOLIDATE.

Per consolidarle occorrono almeno:

- prova fisica del brand e sidebar non scrollabile a più altezze finestra;
- prova fisica selezione provider testo e testo strutturato;
- verifica che lo stesso snapshot mantenga identici HARD passando fra provider diversi;
- prova 1 soggetto coerente su più soggetti totali;
- prova più soggetti coerenti selettivamente;
- prova stessa scena su più immagini;
- prova `immagini > scene` con distribuzione automatica e manuale;
- blocco esplicito di `immagini < scene`;
- verifica Prompt Pack atomico scene/soggetti;
- verifica Vision contro HARD di scena/soggetto/qualità editoriale.
