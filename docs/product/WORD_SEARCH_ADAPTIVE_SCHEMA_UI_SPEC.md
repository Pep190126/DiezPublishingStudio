# Word Search — schema importato e UI adattiva

Status: **DIRETTIVA DI PRODOTTO / CAPACITÀ DA PRESERVARE — NON IMPLEMENTARE FINCHÉ L'UTENTE NON AUTORIZZA ESPLICITAMENTE IL LAVORO WORD SEARCH**

Data: 2026-08-18

Questo documento integra:

- `docs/legacy/WORD_SEARCH_LIST_MANAGER_ANCESTOR_SPEC.md`;
- `docs/product/WORD_SEARCH_SEMANTIC_SCENES_SPEC.md`;
- `docs/product/BOOK_FAMILY_CAPABILITY_REGISTRY_SPEC.md`.

## 1. Principio fondamentale

Word Search **non deve avere una griglia e un pannello filtri hard-coded sul Nostalgic Word Search**.

La UI deve adattarsi al dataset realmente importato e ai **ruoli semantici** che l'utente assegna alle colonne.

Esempio obbligatorio:

- se il dataset contiene `word`, `category`, `subcategory`, `year`, `decade`, e il progetto usa la dimensione temporale, anno/decade possono comparire in griglia e filtri;
- se il dataset riguarda animali, geografia, cucina o altro e non contiene/usa alcuna dimensione temporale, **Anno** e **Decade non devono comparire come colonne o filtri vuoti/inutili**.

La UI deve sembrare costruita per il dataset corrente, non per un database storico specifico.

## 2. Schema fisico vs ruoli operativi

Diez deve distinguere:

1. **colonne fisiche importate** — nomi originali del file;
2. **ruoli operativi mappati** — significato che Diez usa per generazione, filtro e controllo;
3. **colonne extra** — dati preservati anche se non hanno un ruolo speciale.

I nomi delle colonne non devono essere imposti.

Esempio:

un file può avere:

- `TERM`
- `MACROAREA`
- `TOPIC`
- `ERA`
- `SAFE`

oppure:

- `word`
- `category`
- `subcategory`
- `primary_decade`
- `kdp_safe`

Entrambi possono essere equivalenti dopo il mapping dei ruoli.

## 3. Mapping iniziale

Dopo l'import, Diez deve mostrare un passaggio di mappatura che permetta almeno di assegnare:

- parola/testo principale — obbligatorio;
- ID — opzionale;
- rilevanza/peso — opzionale;
- safety/KDPSAFE — opzionale;
- tassonomia principale 1;
- tassonomia subordinata 1;
- tassonomia principale 2;
- tassonomia subordinata 2;
- anno/intervallo temporale — opzionale;
- decade/periodo — opzionale;
- nostalgia/forza evocativa — opzionale;
- stato usato/non usato — opzionale;
- note — opzionale;
- ulteriori ruoli futuri definiti dal profilo.

Le colonne non mappate **restano nel database canonico, visibili/editabili ed esportabili**.

## 4. Griglia adattiva

La griglia DATABASE deve essere generata dallo schema corrente.

### 4.1 Colonne sempre utili

- parola;
- eventuale ID;
- colonne scelte come principali dall'utente.

### 4.2 Colonne condizionali

Una colonna operativa compare se almeno una delle condizioni è vera:

- il ruolo è mappato nel dataset;
- è usato da un filtro/scenario attivo;
- l'utente l'ha resa esplicitamente visibile.

Quindi `Anno`, `Decade`, `Nostalgia`, `KDPSAFE`, tassonomie ecc. **non sono colonne permanenti della UI**.

### 4.3 Colonne extra

L'utente deve poter:

- mostrarle/nasconderle;
- riordinarle;
- modificarle;
- usarle in futuro come filtro/asse se compatibili;
- esportarle senza perdita.

Nascondere una colonna è solo una scelta di visualizzazione, non cancellazione dati.

## 5. Pannello FILTRI adattivo

I controlli disponibili devono derivare dai ruoli mappati e dai valori realmente presenti.

Esempi:

### Dataset nostalgico

Possibili filtri:

- decade;
- anno/intervallo;
- scena;
- categoria/subcategoria;
- nostalgia minima;
- rilevanza;
- KDPSAFE;
- used/not used.

### Dataset animali

Possibili filtri:

- classe;
- habitat;
- continente;
- dieta;
- difficoltà/lunghezza;
- safety;
- used/not used.

Non devono apparire controlli `Anno` o `Decade` se non sono mappati o richiesti dal progetto.

## 6. Tassonomie dinamiche

Le tassonomie non sono limitate ai nomi `category` e `subcategory`.

Diez deve poter costruire filtri dipendenti da qualunque coppia di colonne mappata come tassonomia principale/subordinata.

Esempi:

- `Categoria → Sottocategoria`;
- `Regione → Provincia`;
- `Habitat → Zona`;
- `Sport → Ruolo`;
- `Evento → Momento`;
- `Decade → Anno` quando il dataset lo giustifica.

I valori vengono sempre dal dataset corrente.

## 7. Profili di visualizzazione salvabili

È desiderabile che l'utente possa salvare una configurazione di lavoro composta da:

