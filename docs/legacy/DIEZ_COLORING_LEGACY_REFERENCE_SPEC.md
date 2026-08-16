# Diez Coloring legacy — specifica di riferimento funzionale

Status: **RIFERIMENTO DI PARITÀ PER IL PERCORSO IMMAGINI**

Ultima analisi: 2026-08-16.

## 1. Provenienza ed evidenza

Artefatto ricevuto e analizzato staticamente, senza esecuzione:

- nome ricevuto: `DiezPublishingStudio-Setup(3).exe`
- SHA-256: `a6434531e920afcb1794a50273f64716f7c29c69e743f2792d30ea501fc441e2`
- formato: PE32 Windows GUI Intel i386
- installer identificato staticamente: Inno Setup 6.7.0
- prodotto: Diez Publishing Studio

Poiché l'installer è un contenitore e non viene eseguito durante questa analisi, il comportamento viene **corroborato dal codice legacy Avalonia conservato nello stesso repository**, in particolare:

- `SingleWindowBookFlowUi.cs`
- `ColoringAiCreationUi.cs`
- `SingleWindowVisualEssentialsUi.cs`
- `SingleWindowColoringProfileUi.cs`
- `SingleWindowColoringStylePolicyUi.cs`
- `SingleWindowSubjectStyleUi.cs`
- `SingleWindowImageSpecsUi.cs`
- `SingleWindowImageCollectionProfileUi.cs`
- `SingleWindowVisionValidationUi.cs`

Livelli di evidenza:

- **OSSERVATO-INSTALLER**: identità/packaging ricavati dal setup.
- **CORROBORATO-SORGENTE**: comportamento presente nella linea legacy Avalonia.
- **DIRETTIVA-PRODOTTO**: decisione esplicita dell'utente per la nuova Uno.
- **INVARIANTE-ATTUALE**: regola Core corrente che prevale su dettagli implementativi storici.

L'eseguibile non viene committato. Questa specifica è la memoria persistente della parità richiesta.

## 2. Direttiva principale

**DIRETTIVA-PRODOTTO.** Per Coloring, la nuova Diez deve riprendere tutto ciò che faceva questa linea precedente, aggiungendo ciò che oggi esiste in più — in particolare la **zona Scene**. La filosofia deve poi essere riutilizzata, adattata al caso, per Raccolta immagini e Libro illustrato.

Non significa copiare il vecchio layout o il vecchio codice. Significa non perdere capacità operative mentre si passa a Uno/Core.

## 3. Percorso Coloring: quattro fasi sequenziali

**CORROBORATO-SORGENTE + DIRETTIVA-PRODOTTO.** Le quattro fasi sono un percorso sequenziale nel contenuto principale, non quattro voci del menu laterale.

Sequenza di riferimento:

1. **1/4 — Quantità / definizione immagini**
2. **2/4 — Istruzioni / Prompt**
3. **3/4 — Prompt Pack / produzione AI**
4. **4/4 — Revisione / Vision / approvazione**

Regola UX:

- si procede `1 → 2 → 3 → 4`;
- `Indietro` può tornare alla fase precedente senza distruggere il lavoro;
- non si presenta all'utente un menu laterale con le quattro fasi come destinazioni parallele;
- una fase successiva può essere raggiunta solo quando i prerequisiti della precedente sono coerenti;
- scene e altri sotto-editor appartengono al percorso e non devono diventare una quinta macrofase.

## 4. Fase 1 — quantità, contenuto, stile e specifiche

### 4.1 Dati essenziali sempre disponibili

**CORROBORATO-SORGENTE.** Per Coloring, Raccolta immagini e Libro illustrato il vecchio percorso consolidava tre dati essenziali:

- numero esatto di immagini, da 1 a 500;
- soggetto/personaggio/i;
- ambiente/scenario.

I campi soggetto e ambiente possono contenere eccezioni locali del tipo `Immagine N: ...`; l'eccezione vale solo per quella posizione.

### 4.2 Consistent

**CORROBORATO-SORGENTE.** Sono presenti:

- attivazione/disattivazione `Consistent`;
- regole di consistenza editabili;
- contesto di consistenza persistito per l'intera serie.

Con più soggetti/personaggi, l'identità del soggetto è stabile e può avere criteri di consistenza separati. I criteri osservati includono:

- outfit/accessori;
- espressione;
- posa/azione;
- inquadratura/punto di vista;
- scene con altri soggetti/personaggi.

L'identità/aspetto fisico è trattato come stabile/HARD quando applicabile.

### 4.3 Multi-soggetto/personaggio

**CORROBORATO-SORGENTE.** Il percorso legacy supporta:

- toggle multi-soggetto;
- numero soggetti;
- selezione soggetto attivo;
- nome editabile;
- aggiunta/rimozione;
- descrizione specifica per soggetto;
- ID stabile indipendente dal nome visibile;
- regole Consistent per soggetto.

**INVARIANTE-ATTUALE.** Nella nuova architettura l'identità resta `SubjectId`; nelle scene la partecipazione è `SubjectId + SceneId`. Nomi, numeri e descrizioni sono modificabili senza cambiare identità.

