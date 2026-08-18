# Finalizzazione — bundle di output, materiali e database

Status: **DIRETTIVA DI PRODOTTO / WORKING — NON CONSOLIDATA FINO A TEST FISICO**

Data: 2026-08-19

## 1. Principio

Un libro finalizzato non è sempre un singolo file.

Diez deve poter produrre un **bundle editoriale** composto dal risultato principale e dagli artefatti a corredo necessari per continuare a lavorare, consegnare o archiviare l'edizione.

L'utente sceglie cosa esportare; i preset dipendono dal Tipo libro.

## 2. Evidenza dalla linea Avalonia

La linea Avalonia possedeva già separatamente:

- export del Master in CSV/XLSX;
- export ZIP delle immagini incorporate nel progetto;
- servizi dedicati per DOCX illustrato e layout raccolta immagini;
- Word Search full database XLSX reimportabile;
- Word Search puzzle XLSX/CSV in colonne.

La migrazione Uno deve recuperare queste capacità senza obbligare l'utente a conoscere i nomi dei vecchi servizi.

## 3. Componenti di un bundle

Un preset di finalizzazione può includere:

### A. Libro / risultato principale

Esempi:

- DOCX;
- PDF;
- TXT/Markdown;
- XLSX/CSV per famiglie tabellari;
- output specializzato esterno.

### B. Materiali utente

Originali o materiali selezionati importati nel progetto, raccolti in **Materiali ZIP** con struttura e manifest leggibili.

### C. Materiali AI approvati

Immagini, documenti o altri asset AI effettivamente approvati/portati nel libro, esportabili come **Asset approvati ZIP**.

### D. Database / dataset

Quando il Tipo libro lo richiede:

- database completo;
- database del libro;
- dataset di handoff.

### E. Manifest di consegna

File leggibile che spiega:

- titolo/edizione;
- data/freeze;
- file presenti;
- relazione fra asset e posizioni;
- provenienza (`Utente`, `AI approvata`, `Derivato`);
- eventuali formati esterni.

## 4. Materiali ZIP

### 4.1 Materiali utente

Deve essere possibile esportare gli originali incorporati o una selezione coerente di materiali utente.

Lo ZIP non deve duplicare automaticamente file non pertinenti alla pubblicazione se il preset richiede soltanto gli asset usati.

Prevedere almeno due scope:

- **Tutti i materiali del progetto**;
- **Solo materiali usati dall'edizione**.

### 4.2 Asset AI approvati

ZIP distinto o cartella distinta contenente soltanto Candidate approvate e applicate/necessarie all'edizione.

Non includere implicitamente Candidate scartate o versioni fallite nel bundle pubblico; queste restano nello storico `.diez`.

### 4.3 Bundle combinato

Opzione comoda:

`Libro finale + materiali a corredo`

con sottocartelle leggibili, per esempio:

- `/book/`
- `/materials/user/`
- `/materials/ai-approved/`
- `/database/`
- `/manifest/`

I nomi tecnici definitivi possono variare, ma il contenuto deve essere comprensibile anche fuori da Diez.

## 5. Word Search — quattro output distinti

Word Search deve distinguere chiaramente quattro scopi.

### 5.1 Database completo XLSX

È il patrimonio di lavoro complessivo disponibile nel progetto/dataset:

- tutte le parole disponibili;
- metadata originali;
- tassonomie;
- colonne extra;
- mapping sufficiente al round-trip;
- dati puzzle quando inclusi dal contratto.

Deve essere reimportabile in Diez senza perdita informativa.

La linea Avalonia aveva già `WordSearchFullDatabaseExportService` con fogli `PAROLE`, `DATABASE`, `INFO`; la nuova versione deve evolvere verso lo schema adattivo e preservare colonne non standard.

### 5.2 Database del libro XLSX — NUOVO REQUISITO

Contiene **solo il sottoinsieme effettivamente appartenente al libro corrente**.

Per ogni parola usata conserva, dove disponibili:

- parola;
- ID sorgente;
- puzzle/posizione di utilizzo;
- tema/scenario;
- tassonomie;
- anno/decade o altro asse attivo;
- rilevanza/KDPSAFE;
- colonne extra utili;
- origine/provenienza;
- stato/nota editoriale.

Obiettivi:

- sapere esattamente quale porzione del database è stata consumata dal libro;
- poter archiviare l'edizione;
- confrontare libri diversi;
- alimentare controlli di riuso fra progetti se in futuro desiderato;
- permettere reimport/analisi senza consegnare necessariamente l'intero database sorgente.

Non va confuso con l'XLSX dei puzzle in colonne.

### 5.3 Liste puzzle / Self-Publishing Titans

Output operativo già previsto:

- XLSX;
- CSV;
- un puzzle per colonna;
- parole verticali;
- profilo Titans quando selezionato.

Questo è un handoff, non il database ricco.

### 5.4 Manifest

Riepiloga:

- numero puzzle;
- parole per puzzle;
- unicità;
- scenario/variante quando applicabile;
- ID dell'edizione/freeze;
- file esportati.

## 6. Libri visuali

Coloring, Raccolta immagini e Libro illustrato devono poter esportare:

- documento/libro principale quando applicabile;
- immagini finali approvate in ordine editoriale;
- materiali/reference utente quando richiesti;
- eventuali descrizioni/didascalie;
- manifest posizione → asset;
- eventuale ZIP completo a corredo.

Una raccolta immagini può avere come output principale proprio una cartella/ZIP di immagini, mentre un Libro illustrato avrà normalmente anche un documento impaginato.

## 7. Romanzo / racconto

Preset tipico:

- DOCX/PDF/TXT del Master finalizzato;
- eventuale Bible/outline per uso editoriale interno;
- eventuali immagini/illustrazioni solo se il progetto le usa;
- materiali sorgente opzionali come archivio, non necessariamente consegnati al lettore/stampatore.

## 8. Saggio / manuale

Preset tipico:

- documento finale;
- figure/tabelle finali;
- materiali a corredo selezionati;
- bibliografia/fonti o report provenance quando previsto;
- manifest degli asset.

## 9. Quiz / trivia

Possibili output:

- dataset domande/risposte XLSX/CSV;
- documento impaginato;
- fonti/provenance report;
- materiali a corredo quando presenti.

## 10. Catalogo / raccolta dati

Output principale spesso è il dataset:

- XLSX/CSV ricco;
- schema/manifest;
- provenance;
- eventuali asset associati ZIP.

## 11. UI Esportazione

La pagina **Esportazione** non deve essere una casella di testo con elenco formati.

Proposta:

### Tab `Edizione finale`

- destinazione;
- formato principale;
- metadata;
- freeze/preflight;
- genera.

### Tab `Materiali a corredo`

checkbox/preset contestuali:

- Materiali ZIP;
- Asset AI approvati ZIP;
- Solo asset usati / tutti i materiali;
- Manifest.

### Tab `Database / Handoff`

solo per famiglie applicabili:

- Database completo XLSX;
- Database del libro XLSX;
- Titans XLSX;
- Titans CSV;
- altri handoff.

### Tab `Riepilogo`

mostra prima dell'export:

- cosa verrà creato;
- numero file/asset;
- eventuali problemi bloccanti;
- destinazione.

## 12. Freeze e riproducibilità

Il bundle deve riferirsi a una versione/freeze dell'edizione.

Se dopo il freeze cambiano:

- testo;
- immagini approvate;
- puzzle;
- mapping;
- metadata;

il bundle precedente resta storico e quello nuovo richiede un freeze/candidate aggiornato secondo le regole di finalizzazione.

## 13. Provenienza

Ogni asset a corredo deve poter essere ricondotto a:

- originale utente;
- AI Candidate approvata;
- derivato/impaginato;
- origine esterna quando nota.

Non serve mostrare hash nel nome file, ma il manifest può conservarli per integrità.

## 14. Acceptance test futuro

### Visuale

1. progetto con due immagini utente e tre AI approvate;
2. finalizza;
3. esporta documento + Materiali ZIP + Asset approvati ZIP;
4. verifica file e mapping;
5. nessuna Candidate scartata nel bundle finale.

### Word Search

1. database sorgente con parole usate e non usate;
2. genera libro;
3. esporta Database completo XLSX;
4. esporta Database del libro XLSX;
5. il secondo contiene solo parole/record del libro ma conserva i metadata;
6. esporta Titans XLSX/CSV;
7. verifica che i tre scopi non vengano confusi.

## 15. Principio da preservare

**Finalizzare significa congelare e consegnare l'edizione insieme agli artefatti necessari al suo uso e alla sua continuità, non semplicemente fare “Salva con nome” del file principale.**