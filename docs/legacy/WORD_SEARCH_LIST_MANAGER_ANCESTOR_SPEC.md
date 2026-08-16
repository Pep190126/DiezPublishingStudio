# WordSearchListManager — specifica dell'antenato Word Search

Status: **ANALIZZATO / SPECIFICA CONGELATA — NON IMPLEMENTARE FINCHÉ L'UTENTE NON DICE ESPLICITAMENTE “PROCEDI CON WORD SEARCH”**

Ultima analisi statica: 2026-08-16.

## 1. Provenienza ed evidenza

Questa specifica conserva il comportamento funzionale osservabile dell'applicazione che ha preceduto Diez e dalla quale è nato il percorso Word Search.

Artefatto analizzato staticamente, senza esecuzione:

- nome ricevuto: `WordSearchListManager.exe`
- SHA-256: `222080d4f7be90157e02a500e0b5474a942be09f63f8e738b785b85878e614c7`
- formato: PE32+ Windows GUI x86-64
- packaging osservato: PyInstaller
- versione applicativa osservata nelle risorse/stringhe: `0.7.8`
- moduli applicativi estratti staticamente: `wordsearch.ui`, `wordsearch.io_service`, `wordsearch.storage`, `wordsearch.list_builder`

Livelli di evidenza usati nel documento:

- **OSSERVATO-EXE**: ricavato direttamente dall'artefatto o dai moduli estratti.
- **DIRETTIVA-PRODOTTO**: requisito dato dall'utente per Diez.
- **MIGRAZIONE**: interpretazione architetturale da applicare quando sarà autorizzato il lavoro Word Search.

L'eseguibile non viene aggiunto al repository: la memoria persistente è questa specifica, non il binario.

## 2. Ruolo storico nel prodotto

**DIRETTIVA-PRODOTTO.** WordSearchListManager è l'antenato di Diez. Diez nasce inizialmente per creare Word Search; le altre famiglie di libro vengono aggiunte in seguito fino a trasformarlo in un framework editoriale multi-libro. Quando la migrazione Word Search sarà autorizzata, la nuova implementazione deve recuperare le funzionalità utili dell'antenato senza tornare a un'architettura separata o speciale: database, contenuti, AI, finalizzazione e package `.diez` restano quelli canonici del framework.

## 3. Flusso principale in cinque passi

**OSSERVATO-EXE.** La UI espone un flusso esplicito:

1. `1  DATABASE`
2. `2  FILTRI`
3. `3  GENERA`
4. `4  CONTROLLO`
5. `5  ESPORTA`

Questa sequenza è parte della filosofia dell'antenato e dovrà essere valutata come riferimento UX quando inizierà il lavoro Word Search. Non va però confusa con la navigazione globale di Diez: le cinque fasi sono un percorso della famiglia Word Search, non cinque macrovoci globali.

## 4. DATABASE

### 4.1 Import

**OSSERVATO-EXE.** Formati riconosciuti:

- XLSX
- CSV
- TSV
- TXT

Il database non impone uno schema di nomi colonne rigido. La UI dichiara: **“Mappa i ruoli operativi senza imporre nomi o numero di colonne.”**

### 4.2 Mappatura colonne

**OSSERVATO-EXE.** Il dialogo di mappatura consente almeno:

- colonna parola — obbligatoria;
- identificativo / ID — facoltativo;
- rilevanza — facoltativa;
- KDPSAFE — facoltativa;
- due coppie indipendenti di filtri tassonomici principale/subordinato;
- ulteriori colonne non mappate.

La seconda voce di ciascuna coppia tassonomica dipende dalla prima. Nomi e valori provengono dal database, non da un catalogo hard-coded.

**OSSERVATO-EXE.** Regola importante: **le colonne non mappate restano visibili, modificabili ed esportabili**. I nomi mostrati nella UI restano quelli del file sorgente.

### 4.3 Editing del database

**OSSERVATO-EXE.** Funzioni osservate:

- ricerca parola;
- vai a ID;
- aggiungi parola clonando i metadati da una parola modello;
- elimina parola;
- salva modifiche;
- esporta database;
- validazione di ID duplicati;
- validazione di parola vuota o duplicata;
- nuova parola con ID, parola e nota di creazione nuovi, mentre gli altri dati sono clonati dal modello.

La nuova parola resta “in attesa” finché non viene salvata.

### 4.4 Modello persistente osservato

**OSSERVATO-EXE.** Il modulo storage usa SQLite e contiene una tabella `words` con campi osservati:

- `source_id`
- `word` (primary key)
- `years`
- `primary_decade`
- `category`
- `subcategory`
- `nostalgia`
- `kdp_safe`
- `used`
- `notes`
- `extra_json`

È presente anche una tabella `settings` key/value.

**MIGRAZIONE.** In Diez questi dettagli non implicano l'adozione di SQLite come seconda fonte di verità. Le capacità vanno mappate sul modello canonico Core e sul package `.diez`, preservando campi estesi/sconosciuti e ID stabili.

## 5. FILTRI

**OSSERVATO-EXE.** Il pannello FILTRI supporta:

- due coppie tassonomiche indipendenti;
- dipendenza del secondo campo dal primo nella propria coppia;
- valori derivati dal database;
- ulteriori filtri;
- rilevanza minima;
- `Solo KDPSAFE=YES`;
- ricerca parola nei risultati filtrati;
- azzeramento ricerca;
- esclusione di risultati.

