# Word Search — contratto delle griglie operative

Status: **DIRETTIVA DI PRODOTTO / PARITÀ FUNZIONALE — NON IMPLEMENTARE FINCHÉ IL LAVORO WORD SEARCH NON È AUTORIZZATO**

Data: 2026-08-19

Questo documento integra `WORD_SEARCH_LIST_MANAGER_ANCESTOR_SPEC.md` e `WORD_SEARCH_ADAPTIVE_SCHEMA_UI_SPEC.md`.

## 1. Principio

Le griglie Word Search sono strumenti di lavoro editoriale, non semplici tabelle decorative.

Devono comportarsi come superfici dati familiari a chi lavora con fogli di calcolo:

- selezione precisa;
- copia rapida;
- editing quando consentito;
- tastiera;
- ordinamento/filtri;
- dettaglio espandibile dove un record contiene una struttura più ricca.

## 2. Tipi di griglia

### 2.1 Database parole

Scopo: consultare e correggere il database importato.

Requisiti:

- colonne generate dallo schema reale;
- celle selezionabili;
- selezione di intervalli quando il controllo Uno scelto lo permette;
- `Ctrl+C` copia la selezione in formato tabellare compatibile con Excel/Sheets;
- editing inline delle colonne editabili;
- colonne operative protette quando una modifica deve passare da una regola specifica;
- header visibili durante lo scroll;
- resize e riordino colonne;
- visibilità colonne configurabile;
- ricerca e vai-a-ID;
- aggiunta/eliminazione controllata;
- nessuna perdita delle colonne extra.

### 2.2 Risultati filtrati

Scopo: capire esattamente il pool su cui opererà GENERA.

Requisiti:

- selezione/copia;
- esclusione dalla selezione corrente senza cancellare dal database;
- conteggio live;
- evidenza dei filtri attivi;
- possibilità di vedere perché una parola è inclusa/esclusa quando utile;
- colonne coerenti con gli assi tassonomici attivi.

### 2.3 Puzzle generati

Scopo: vedere il libro come insieme di puzzle e poter controllare ciascuno senza perdere la visione whole-book.

Ogni riga rappresenta un puzzle e deve poter essere **espansa**.

Riga compatta:

- ordine;
- ID;
- titolo/tema/scenario;
- variante (decade, regione, stagione...) quando applicabile;
- numero parole;
- stato;
- problemi.

Dettaglio espanso:

- lista completa delle parole;
- eventuali metadata/tassonomie rilevanti;
- parole riutilizzate evidenziate;
- controlli del puzzle;
- azioni di sostituzione chirurgica;
- note/descrizione.

L'espansione non apre obbligatoriamente una nuova finestra.

### 2.4 Controllo

La griglia di controllo può mostrare problemi aggregati:

- puzzle;
- posizione/parola;
- tipo problema;
- severità;
- proposta;
- stato.

Selezionare un problema deve portare direttamente alla cella/voce interessata.

## 3. Clipboard

Comportamento minimo:

- `Ctrl+C`: copia cella o selezione corrente;
- copia di più celle: TSV negli appunti, per incollare naturalmente in Excel/Sheets;
- copia di una riga: conserva ordine colonne visibile;
- intestazioni opzionali tramite comando esplicito `Copia con intestazioni`;
- nessun ID tecnico nascosto viene copiato salvo colonna visibile/selezionata.

Non modificare i dati con la semplice copia.

## 4. Tastiera

Dove il controllo Uno lo supporta in modo affidabile:

- frecce: navigazione celle;
- Tab / Shift+Tab: cella successiva/precedente;
- Enter/F2: modifica cella;
- Esc: annulla modifica corrente;
- Ctrl+C: copia;
- Ctrl+F: porta alla ricerca della griglia;
- Delete: non cancella record senza conferma/azione esplicita; può svuotare una cella solo nei contesti dove è semanticamente sicuro.

Le scorciatoie devono rispettare gli standard di piattaforma e non sostituire i pulsanti visibili.

## 5. Editing inline

Non tutte le griglie sono ugualmente editabili.

### Database

Editing normalmente consentito sulle colonne del record, con validazione.

### Pool filtrato

Preferire editing del record sorgente oppure azioni contestuali; evitare copie divergenti dello stesso dato.

### Puzzle generati

Consentire modifiche controllate a:

- titolo;
- tema/descrizione;
- singola parola/posizione;
- note/stato.

Una sostituzione di parola deve passare dal servizio whole-book e non essere un semplice setter della cella.

## 6. Validazione dopo editing

Ogni modifica che può incidere sui vincoli del libro deve rieseguire i controlli pertinenti.

Esempio: se l'utente sostituisce una parola in Puzzle 73, Diez deve verificare se quella parola è già presente in Puzzle 4, 21 o 99.

La UI può modificare localmente; l'autorità di validazione resta globale.

## 7. Espansione dei puzzle

Il pattern preferito è master/detail espandibile:

- freccia/chevron nella riga;
- apertura del contenuto sotto la riga o pannello dettagli sincronizzato;
- possibilità di tenere aperto almeno un puzzle mentre si scorre;
- la lista delle parole deve poter essere copiata;
- eventuali problemi sono evidenziati senza rendere il testo illeggibile.

Per libri con molti puzzle, virtualizzare e non renderizzare centinaia di dettagli espansi simultaneamente.

## 8. Scene semantiche e varianti

Quando il libro usa Scenario:

- la griglia può mostrare `Scenario` e `Variante` come colonne;
- se il progetto non usa quegli assi, le colonne non compaiono;
- espandendo il puzzle si possono mostrare le ragioni tassonomiche delle parole quando utile al controllo.

Esempio:

`Pranzo di Natale | 1970s | 20 parole | OK`

oppure, in un progetto non nostalgico:

`Vita in fattoria | Inverno | 18 parole | 1 problema`.

## 9. Selezione multipla e azioni batch

Azioni batch ammissibili solo quando non violano l'identità dei record:

- copia;
- cambia visibilità/stato quando semanticamente sicuro;
- esporta selezione;
- marca per controllo.

Non introdurre un `Sostituisci ovunque` implicito per le parole: la sostituzione resta chirurgica e rivalidata.

## 10. Stato modificato

Editing in griglia deve aggiornare il dirty-state del progetto.

Prima di cambiare dataset/progetto o chiudere l'app, le modifiche devono partecipare allo stesso contratto Save/Exit della shell.

## 11. Accessibilità

- focus visibile;
- contrasto sufficiente;
- celle raggiungibili da tastiera;
- stato problema non comunicato soltanto dal colore;
- tooltip/help per comandi specifici;
- dimensioni righe/colonne leggibili.

## 12. Acceptance test futuro

1. import dataset con almeno 10 colonne, incluse extra;
2. seleziona e copia un rettangolo di celle in Excel/Sheets;
3. modifica una cella editabile e salva;
4. riapri `.diez` e verifica persistenza;
5. nascondi/riordina colonne senza perdita dati;
6. genera almeno 50 puzzle;
7. espandi puzzle 1, 25, 50 e verifica contenuti;
8. copia le parole del puzzle espanso;
9. sostituisci una parola con una già usata altrove e verifica blocco/errore whole-book;
10. usa tastiera per navigazione/copia/editing;
11. verifica comportamento a finestra massimizzata e ridotta.

## 13. Principio da preservare

**Una griglia Word Search deve permettere di lavorare sui dati con la stessa immediatezza di un buon foglio elettronico, ma con in più le regole editoriali e whole-book che un foglio elettronico non conosce.**