### 4.4 Zona Scene — aggiunta rispetto al riferimento allegato

**DIRETTIVA-PRODOTTO.** L'utente indica che rispetto all'attuale obiettivo del Coloring, alla versione allegata manca sostanzialmente la zona Scene.

La nuova Uno deve quindi mantenere le quattro fasi ma integrare:

- scene con `SceneId` stabile e non riciclabile;
- ambiente locale della scena;
- partecipanti della scena per SubjectId;
- ambiente locale prioritario rispetto all'ambiente generico;
- possibilità di scene attive/inattive senza riuso dell'ID.

La posizione grafica definitiva della zona Scene verrà raffinata durante la prova reale dell'interfaccia. Vincolo già deciso: **Scene non diventa una macrovoce laterale né una quinta fase Coloring**.

### 4.5 Profilo editoriale Coloring

**CORROBORATO-SORGENTE.** Campi/controlli da preservare come capacità:

- descrizione soggetto/i;
- descrizione ambiente/scenario;
- stile;
- pubblico;
- difficoltà;
- spessore linee;
- complessità;
- densità elementi;
- sfondo;
- spazio bianco;
- aree chiuse e facili da colorare;
- evita aree/dettagli minuscoli;
- contorni puliti e continui;
- niente testo/numeri nell'immagine;
- soggetto chiaramente separato dallo sfondo;
- note stile facoltative.

### 4.6 Regola binaria Coloring

**CORROBORATO-SORGENTE / HARD.** Coloring usa come vincolo fisso:

- nero puro `#000000`;
- bianco puro `#FFFFFF`;
- nessun grigio;
- nessun colore;
- nessuna ombra;
- nessuna sfumatura/gradiente;
- nessun valore intermedio.

Questa regola non deve contaminare Raccolta immagini o Libro illustrato se il loro profilo cromatico prevede colore o scala di grigi.

### 4.7 Stile, Bold & Easy e Cozy

**CORROBORATO-SORGENTE + INVARIANTE-ATTUALE.** Sono dimensioni distinte:

- Visual Style: scelta di stile;
- Bold & Easy: parametro HARD indipendente ON/OFF;
- Cozy: parametro HARD indipendente ON/OFF;
- line weight: indipendente dallo stile.

Vincolo osservato/consolidato:

- linee `Sottile/Fine` o `Molto sottile/Extra Fine` forzano Bold & Easy OFF;
- Cozy rimane indipendente dallo spessore;
- un renderer non può reinterpretare automaticamente Bold & Easy o Cozy se il relativo HARD è OFF.

Stili storicamente presenti come preset/riferimento includono Bold & Easy, Line Art pulita, Line Art dettagliata, Kawaii/Cartoon, Mandala/Pattern; la nuova terminologia standard può usare il catalogo Core corrente senza traduzioni artificiali.

### 4.8 Specifiche tecniche immagine/stampa

**CORROBORATO-SORGENTE.** Funzionalità da preservare:

- trim/formato pagina KDP;
- larghezza/altezza e unità;
- aspect ratio immagine separato dal trim;
- orientamento derivato dal ratio, non duplicato come impostazione autonoma;
- classe risoluzione;
- larghezza/altezza pixel;
- DPI;
- qualità rendering;
- dettaglio tecnico.

Preset/riferimenti legacy comprendono formati KDP comuni (5×8, 5.5×8.5, 6×9, 7×10, 8×10, quadrati, Letter 8.5×11, A4 e custom) e numerosi aspect ratio editoriali/fotografici/display.

Classi risoluzione osservate:

- HD — lato lungo 1280 px;
- Full HD — 1920 px;
- 2K — 2560 px;
- 4K UHD — 3840 px;
- 8K UHD — 7680 px;
- Stampa — pagina × DPI mantenendo il ratio;
- Personalizzata.

Il sistema valuta la coerenza fra trim e aspect ratio, ma non deforma mai l'immagine. Crop/posizionamento, bleed e margini di sicurezza sono responsabilità dell'impaginazione.

## 5. Fase 2 — DEVE FARE, NON DEVE FARE e Prompt editabile

**CORROBORATO-SORGENTE.** La fase contiene:

- `DEVE FARE`;
- `NON DEVE FARE`;
- `PROMPT` generato;
- `Prepara prompt`;
- `Copia prompt`;
- passaggio alla fase Prompt Pack.

Tutti i box sono normali campi editabili:

- selezione/copia;
- modifiche manuali;
- undo / Ctrl+Z;
- il prompt copiato o usato per la serie include le modifiche manuali dell'utente.

Il prompt incorpora le regole comuni del progetto, contenuto richiesto, esclusioni, Consistent e profili tecnici/editoriali.

**INVARIANTE-ATTUALE IMPORTANTE.** La nuova implementazione deve conservare il comportamento utente ma **non** copiare letteralmente eventuali vecchi suffissi tecnici come `ELEMENTO DIEZ`, ID, routing o session metadata nel prompt visuale provider-facing. Prompt Compiler 3.6 deve sintetizzare ART DIRECTION + HARD locks e tenere metadata/routing fuori dal visual prompt.

