# Diez — matrice delle decisioni utente per Tipo libro

Status: **WORKING SPEC — input per UX e Prompt Compiler, NON CONSOLIDATA**

Data: 2026-08-18

Scopo: definire **quali decisioni deve poter prendere il publisher** prima di stabilizzare il Prompt Compiler multi-libro.

Regola: una decisione entra nel Prompt soltanto se è applicabile e se il suo stato è `Defined`, `Propose` o `Derive`. I placeholder UI non sono dati editoriali.

---

## 1. Campi trasversali

Da rendere disponibili solo quando hanno senso:

| Decisione | Stati possibili | Uso |
|---|---|---|
| Lingua | Defined / Derive | output testuale e metadati |
| Pubblico | Defined / Propose | tono, difficoltà, densità |
| Obiettivo | Defined | direzione editoriale |
| Materiali/fonti | Defined / None | grounding |
| Quantità unità | Defined / Propose / Derive / Later | immagini, puzzle, domande, record |
| Struttura | Defined / Propose / Derive / Later | long-form / libro illustrato |
| Note da evitare | Defined / None | negative constraints |
| Output | Defined | response contract |

---

## 2. Coloring Book

### Identità del progetto

- tema/idea;
- quantità tavole;
- pubblico;
- difficoltà;
- formato/orientamento/risoluzione.

### Contenuto

- soggetti;
- personaggi/entità Consistent;
- ambientazione generale;
- Scene;
- partecipanti per scena;
- azione/relazione per tavola quando applicabile.

### Linguaggio visuale

- stile principale;
- Kawaii semantico quando selezionato;
- Cozy ON/OFF;
- Bold & Easy ON/OFF;
- line weight;
- complessità;
- densità;
- sfondo;
- white space.

### HARD coloring

- B/N puro;
- no grigi/ombre/gradienti;
- aree colorabili;
- clean contours;
- no microcelle inappropriate;
- soggetto leggibile;
- no testo/watermark;
- qualità/anatomia coerente col soggetto;
- no placeholder geometrici casuali.

---

## 3. Raccolta immagini

### Progetto

- uso della raccolta;
- quantità;
- serie coerente sì/no;
- ordine/sequenza;
- descrizioni sì/no;
- formato file;
- orientamento;
- risoluzione.

### Contenuto visuale

- soggetti;
- ambienti;
- Scene opzionali;
- Consistent opzionale;
- viewpoint;
- inquadratura;
- livello dettaglio;
- stile rendering;
- resa colore;
- sfondo;
- uniformità di scala/inquadratura.

### Output

- immagini;
- descrizioni associate;
- metadati;
- ordine raccolta.

---

## 4. Libro illustrato

### Progetto

- pubblico;
- finalità narrativa/informativa;
- lunghezza indicativa opzionale;
- struttura nota/proposta/derivata;
- quantità testo per nodo/pagina;
- formato.

### Struttura

- parti;
- capitoli/sezioni;
- nodi/pagine;
- testo/brief per nodo.

### Piano visuale

Per ciascun nodo:

- illustrazione necessaria sì/no;
- scopo;
- scena;
- partecipanti;
- reference;
- Consistent;
- inquadratura;
- relazione testo/immagine.

### Output

- testo Candidate;
- immagini Candidate;
- mapping posizione → asset;
- descrizioni/didascalie.

---

## 5. Romanzo / racconto

### Bussola

- genere/sottogenere;
- pubblico;
- premessa;
- promessa al lettore;
- tono;
- POV;
- tempo verbale;
- lingua.

### Lunghezza e struttura

Ognuno separato:

- parole target: Defined / Propose / Later;
- pagine target: Defined / Derive / Later;
- parti: Defined / Propose / Later;
- capitoli: Defined / Propose / Later;
- scene: Defined / Propose / Later.

### Fondamenta narrative

- conflitto centrale;
- posta in gioco;
- arco;
- temi;
- finale;
- limiti/cose da evitare.

### Bible

- personaggi;
- obiettivi;
- relazioni;
- luoghi;
- timeline;
- regole del mondo;
- fatti canonici.

### Unità di produzione

Capitolo e/o scena:

- titolo;
- obiettivo;
- POV;
- luogo/tempo;
- partecipanti;
- beat/eventi richiesti;
- apertura/chiusura;
- lunghezza indicativa opzionale;
- note manuali.

