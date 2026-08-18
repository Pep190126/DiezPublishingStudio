# Uno Platform — UX review round 2

Status: **DIRETTIVA DI PRODOTTO / WORKING — DA VALIDARE FISICAMENTE SU INSTALLER WINDOWS**

Data: 2026-08-19

Questo documento raccoglie le osservazioni dell'utente dopo una nuova esplorazione fisica della build Uno. Le direttive qui sotto non rendono automaticamente consolidata la relativa implementazione: vale `SPEC_CONSOLIDATION_MEMO.md`.

## 1. Progetto e verifica dei materiali importati

La zona **Progetto** resta concettualmente valida, ma ogni materiale importato deve essere verificabile visivamente.

Obiettivo: l'utente non deve limitarsi a leggere il nome del file e confidare che l'import sia corretto.

Quando un materiale viene selezionato, mostrare un pannello di anteprima ad ampiezza utile che adatti il contenuto al tipo di file:

- immagini: preview reale, proporzioni preservate;
- TXT/MD/RTF e altri documenti testuali: testo/estratto leggibile;
- CSV/TSV/XLSX: intestazioni, prime righe/colonne e riepilogo schema;
- DOCX/ODT/PDF: anteprima o estratto leggibile; dove il renderer completo non sia disponibile, dichiarare chiaramente che si sta mostrando un estratto e non fingere una resa pagina completa;
- ZIP: elenco navigabile delle entry, cartelle, dimensioni e, quando possibile, preview del file interno selezionato;
- altri formati: almeno metadata, dimensione, tipo rilevato, hash e una diagnostica comprensibile di ciò che Diez ha incorporato.

Per ZIP e package non estrarre implicitamente contenuti nel progetto: l'elenco interno è prima di tutto una superficie di verifica.

L'anteprima non deve modificare né approvare il materiale.

## 2. Focus dei TextBox

Quando un TextBox riceve focus, il caret/cursore di inserimento e gli indicatori di focus/selezione devono essere chiaramente visibili usando l'**azzurro Borbonico / azzurro Napoli del brand**.

Il contrasto deve restare leggibile su fondo bianco. La correzione va applicata come stile/risorsa comune, non campo per campo.

## 3. Layout a finestra massimizzata

Il workspace principale deve occupare tutta la larghezza disponibile dopo la sidebar.

Da rimuovere come vincolo generale l'attuale `MaxWidth` che lascia una fascia bianca inutilizzata a destra.

Regole:

- sidebar a larghezza controllata;
- contenuto principale `Stretch`;
- margini interni ragionevoli, ma nessuna colonna centrale artificiosamente stretta;
- preview, griglie, editor e tabelle possono usare la larghezza guadagnata;
- i singoli componenti possono avere limiti locali solo quando migliorano la leggibilità di testo lungo, non come vincolo del workspace intero.

## 4. Colore sidebar e brand

La sidebar usa lo stesso azzurro Borbonico/Napoli del linguaggio visivo principale, non un blu più scuro che faccia sembrare la navigazione un'app separata.

Brand nella parte alta, centrato e disposto verticalmente:

1. `Diez` — leggermente più grande dell'attuale;
2. `∞` — più grande, centrato sotto `Diez`;
3. `Publishing Studio` — leggermente più piccolo, centrato sotto `∞`.

Non comprimere il brand nella stringa `Diez ∞ Publishing Studio`.

## 5. Navigazione globale

La sidebar deve contenere soltanto sei macrovoci:

1. **Progetto**
2. **Tipo libro**
3. **Produzione**
4. **Controlli e revisione**
5. **Esportazione**
6. **Libri finalizzati**

Non mostrare in sidebar:

- le quattro fasi della produzione;
- ogni singolo Tipo libro;
- Scene/Soggetti come macrovoce;
- Editable Master / Content Graph / Consistency come tre voci globali separate.

## 6. Produzione con tab contestuali

La macrovoce **Produzione** apre il percorso specifico della famiglia corrente.

Per i libri visuali, la schermata principale usa tab/step interni per le quattro fasi invece di quattro pulsanti laterali.