## 6. Serie di immagini e identità stabile

**CORROBORATO-SORGENTE.** Quando si crea la serie:

- il numero richiesto è preciso;
- vengono creati i job mancanti;
- gli elementi hanno identità stabile;
- il sistema non elimina automaticamente immagini già esistenti solo perché l'utente abbassa il numero richiesto;
- ogni posizione riceve un risultato distinto pur condividendo le regole della serie.

La nuova implementazione può usare WorkUnit/placement/SceneId correnti, ma deve preservare questa sicurezza: nessuna cancellazione implicita distruttiva.

## 7. Fase 3 — Prompt Pack, modalità AI e paradigmi

**CORROBORATO-SORGENTE.** Capacità osservate:

- Work Unit immagine ordinate;
- scelta modalità di scambio AI;
- applicazione modalità alla serie;
- immagini paradigma/reference;
- ruoli del paradigma (es. character, style, palette/reference);
- import immagini con picker di sistema;
- formati immagine almeno PNG/JPG/JPEG/GIF/BMP/WEBP;
- deduplica materiale per SHA;
- associazione paradigma alla collezione/work unit;
- creazione Prompt Pack ZIP;
- import di uno o più ZIP restituiti dall'AI;
- passaggio alla revisione nella stessa finestra.

## 8. Anteprima immagine — requisito di parità esplicito

**DIRETTIVA-PRODOTTO + CORROBORATO-SORGENTE.** Questa è una capacità da non perdere.

La UI legacy ha un'area `Anteprima` persistente nello stesso MainWindow. Quando viene selezionata un'immagine, il file reale viene visualizzato senza aprire una finestra separata.

L'anteprima deve funzionare almeno per:

1. **materiale aggiunto/importato** nel progetto;
2. **immagine paradigma/reference** aggiunta alla produzione;
3. **immagine Candidate ricevuta dall'AI**, prodotta a partire dal Prompt generato da Diez;
4. versione approvata/attiva quando si naviga il contenuto visuale.

Comportamento legacy corroborato:

- recupero materiale tramite MaterialId;
- lettura dei byte embedded dal package/progetto;
- rendering bitmap con proporzioni preservate (`Uniform`);
- didascalia/contesto sotto l'immagine;
- fallback testuale se la bitmap non è decodificabile;
- selezionando una Candidate nella fase 4 l'anteprima viene aggiornata immediatamente;
- aggiungendo un paradigma la sua immagine viene mostrata immediatamente.

**Contratto nuovo:** la preview deve essere un componente riusabile Core/Uno-facing e non dipendere dalla provenienza del file. Materiale importato e risultato AI sono entrambi asset del progetto; cambiano origine e stato, non il modo in cui l'utente li può vedere.

## 9. Fase 4 — revisione, descrizione, Vision, approvazione

**CORROBORATO-SORGENTE.** La fase di revisione contiene:

- elenco risultati immagine;
- stato per elemento/versione;
- selezione → anteprima reale;
- descrizione associata editabile;
- salvataggio descrizione;
- candidate separate dalle versioni approvate;
- approvazione esplicita.

Il percorso Vision legacy aggiunge:

- controllo tecnico deterministico;
- Vision via API quando disponibile;
- Prompt Pack/ZIP Vision come percorso sempre disponibile;
- import esito Vision;
- dettaglio di ciò che Vision osserva;
- PASS / REVIEW / FAIL;
- approvazione visibile che passa dai gate, non dal vecchio pulsante diretto.

**INVARIANTE-ATTUALE.** Nel Core corrente i gate HARD richiesti includono subject match, single composition e, secondo profilo, stile, Bold & Easy, Cozy, line weight e partecipanti scena. Ogni FAIL HARD blocca l'approvazione. Non abbassare questa protezione per replicare il legacy.

## 10. Cosa aggiunge l'attuale Diez rispetto al riferimento

La parità col vecchio Diez non è il limite superiore. Il percorso nuovo deve includere anche:

- SceneId stabile;
- partecipazione SubjectId+SceneId;
- prompt locale di scena prioritario;
- Vision HARD corrente;
- AI Exchange tipizzato;
- separazione `Approva` / `Porta nel libro`;
- placement editoriale canonico;
- controlli asset/duplicati;
- Edition Freeze / Publication Candidate / export finale;
- package `.diez` migration-safe.

## 11. Regola di regressione

Durante il rifacimento Uno, una build verde non basta a dichiarare parità. Prima della promozione del nuovo installer, la prova reale deve verificare almeno:

- progressione 1→2→3→4;
- indietro senza perdita dati;
- campi bianchi editabili e undo;
- Prompt preparato/copiato correttamente;
- material import;
- anteprima immediata del materiale;
- anteprima immediata del risultato AI;
- Prompt Pack export/import;
- Vision che blocca FAIL HARD;
- approvazione e `Porta nel libro` separati;
- riapertura del `.diez` con asset ancora disponibili;
- export/finalizzazione senza asset mancanti/duplicati.

Questa specifica è il baseline di parità per il prossimo lavoro sui libri con immagini.