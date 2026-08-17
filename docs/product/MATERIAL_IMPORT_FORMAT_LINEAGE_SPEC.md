# Diez — genealogia dei formati di import e distinzione fra database e materiali

Status: **DIRETTIVA DI PRODOTTO / MEMORIA DI MIGRAZIONE — NON CONSOLIDATA FINO A TEST FISICO UNO**

Data: 2026-08-18

Questo documento chiarisce la provenienza delle capacità di import osservate nelle diverse fasi di Diez e impedisce di confondere due contratti diversi:

1. **import del database operativo di una famiglia** (es. lessico Word Search);
2. **intake generale dei materiali di progetto** (documenti, tabelle, immagini e altre fonti incorporate nel `.diez`).

## 1. WordSearchListManager: database operativo

La specifica dell'antenato Word Search documenta come formati di database/lessico osservati:

- XLSX;
- CSV;
- TSV;
- TXT.

Questi formati appartengono al percorso `DATABASE → FILTRI → GENERA → CONTROLLO → ESPORTA` e servono a costruire una griglia di record/parole con colonne mappabili, tassonomie, filtri e metadata.

Il punto fondamentale non è l'estensione in sé, ma la capacità di ottenere una struttura tabellare/record sulla quale mappare ruoli operativi.

## 2. Diez Avalonia Preview 0.1: primo intake generale

Con la successiva evoluzione di Diez in framework multi-libro, l'applicazione Avalonia ha introdotto un intake generale dei materiali di progetto.

Il commit storico `4928364d223b389634996b95e83683c890235347` (`Preview 0.1: Materials intake and safer installer upgrades`) dichiara:

- TXT;
- Markdown / MD;
- CSV;
- XLSX.

Questi file non erano più soltanto "database Word Search": diventavano materiali del progetto persistiti nel `.diez`.

## 3. Diez Avalonia Preview 0.2: documenti e immagini

Il commit storico `59f8754e7ce95810e841c958fef5b9ec3cafe688` (`Preview 0.2: real .diez package and richer intake`) estende esplicitamente l'intake generale a:

### Documenti

- TXT;
- MD;
- DOCX;
- ODT;
- RTF;
- PDF.

### Tabelle

- CSV;
- XLSX.

### Immagini comuni

- PNG;
- JPG;
- JPEG;
- GIF;
- BMP;
- WebP.

La stessa Preview introduce inoltre:

- selezione multipla dei file;
- originali incorporati nel pacchetto `.diez`;
- deduplica dei materiali;
- anteprima/intake;
- rimozione dal progetto senza cancellare il file sorgente sul PC.

Questa capacità nasce con l'allargamento di Diez ai libri con immagini e ai materiali eterogenei e **non va retroattribuita al WordSearchListManager originario**.

## 4. Regola di migrazione verso Uno

Uno Platform non deve regredire rispetto alla più ricca capacità di intake raggiunta dalla linea Avalonia.

La nuova architettura deve quindi distinguere almeno due registry/contratti:

### A. `ProjectMaterialImportAdapter`

Per incorporare materiali sorgente nel progetto.

Deve poter gestire almeno il set storicamente raggiunto da Avalonia:

- TXT / MD;
- CSV / XLSX;
- DOCX / ODT / RTF / PDF;
- PNG / JPG / JPEG / GIF / BMP / WebP.

Nuovi formati possono essere aggiunti tramite adapter senza cambiare il modello editoriale.

### B. `StructuredDatasetImportAdapter`

Per trasformare un file in record/righe/colonne utilizzabili da workspace come Word Search, Catalogo/raccolta dati e altri strumenti tabellari.

Il set iniziale deve preservare almeno il contratto storico Word Search:

- XLSX;
- CSV;
- TSV;
- TXT quando interpretabile come dataset/lista.

Altri formati possono diventare dataset soltanto tramite un adapter/trasformazione esplicita e verificabile.

## 5. Non tutti i materiali sono database

Un PDF, DOCX o'immagine importati nel progetto possono essere:

- fonte;
- reference;
- materiale editoriale;
- paradigma visuale;
- documento da analizzare;
- asset da usare o trasformare.

Non devono diventare automaticamente righe della griglia Word Search.

Viceversa un CSV/XLSX/TSV scelto esplicitamente come **database Word Search** deve passare dal mapping schema/ruoli e alimentare griglia e filtri adattivi.

Quindi:

`formato supportato dal progetto` ≠ `formato automaticamente interpretabile come database Word Search`.

## 6. Un solo picker non deve imporre un solo significato

Lo stesso file può avere ruoli differenti.

Esempio: un XLSX può essere:

- database Word Search;
- tabella di fonti per un saggio;
- catalogo dati;
- materiale di riferimento generale.

L'utente sceglie il contesto/azione e Diez applica l'adapter corretto.

La UI deve parlare in termini di azione:

- `Aggiungi materiali al progetto`;
- `Importa database Word Search`;
- `Importa struttura / outline`;
- `Aggiungi reference immagini`;

non soltanto `Apri file`.

## 7. Rapporto con la UI adattiva Word Search

Quando un file viene scelto come database Word Search:

1. viene letto dall'adapter strutturato;
2. viene mostrata l'anteprima di schema;
3. vengono mappati i ruoli operativi;
4. la griglia nasce dalle colonne reali;
5. i filtri nascono dai ruoli/tassonomie realmente disponibili;
6. colonne temporali come anno/decade compaiono solo se presenti/mappate e utili.

L'intake generale di immagini e documenti non deve rendere la griglia Word Search più rigida o più rumorosa.

## 8. Rapporto con i libri visuali

Per Coloring, Raccolta immagini e Libro illustrato, la linea Avalonia dimostra che l'intake immagini/documenti è una capacità trasversale già acquisita dal prodotto.

Uno deve conservarla e migliorarla con:

- preview reale;
- provenienza;
- ruolo del materiale;
- reference/paradigma;
- collegamento a Scene/soggetti/posizioni;
- originali separati dalle Candidate AI;
- persistenza nel `.diez`.

## 9. Formati futuri

Non hard-codare la logica editoriale su un elenco chiuso di estensioni.

Ogni adapter deve dichiarare almeno:

- estensioni/MIME riconosciuti;
- capacità (`Material`, `StructuredDataset`, `Image`, `Document`, ecc.);
- possibilità di preview;
- possibilità di estrazione testo/schema;
- eventuali limitazioni;
- risultato canonico prodotto.

Il picker può essere costruito dal registry degli adapter disponibili.

## 10. Acceptance contract futuro Uno

Prima di dichiarare l'intake Uno equivalente/superiore alla linea Avalonia verificare almeno:

1. multi-file material intake;
2. TXT/MD;
3. CSV/XLSX;
4. DOCX/ODT/RTF/PDF;
5. PNG/JPG/JPEG/GIF/BMP/WebP;
6. incorporamento degli originali nel `.diez`;
7. deduplica;
8. preview dove applicabile;
9. rimozione dal progetto senza cancellare l'originale esterno;
10. Word Search: TSV preservato nel percorso database anche se non faceva parte del picker generale Preview 0.2;
11. nessuna confusione tra import materiale e import database;
12. round-trip salva/chiudi/riapri senza perdita degli asset o del mapping.

## 11. Principio da preservare

**La capacità di intake di Diez è cresciuta oltre il Word Search con Avalonia. Uno deve ereditare il set più ricco raggiunto dal prodotto, ma ogni famiglia decide come interpretare il materiale importato.**