Il builder contiene logica per tassonomia, periodi/decenni, categoria/subcategoria, rilevanza, KDPSAFE e stato di utilizzo.

## 6. GENERA

**OSSERVATO-EXE.** Parametri/funzioni osservati:

- numero puzzle;
- parole per puzzle;
- puzzle per blocco;
- **composizione per blocchi omogenei**;
- riutilizzo controllato quando le parole non bastano;
- generazione e anteprima;
- analisi delle parole generate;
- evidenzia parole riutilizzate;
- mostra solo parole riutilizzate;
- ricerca parola nei puzzle generati;
- editing della lista generata.

**OSSERVATO-EXE.** La lista generata può modificare parola, tema o descrizione; i metadati di origine appartengono invece alla schermata DATABASE.

**DIRETTIVA-PRODOTTO già consolidata in Diez.** Il controllo duplicati/riusi è **a livello dell'intero libro**. Se il progetto prevede 100 puzzle, tutti e 100 costituiscono un unico dominio di unicità. Una sostituzione resta chirurgica sul singolo puzzle/posizione, ma la validazione deve considerare tutti i puzzle del libro.

## 7. CONTROLLO

**OSSERVATO-EXE.** Il controllo evidenzia almeno:

- parole troppo lunghe;
- parole riutilizzate;
- duplicati/riusi generati;
- sostituzione contestuale della parola selezionata.

La UI specifica che i duplicati generati possono essere sostituiti senza bloccare la generazione.

### 7.1 Sostituzione contestuale

**OSSERVATO-EXE.** Il builder contiene una funzione di suggerimenti contestuali che cerca alternative non già usate nei puzzle generati e rispetta i vincoli disponibili nel database. Le stringhe osservate mostrano anche la regola `NOT_USED` per sostituire un riuso.

**DIRETTIVA-PRODOTTO.** In Diez:

- la modifica colpisce una sola parola di un solo puzzle;
- non esiste “sostituisci ovunque” implicito;
- prima di applicare la proposta, il sistema ricontrolla l'intero libro;
- se la proposta è diventata usata altrove nel frattempo, l'applicazione stale viene rifiutata;
- il controllo globale vale anche per libri da 100 puzzle o più.

## 8. ESPORTA

**OSSERVATO-EXE.** Sono presenti:

- esportazione liste;
- esportazione manifesto;
- esportazione database;
- XLSX;
- CSV;
- preset esplicito `SELF-PUBLISHING TITANS CSV`.

Il preset Titans è descritto staticamente come:

> un puzzle per colonna, parole verticali, minuscolo, UTF-8 BOM.

Le intestazioni sono `puzzle 1 ... puzzle n`; gli XLSX del profilo non devono aggiungere filtri o sfondi.

### 8.1 Contratto Diez già fissato

**DIRETTIVA-PRODOTTO.** L'export finale Word Search di Diez dovrà mantenere due scopi distinti:

1. **database XLSX completo Diez, reimportabile**, con i dati necessari alla continuità editoriale;
2. **handoff Self Publishing Titans**, disponibile almeno in:
   - XLSX;
   - CSV.

Il CSV Titans segue il file campione fornito dall'utente: puzzle per colonne, parole verticali, separatore virgola, UTF-8 BOM, senza righe tecniche Diez. Il padding vuoto presente nel campione non è considerato dato editoriale finché un test reale di import Titans non dimostri che è obbligatorio.

## 9. Funzionalità da preservare quando partirà la migrazione

Quando l'utente autorizzerà esplicitamente il lavoro Word Search, il confronto di parità deve includere almeno:

- import XLSX/CSV/TSV/TXT;
- mappatura flessibile delle colonne;
- preservazione colonne extra;
- ID stabili;
- ricerca e vai-a-ID;
- aggiunta per clonazione metadati;
- eliminazione e salvataggio controllato;
- filtri tassonomici dinamici a due coppie;
- rilevanza e KDPSAFE;
- composizione per blocchi omogenei;
- quantità puzzle, parole/puzzle, puzzle/blocco;
- riuso controllato quando esplicitamente consentito;
- anteprima della generazione;
- analisi globale riusi/duplicati;
- evidenziazione e filtro dei riusi;
- editing controllato della lista generata;
- controllo parole troppo lunghe;
- sostituzione contestuale;
- revalidazione whole-book al momento dell'applicazione;
- export database completo/reimportabile;
- export liste XLSX/CSV;
- manifesto;
- profilo Self Publishing Titans.

## 10. Cose da non copiare letteralmente

La migrazione recupera **capacità e filosofia**, non debito tecnico:

- niente database parallelo che diventi una seconda fonte di verità rispetto al Core;
- niente chiavi basate solo su nomi visibili se esistono ID canonici;
- niente navigazione globale piatta con tutte le sottofasi nel menu laterale;
- niente validazione locale che ignori gli altri puzzle del libro;
- niente export finale che perda il database ricco/reimportabile Diez.

## 11. Gate di ripresa

**STOP operativo.** Questa specifica può essere letta, raffinata e usata per evitare perdita di conoscenza, ma non autorizza nuove modifiche Word Search. Riprendere l'implementazione soltanto dopo un comando esplicito dell'utente equivalente a **“procedi con Word Search”**. Fino ad allora la priorità resta il percorso dei libri con immagini.