La struttura definitiva dei quattro passaggi Coloring resta da rivedere con l'utente; non hard-codare nomi finali finché quel lavoro non è concluso.

Lo stesso pattern vale per altre famiglie: tab/step nel contenuto principale quando esistono fasi correlate.

### Scene e soggetti

Scene e Soggetti appartengono a **Produzione**. Vanno riprogettati e inseriti nel punto del metodo in cui definiscono contenuto, identità e partecipazione prima della compilazione del Prompt.

Non devono restare una funzione tecnica isolata nella sidebar.

## 7. Controlli e revisione

La macrovoce **Controlli e revisione** raggruppa almeno:

- **Testo principale modificabile**;
- **Mappa contenuti + Guida progetto**;
- **Controllo coerenza**.

Queste funzioni vengono mostrate tramite tab o altra navigazione interna coerente.

Il profilo del Tipo libro può nascondere/disabilitare ciò che non è applicabile, ma la shell globale non cambia.

## 8. Esportazione

L'esportazione deve distinguere chiaramente:

- libro/edizione finale;
- materiali a corredo;
- database/dataset a corredo quando applicabile;
- handoff specializzati.

Per libri non solo testuali, deve essere possibile produrre insieme o separatamente i materiali utente e quelli AI approvati/generati necessari alla continuità editoriale.

Possibili voci/preset, dipendenti dalla famiglia:

- Documento/libro finale;
- **Materiali ZIP**;
- **Asset approvati ZIP**;
- **Database XLSX**;
- formati specializzati di famiglia;
- manifest/handoff.

I dettagli sono definiti in `FINALIZATION_OUTPUT_BUNDLE_SPEC.md`.

## 9. Word Search — griglie operative

La futura UI Word Search deve recuperare la filosofia dell'antesignano e non ridursi a ListBox + TextBox.

Requisiti:

- celle selezionabili;
- copia celle/selezioni tramite clipboard;
- scorciatoie da tastiera dove naturali (`Ctrl+C`, selezione, navigazione); 
- editing direttamente nella griglia nei pannelli in cui i dati sono editabili;
- colonne adattive allo schema importato;
- intestazioni persistenti/leggibili;
- generated-puzzle grid con righe o pannelli espandibili per visualizzare l'intero contenuto di ogni puzzle;
- selezione e modifica non devono rompere gli ID stabili;
- controlli whole-book restano autoritativi anche quando si edita una singola cella.

Dettagli in `WORD_SEARCH_GRID_INTERACTION_SPEC.md`.

## 10. Chiusura applicazione

La chiusura deve essere esplicita:

### Progetto con modifiche non salvate

Mostrare una richiesta che permetta almeno:

- **Salva ed esci**;
- **Esci senza salvare**;
- **Annulla**.

### Progetto già salvato e non modificato

Mostrare conferma semplice:

`Sei sicuro di voler uscire?`

con `Esci` / `Annulla`.

La decisione deve basarsi su vero dirty-state del documento, non sul fatto che esista o meno un percorso file.

Salvataggi automatici mirati non devono falsare lo stato: il sistema deve sapere se lo stato canonico corrente coincide con l'ultima persistenza riuscita.

## 11. Workflow leggibili per il publisher

Diez deve avere documentazione/workflow end-to-end per ogni Tipo libro, dall'apertura del progetto alla finalizzazione e all'export.

I workflow devono spiegare non solo **cosa cliccare**, ma **perché esiste ogni fase**, quando serve e quale dato produce per la fase successiva.

Riferimento: `BOOK_TYPE_END_TO_END_WORKFLOWS.md`.

## 12. Criterio di validazione fisica

La prossima build che implementerà questo round deve essere testata almeno su:

1. finestra normale e massimizzata;
2. brand/sidebar;
3. focus TextBox;
4. import e preview di immagine, documento, tabella e ZIP;
5. sidebar a sei macrovoci;
6. tab Produzione e Controlli/Revisione;
7. chiusura con progetto dirty e clean;
8. export con almeno un materiale a corredo;
9. navigazione casuale senza perdita dello stato.

Solo dopo conferma dell'utente i punti testati possono diventare CONSOLIDATI.