- colonne visibili;
- ordine colonne;
- larghezze;
- mapping ruoli;
- filtri correnti;
- ordinamento;
- eventuale scenario attivo.

Questa configurazione appartiene al progetto `.diez` o a un preset esplicito, senza cambiare i dati sorgente.

Un progetto Nostalgic può quindi avere una vista diversa da un progetto Animals anche se usano lo stesso motore Word Search.

## 8. Import: capacità, non elenco rigido di tre estensioni

L'import Word Search deve essere progettato come **pipeline di adapter di formato**.

Formati già documentati nell'antenato:

- XLSX;
- CSV;
- TSV;
- TXT.

L'utente ricorda correttamente che il prodotto storico gestiva/accettava più casistiche di quanto vada ridotto a `CSV/TSV/XLSX`.

Regola di prodotto:

- non progettare la UI o il Core assumendo solo tre estensioni;
- mantenere un registro di import adapter estendibile;
- rilevare delimitatore/encoding/schema dove possibile;
- separare **formato contenitore** da **schema delle colonne**;
- quando riprenderà l'implementazione Word Search, recuperare e verificare gli ulteriori formati/casistiche dell'antenato prima di dichiarare parità completa.

Non si enumerano qui formati non ancora verificati per evitare di trasformare un ricordo corretto di maggiore ampiezza in un elenco tecnico inventato.

## 9. Rilevamento e anteprima prima dell'import definitivo

Prima di incorporare il dataset, Diez dovrebbe mostrare:

- formato rilevato;
- encoding/delimitatore quando applicabile;
- intestazioni;
- prime righe;
- numero colonne;
- mapping suggerito;
- eventuali conflitti (colonna parola assente, intestazioni duplicate, righe irregolari).

L'utente può correggere il mapping prima di confermare.

## 10. Schema evolution

Se un database reimportato cambia colonne:

- non perdere automaticamente il vecchio mapping;
- tentare il remap per nome/ruolo;
- segnalare colonne mancanti o nuove;
- preservare dati extra quando possibile;
- non trasformare silenziosamente una colonna diversa in `Anno`, `Categoria` ecc.;
- richiedere conferma quando il significato è ambiguo.

## 11. Rapporto con Scene semantiche

La capacità `scena × variante` deve usare gli assi che esistono davvero.

Per un Nostalgic:

`Pranzo di Natale × Decade`

Per altri progetti:

`Vita in fattoria × Stagione`

`Cucina italiana × Regione`

`Animali marini × Habitat`

La UI deve quindi offrire come assi di scenario le dimensioni tassonomiche/operative disponibili, non un elenco fisso `Anno/Decade`.

## 12. Rapporto con GENERA

Il generatore riceve:

- pool risultante dai filtri correnti;
- mapping ruoli;
- scenario/assi selezionati;
- quantità puzzle/parole;
- regole di bilanciamento;
- unicità whole-book;
- eventuali colonne usate per titolo/descrizione/metadati.

Non deve conoscere i nomi fisici delle colonne: usa i ruoli canonici e gli assi dichiarati.

## 13. Rapporto con CONTROLLO

I controlli devono essere anch'essi adattivi.

Sempre applicabili quando configurati:

- duplicati whole-book;
- lunghezza;
- parole vuote/invalide;
- pool insufficiente.

Condizionali:

- mismatch temporale solo se esiste un asse temporale;
- KDPSAFE solo se mappato/attivato;
- coerenza scena solo se si usa uno Scenario;
- vincoli tassonomici solo per assi attivi.

Nessun warning deve essere prodotto per una dimensione che il progetto non possiede.

## 14. Export e round-trip

L'export database completo Diez deve preservare:

- nomi colonne originali;
- mapping ruoli;
- colonne extra;
- ordine/dati;
- metadata necessari a riaprire la stessa esperienza adattiva.

Reimportando il database ricco, Diez deve poter ricostruire lo stesso significato operativo senza imporre lo schema Nostalgic.

Gli export finali specializzati (es. Self Publishing Titans) restano separati dal database ricco.

## 15. Acceptance contract futuro

Quando il lavoro Word Search sarà autorizzato, test obbligatori:

1. import dataset nostalgico con anno/decade → filtri temporali presenti;
2. import dataset non nostalgico senza anno/decade → nessun controllo temporale inutile;
3. dataset con nomi colonne non standard → mapping corretto;
4. almeno due coppie tassonomiche dipendenti;
5. colonne extra preservate, editabili ed esportabili;
6. cambia visibilità/ordine colonne senza perdita dati;
7. reimport con colonna nuova → schema evolution controllata;
8. generazione usa i ruoli mappati, non nomi hard-coded;
9. controllo whole-book invariato;
10. round-trip database ricco mantiene mapping e colonne extra;
11. almeno un formato delimitato e un formato spreadsheet;
12. recupero/verifica degli ulteriori formati storici prima di dichiarare parità con WordSearchListManager.

## 16. Principio da preservare

**È il database importato a descrivere il dominio editoriale; Diez gli assegna ruoli e strumenti. Non deve essere Diez a costringere ogni database dentro le colonne di un Nostalgic Word Search.**
