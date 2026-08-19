# Diez — export del libro, materiali e database Word Search

Status: **DIRETTIVA DI PRODOTTO / DA IMPLEMENTARE E VALIDARE FISICAMENTE**

## 1. Principio generale

L'esportazione non deve produrre soltanto il file principale del libro quando il progetto contiene materiali/asset che servono alla consegna editoriale.

Per un libro non solo testuale devono poter essere esportati, separatamente o in un pacchetto coordinato:

- output principale del libro;
- materiali utente incorporati e pertinenti;
- asset generati con AI e approvati;
- eventuali reference/paradigmi quando previsti dalla policy di handoff;
- manifest/provenienza sufficienti a capire origine e ruolo dei file;
- database o dataset operativi quando la famiglia li usa.

## 2. Evidenza Avalonia da preservare

La linea Avalonia esponeva già:

- `Solo immagini (ZIP)`;
- `Pacchetto completo per impaginatore`;
- DOCX/CSV/XLSX;
- piano immagini;
- libreria degli output finalizzati.

Il pacchetto completo raccoglieva documento/tabelle, immagini originali, dati libro, piano immagini e controllo di integrità.

La Uno deve preservare questa filosofia ma renderla più esplicita e adattiva alla famiglia.

## 3. Esportazione — struttura UI proposta

Nella macroarea **Esportazione**, usare tab/sezioni come:

### Libro / output principale

Formati applicabili alla famiglia: DOCX, PDF, CSV/XLSX, immagini, handoff specializzati ecc.

### Materiali

Opzioni:

- `Materiali utente (ZIP)`;
- `Asset AI approvati (ZIP)`;
- `Materiali + asset approvati (ZIP)`;
- `Pacchetto completo di produzione (ZIP)`.

Il contenuto deve mantenere nomi utili, provenienza e manifest; evitare collisioni di filename.

### Database / dati

Visibile solo alle famiglie che usano dataset operativi, in particolare Word Search e Catalogo dati.

## 4. Materiali utente vs AI

Il pacchetto deve distinguere almeno semanticamente:

- `user-materials/` — originali/import dell'utente pertinenti all'handoff;
- `approved-ai-assets/` — Candidate AI approvate/applicate;
- `references/` — reference/paradigmi se inclusi;
- `manifest` — mappa origine → ruolo → placement/ContentId/SubjectId dove necessario.

Non includere automaticamente Candidate scartate o fallite nel pacchetto editoriale finale; possono rimanere nel `.diez` per storia/provenienza.

## 5. Word Search — due database XLSX distinti

Devono esistere **entrambi**:

### A. Database completo

L'intero database/lessico operativo del progetto, indipendentemente dalle parole effettivamente entrate nel libro.

Deve preservare:

- colonne originali;
- mapping dei ruoli;
- colonne extra;
- metadata tassonomici;
- anno/decade quando presenti;
- stato used/not used;
- dati necessari al round-trip/reimport.

Nome UI suggerito: `Database completo (XLSX)`.

### B. Database del libro

Solo le parole/record effettivamente utilizzati nei puzzle del libro corrente, con sufficiente metadata per controllo e tracciabilità.

Nome UI suggerito: `Database di questo libro (XLSX)`.

Il database del libro NON sostituisce il database completo.

## 6. Word Search — altri export da preservare

Oltre ai due database XLSX:

- liste/puzzle;
- manifest;
- Self Publishing Titans CSV;
- Self Publishing Titans XLSX;
- eventuali export canonici già supportati;
- materiali ZIP quando il progetto include reference/materiali supplementari.

## 7. Database del libro: regole

Per ciascuna parola usata deve poter essere ricostruito almeno:

- puzzle/posizione in cui è usata;
- parola canonica;
- ID sorgente se disponibile;
- tassonomie rilevanti;
- scena/scenario/variante quando applicabile;
- metadata temporali quando applicabili;
- provenienza/database origine;
- eventuale stato/nota editoriale.

Se la stessa parola è vietata whole-book, l'export deve risultare coerente con quella policy.

## 8. Export adattivo

Le voci di Esportazione devono dipendere dal Tipo libro/capability.

Esempi:

- Romanzo puro: DOCX/PDF + eventuali materiali/fonti;
- Libro illustrato: DOCX/PDF + asset approvati + materiali/reference + piano placement;
- Coloring: immagini finali + materiali/reference + eventuale PDF/layout;
- Word Search: database completo, database libro, Titans, manifest/liste;
- Catalogo dati: dataset completo/filtrato e provenance.

Non mostrare opzioni inutili per la famiglia corrente.

## 9. Finalizzazione e Libreria finalizzati

Ogni output finalizzato deve poter registrare:

- ricetta/formato;
- versione/freeze di origine;
- data;
- percorso/link;
- hash quando utile;
- elenco dei companion output creati insieme.

Un pacchetto completo deve poter essere riaperto/identificato dalla Libreria finalizzati senza confonderlo con un semplice DOCX.

## 10. Acceptance test futuro

1. Libro illustrato con materiali utente + Candidate AI approvate → export ZIP materiali completo e verificabile;
2. nessuna Candidate scartata nel pacchetto finale;
3. Word Search → `Database completo.xlsx` contiene anche parole non usate;
4. Word Search → `Database libro.xlsx` contiene solo parole usate e mapping puzzle;
5. entrambi gli XLSX preservano colonne extra/metadata applicabili;
6. Titans CSV/XLSX ancora disponibili;
7. pacchetto completo mantiene manifest/provenienza;
8. salva/chiudi/riapri prima dell'export senza perdita;
9. Libreria finalizzati registra output e companion.