### Response

- Candidate testo;
- versione;
- stato;
- note review;
- applicazione al Master.

---

## 6. Saggio / manuale

### Progetto

- argomento;
- obiettivo per il lettore;
- pubblico/livello;
- tono;
- profondità;
- lingua;
- lunghezza opzionale.

### Fonti

- materiali obbligatori;
- fonti ammesse;
- fonti vietate;
- citazioni richieste sì/no;
- policy fattuale;
- glossario/terminologia.

### Struttura

- parti;
- capitoli;
- sezioni;
- esempi;
- esercizi;
- box;
- appendici.

### Unità di produzione

- obiettivo sezione;
- concetti obbligatori;
- prerequisiti;
- esempi;
- fonti;
- figure/tabelle;
- livello dettaglio;
- lunghezza opzionale.

### Review

- copertura outline;
- fatti;
- terminologia;
- citazioni;
- ridondanze;
- chiarezza;
- figure mancanti.

---

## 7. Word Search

Vincolo: seguire il WordSearchListManager ancestor spec.

### DATABASE

- file sorgente;
- column mapping;
- parola;
- ID;
- rilevanza;
- KDPSAFE;
- tassonomie;
- extra columns.

### FILTRI

- tassonomie 1/2;
- rilevanza;
- KDPSAFE;
- used/not used;
- esclusioni.

### GENERA

- numero puzzle;
- parole/puzzle;
- puzzle/blocco;
- blocchi omogenei;
- riuso consentito sì/no;
- max lunghezza parola/frase;
- tema/descrizione per puzzle.

### CONTROLLO

- uniqueness whole-book;
- troppo lunghe;
- riusi;
- sostituzione contestuale.

### OUTPUT

- database Diez;
- liste;
- manifest;
- Titans CSV/XLSX.

---

## 8. Cruciverba

- lingua;
- tema;
- pubblico;
- difficoltà;
- quantità;
- fonte lessico;
- parola;
- definizione Candidate;
- categoria;
- lunghezza;
- caratteri ammessi;
- gioco di parole sì/no;
- numero Candidate definizione;
- handoff Qxw sì/no.

Review:

- risposta contenuta nella definizione;
- ambiguità;
- duplicati;
- definizioni troppo simili;
- difficoltà;
- compatibilità griglia.

---

## 9. Quiz / trivia

- scopo;
- pubblico;
- lingua;
- categorie;
- quantità domande;
- risposte/domanda;
- distribuzione difficoltà;
- spiegazione sì/no;
- fonti;
- cutoff temporale;
- temi vietati;
- ordine/round.

Per domanda:

- testo;
- opzioni;
- risposta corretta;
- spiegazione;
- categoria;
- difficoltà;
- fonte/provenienza.

Review:

- duplicati semantici;
- più risposte corrette;
- ambiguità;
- distrattori deboli;
- supporto della risposta;
- difficoltà incoerente.

---

## 10. Catalogo / raccolta dati

### Scopo

- oggetto raccolto;
- uso;
- quantità opzionale;
- perimetro geografico/temporale;
- inclusioni/esclusioni.

### Schema

Per campo:

- nome;
- tipo;
- obbligatorio;
- descrizione;
- esempio;
- normalizzazione.

### Fonti/provenienza

- fonti;
- URL/origine;
- data raccolta;
- affidabilità;
- note.

### Review

- schema;
- missing;
- duplicati;
- normalizzazione;
- conflitti;
- provenance.

---

## 11. Altro

- obiettivo;
- unità del risultato;
- struttura;
- quantità opzionale;
- vincoli;
- formato output;
- review criteria;
- export.

---

## 12. Implicazioni per la UI Uno

L'attuale rendering generico di `BookTypeAiOptionsCoreService` può restare temporaneamente come fallback, ma non deve dettare la UX definitiva.

Ogni decisione futura deve dichiarare almeno:

- chiave stabile;
- label parlante;
- tipo controllo;
- se è obbligatoria;
- stati ammessi;
- passo in cui compare;
- se entra nel Prompt globale o di unità;
- se genera un HARD lock;
- validator associato;
- dipendenze/visibilità.

Questo documento sarà la base per trasformare gli attuali form in percorsi metodologici senza riscrivere il sistema Prompt a ogni iterazione UI.
