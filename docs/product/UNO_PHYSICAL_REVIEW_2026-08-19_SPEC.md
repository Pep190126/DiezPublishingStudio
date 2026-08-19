# Uno Platform — revisione fisica UI del 19 agosto 2026

Status: **DIRETTIVA DI PRODOTTO / DA IMPLEMENTARE E VALIDARE FISICAMENTE**

Questa specifica registra le osservazioni raccolte dall'utente durante l'esplorazione dell'installer Uno. Non rende automaticamente consolidati i comportamenti: vale `SPEC_CONSOLIDATION_MEMO.md`.

## 1. Progetto: verifica reale dei materiali importati

La zona **Progetto** è approvata come impostazione generale, ma deve permettere di verificare visivamente cosa è stato importato.

Per ogni materiale selezionato deve esistere una preview appropriata al formato.

Obiettivo: l'utente deve poter rispondere alla domanda **“ho importato davvero il file giusto e Diez lo ha capito correttamente?”** senza uscire dall'app.

### Preview per famiglia di formato

- immagini: preview reale con proporzioni preservate;
- TXT/MD/RTF e documenti estraibili: testo/estratto leggibile;
- CSV/TSV/XLSX: intestazioni, righe campione, colonne e dimensioni;
- DOCX/ODT/PDF: preview/estratto strutturato quando tecnicamente disponibile, con metadata e pagine/struttura almeno diagnostici;
- ZIP: **elenco dei file interni**, cartelle, dimensioni e possibilmente preview del file selezionato nell'archivio senza estrazione distruttiva;
- file non renderizzabili: metadata, hash/dimensione/tipo e diagnostica sufficiente a verificarne l'identità.

Una preview fallita non deve eliminare né invalidare automaticamente il materiale importato.

## 2. Colori e focus dei TextBox

Il cursore/caret e gli indicatori di focus dei campi testuali devono essere chiaramente visibili.

Direzione cromatica richiesta:

- **Azzurro Borbonico** per caret/focus primario;
- coerente con la famiglia cromatica dello sfondo **Azzurro Napoli** usata nel brand/shell;
- contrasto sufficiente su fondo bianco;
- non cambiare il colore del testo digitato soltanto per ottenere un caret visibile: se WinUI non espone direttamente il caret brush, usare il livello di styling/templating corretto e validarlo fisicamente.

La risorsa attuale `#007FFF` è da considerare un placeholder tecnico finché la coppia Borbonico/Napoli definitiva non viene validata visivamente.

## 3. Layout a finestra massimizzata

Problema osservato: a finestra massimizzata resta spazio bianco inutilizzato sulla destra.

Regola:

- la sidebar mantiene la propria larghezza controllata;
- tutto lo spazio restante appartiene al workspace;
- eliminare `MaxWidth` rigidi sulle root dei workspace quando causano colonne strette e spazio morto;
- editor, grid e preview devono poter espandersi;
- sulle finestre strette il layout può rifluire verticalmente;
- sulle finestre larghe usare lo spazio per preview più grandi, griglie, editor affiancati e riepiloghi.

## 4. Sidebar e brand

La sidebar deve usare **Azzurro Borbonico** come sfondo.

Header/brand verticale centrato:

1. `Diez` — leggermente più grande dell'attuale, centrato;
2. simbolo `∞` — ingrandito, centrato, sotto Diez;
3. `Publishing Studio` — leggermente più piccolo, centrato, sotto l'infinito.

Evitare testo tecnico come `Uno Platform · workspace stabile` in posizione prominente nel brand finale.

## 5. Nuova navigazione laterale

La sidebar deve contenere soltanto queste macroaree:

1. **Progetto**
2. **Tipo libro**
3. **Produzione**
4. **Controlli e revisione**
5. **Esportazione**
6. **Libri finalizzati**

### 5.1 Produzione

Il contenuto di Produzione usa navigazione interna a tab/step, non nuove voci nella sidebar.

Per i libri visuali include le quattro fasi del percorso corrente; i nomi e la distribuzione definitiva dei quattro passi Coloring restano WORKING finché termina la revisione utente.

`Scene e soggetti` non deve essere una macrovoce laterale autonoma. Va riprogettato e collocato nel punto logico della **Produzione** in cui definisce il contenuto prima del Prompt.

### 5.2 Controlli e revisione

Raggruppa almeno le funzionalità oggi sparse come:

- Testo principale modificabile / Editable Master;
- Mappa contenuti + guida progetto / Content Graph + Bible;
- Controllo coerenza / Revision Candidate.

Anche qui usare tab o navigazione interna contestuale.

### 5.3 Principio generale

Quando più funzioni appartengono alla stessa macroarea, preferire tab/step interni invece di aumentare il numero di voci laterali.

## 6. Chiusura applicazione

Alla richiesta di chiusura:

- se il progetto ha modifiche non salvate: chiedere **se salvare prima di uscire**, con almeno `Salva e chiudi`, `Esci senza salvare`, `Annulla`;
- se il progetto non ha modifiche: chiedere soltanto conferma equivalente a **“Sei sicuro di voler uscire?”**;
- il concetto di `dirty` deve essere reale e coprire modifiche a scelte, testo, struttura, scene, materiali, Candidate/review e configurazione; non basarsi solo sul fatto che esista un path;
- un salvataggio fallito non deve chiudere automaticamente l'app.

## 7. Tab come pattern UI canonico

Le macroaree laterali restano poche e stabili; tab/step interni rappresentano le fasi o gli strumenti dello stesso dominio.

Esempi:

- Produzione → 4 fasi visuali o percorso specifico della famiglia;
- Controlli e revisione → Master | Mappa/Bible | Coerenza;
- Esportazione → Output libro | Materiali | Database/Handoff quando applicabile;
- Word Search → DATABASE | FILTRI | GENERA | CONTROLLO | ESPORTA nel workspace della famiglia.

## 8. Acceptance test UI futuro

La prossima build che implementa questa revisione deve essere provata almeno così:

1. massimizza la finestra: nessuna grande area bianca inutilizzata a destra;
2. ridimensiona: workspace rifluisce senza perdere controlli;
3. verifica brand/sidebar e colori;
4. tab Produzione e Controlli/Revisione navigabili senza perdita di stato;
5. importa almeno immagine, documento, tabella e ZIP e verifica preview/diagnostica;
6. ZIP mostra l'elenco interno;
7. caret/focus TextBox chiaramente visibile;
8. modifica qualcosa e chiudi → dialogo salvataggio;
9. salva e chiudi → nessuna perdita;
10. riapri senza modificare e chiudi → semplice conferma uscita.
