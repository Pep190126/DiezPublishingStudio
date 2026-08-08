# Diez Publishing Studio 1.0 — Guida pratica al collaudo pre-finale

Questa guida descrive **cosa fa ogni area della schermata, quando usarla, perché esiste e cosa osservare durante il test**.

## Il flusso mentale corretto

Usa Diez in questo ordine:

**1. Progetto → 2. Materiali → 3. Editable Master → 4. Content Graph / Bible → 5. Consistency Review / Revision Candidate → 6. Edizione / Preflight → 7. Publication Candidate → 8. Export / Handoff**

Non è obbligatorio usare ogni funzione in ogni progetto. Un coloring, per esempio, può usare soprattutto materiali immagine e ZIP originali; un libro testuale usa soprattutto Master, revisione, Freeze e DOCX.

---

## Barra superiore

### Nuovo progetto
**Cosa fa:** crea un nuovo file `.diez`.

**Quando usarlo:** all'inizio di un nuovo lavoro editoriale.

**Perché esiste:** il `.diez` è l'archivio principale del progetto. Conserva struttura editoriale, revisioni, metadati, Bible, stato di revisione, piano illustrazioni e materiali incorporati.

**Da verificare:** dopo la creazione deve comparire uno stato di conferma e il file deve poter essere chiuso e riaperto.

### Apri .diez
**Cosa fa:** riapre un progetto Diez esistente.

**Quando usarlo:** ogni volta che riprendi un lavoro già iniziato.

**Perché esiste:** serve a verificare che tutto ciò che hai fatto sia realmente persistito nel progetto e non esista solo nella sessione corrente.

**Da verificare:** chiudi Diez, riaprilo, apri lo stesso `.diez` e controlla che materiali, modifiche, decisioni e metadati siano ancora presenti.

### Importa materiali
**Cosa fa:** incorpora nel progetto documenti, tabelle e immagini supportati e, quando possibile, ne ricava testo e struttura.

**Quando usarlo:** quando aggiungi manoscritti, capitoli, documenti di riferimento, CSV/XLSX o immagini al progetto.

**Perché esiste:** Diez deve lavorare su copie incorporate senza dipendere dal fatto che il file originale resti nella stessa cartella del computer.

**Da verificare:** il materiale deve apparire nella sezione Materiali incorporati; un duplicato identico dovrebbe essere ignorato; spostare o cancellare successivamente il file sorgente esterno non dovrebbe rendere inutilizzabile il `.diez`.

### Rimuovi materiale
**Cosa fa:** elimina dal progetto il materiale selezionato e ripulisce gli elementi collegati che non hanno più senso.

**Quando usarlo:** quando un file è stato importato per errore o non deve più far parte del progetto.

**Perché esiste:** evita che il progetto mantenga contenuti, entità o collocazioni immagine orfane.

**Da verificare:** selezionare un materiale, rimuoverlo, salvare e riaprire; non devono riapparire riferimenti fantasma.

### Salva
**Cosa fa:** salva lo stato corrente nel `.diez`.

**Quando usarlo:** dopo modifiche importanti e prima di chiudere il programma.

**Perché esiste:** rende persistenti decisioni editoriali, modifiche al Master e stato del progetto.

**Da verificare:** dopo Salva, chiudi e riapri il progetto e controlla che lo stato sia identico.

### Edizione / Preflight
**Cosa fa:** apre il controllo finale dell'edizione: metadati, Edition Freeze, preflight e Publication Candidate.

**Quando usarlo:** solo quando il contenuto e le decisioni editoriali sono abbastanza maturi da preparare una consegna.

**Perché esiste:** separa il lavoro ancora modificabile dalla versione editoriale approvata che deve alimentare gli export professionali.

### Export / Handoff
**Cosa fa:** raccoglie tutti gli output modificabili e il Production Package.

**Quando usarlo:** quando devi passare il lavoro a Word, Publisher, Excel, Canva per i coloring o a un impaginatore esterno.

**Perché esiste:** Diez non vuole diventare un generatore di PDF/EPUB finali; deve consegnare materiale ordinato, verificabile e modificabile.

---

## Materiali incorporati

Questa lista mostra ciò che è entrato fisicamente nel progetto.

**Quando usarla:** seleziona un elemento ogni volta che vuoi controllare nome, tipo, origine, dimensione, hash, stato di incorporazione e anteprima.

**Perché è importante:** questa è la prova che Diez possiede una copia del materiale sorgente necessaria al progetto.

**Nel riquadro Dettaglio** dovresti vedere informazioni tecniche del file selezionato e, quando disponibile, una sua anteprima o snapshot testuale.

---

## Editable Master / Struttura editoriale

Questa è la parte più importante per un libro testuale.

La lista rappresenta capitoli/sezioni ricavati dai materiali. Il Master è la versione editoriale su cui Diez lavora; **gli originali importati non vengono sovrascritti**.

### Modifica Master
**Cosa fa:** modifica il testo del capitolo/sezione selezionato e registra una nuova revisione manuale.

**Quando usarlo:** per correzioni editoriali reali che devono entrare nella versione di lavoro.

**Perché esiste:** permette di evolvere il manoscritto senza distruggere la sorgente importata.

**Da verificare:** modifica una frase, salva, riapri; la frase deve restare modificata e il conteggio revisioni deve aumentare.

### Ripristina importato
**Cosa fa:** riporta il contenuto selezionato allo snapshot originariamente importato, registrando comunque il ripristino come nuova revisione.

**Quando usarlo:** quando una modifica al Master non ti convince e vuoi tornare alla base originale.

**Perché esiste:** il ritorno all'originale deve essere tracciabile, non cancellare la storia editoriale.

---

## Content Graph / Bible

Qui Diez prova a trasformare informazioni ricorrenti del testo in elementi controllabili: persone, luoghi o altre entità e relazioni.

### Conferma entità
**Cosa fa:** trasforma un'entità candidata in un elemento confermato e utilizzabile dalla Bible.

**Quando usarlo:** quando sei sicuro che il nome/elemento individuato rappresenti davvero qualcosa che deve essere seguito nel progetto.

**Perché esiste:** la Bible deve contenere solo informazioni accettate dall'utente, non tutte le ipotesi automatiche.

### Ignora entità
**Cosa fa:** elimina una candidatura irrilevante insieme ai riferimenti collegati che dipendono da essa.

**Quando usarlo:** per falsi positivi o elementi che non vuoi controllare editorialmente.

**Perché esiste:** evita di intasare la Bible con rumore.

**Da verificare:** conferma almeno una entità e ignorane un'altra; salva e riapri e controlla che le decisioni persistano.

---

## Consistency Review / Revision Candidate

Questa area segnala possibili contraddizioni e gestisce le decisioni editoriali. È importante distinguere **lo stato del problema** dalla **modifica del testo**.

### Segna rivisto
**Cosa fa:** indica che hai esaminato il problema, senza dichiararlo risolto.

**Quando usarlo:** quando hai controllato l'avviso ma vuoi ancora decidere cosa fare.

### Accetta eccezione
**Cosa fa:** registra che la differenza è intenzionale e non deve essere considerata un errore.

**Quando usarlo:** quando la storia richiede volutamente due valori diversi o la regola automatica non è applicabile.

### Segna risolto
**Cosa fa:** registra che il problema è stato risolto.

**Quando usarlo:** dopo aver corretto realmente la causa o quando hai verificato che non esiste più.

### Riapri
**Cosa fa:** riporta un problema precedentemente deciso allo stato aperto.

**Quando usarlo:** se scopri che la decisione precedente era sbagliata o incompleta.

### Crea proposta
**Cosa fa:** prepara un Revision Candidate separato dal Master.

**Quando usarlo:** quando vuoi che Diez prepari una possibile modifica per risolvere il problema senza cambiare subito il testo.

**Perché esiste:** una proposta automatica o assistita non deve modificare il manoscritto senza una decisione esplicita.

### Approva proposta
**Cosa fa:** approva il Revision Candidate, ma **non modifica ancora il Master**.

**Quando usarlo:** dopo aver confrontato PRIMA e DOPO nel riquadro Dettaglio e deciso che la proposta è corretta.

### Scarta proposta
**Cosa fa:** rifiuta la proposta senza modificare il Master.

### Applica approvata
**Cosa fa:** applica al Master la proposta già approvata.

**Quando usarlo:** solo dopo l'approvazione.

**Perché la sequenza è lunga:** `Crea → Approva → Applica` impedisce che una proposta venga confusa con una modifica già effettuata.

**Test consigliato:** crea una proposta, controlla PRIMA/DOPO, approvala e verifica che il Master non cambi; solo dopo Applica approvata il testo deve cambiare.

---

## Riquadro Dettaglio

È il pannello di spiegazione contestuale della selezione corrente.

- Se selezioni un **materiale**, mostra informazioni sul file.
- Se selezioni un **contenuto**, mostra testo e storia delle revisioni.
- Se selezioni una **entità**, mostra Bible e problemi collegati.
- Se selezioni un **problema**, mostra evidenze, decisioni e Revision Candidate con confronto PRIMA/DOPO.

**Quando usarlo:** praticamente prima di ogni decisione importante. La lista ti dice *che cosa esiste*; Dettaglio ti dice *perché* e *con quali conseguenze*.

---

## Edizione / Preflight

Questa finestra si usa quando vuoi trasformare il progetto di lavoro in una versione approvata per la consegna.

### Metadati edizione
Contiene titolo, sottotitolo, autore/creatore, lingua, editore, ISBN e descrizione.

**Quando usarlo:** prima del Freeze definitivo. Titolo e lingua sono necessari al preflight; l'ISBN è opzionale ma, se presente, deve essere valido.

### Crea Edition Freeze
**Cosa fa:** fotografa lo stato corrente di metadati, Master, Bible e piano illustrazioni.

**Quando usarlo:** quando ritieni che il progetto sia pronto per il controllo finale.

**Perché esiste:** serve un punto preciso e immutabile rispetto al quale dire “questa è l'edizione che sto approvando”.

**Importante:** se dopo il Freeze modifichi testo, metadati, Bible o piano illustrazioni, il Freeze diventa **SUPERATO**. È corretto: devi crearne uno nuovo.

### Esegui preflight
**Cosa fa:** controlla se il Freeze corrente è pronto per diventare una consegna.

**Quando usarlo:** dopo il Freeze e ogni volta che vuoi capire cosa impedisce il passaggio successivo.

**Come leggere i risultati:**
- `✓` = controllo superato;
- `!` = attenzione/non necessariamente bloccante;
- `✕` = problema bloccante.

### Crea Publication Candidate
**Cosa fa:** crea una copia editoriale immutabile collegata al Freeze corrente.

**Quando usarlo:** solo quando il preflight è READY.

**Perché esiste:** gli export professionali devono provenire da una versione precisa e approvata, non da un Master che potrebbe cambiare un minuto dopo.

**Importante:** se modifichi il progetto dopo la creazione, il candidate diventa **SUPERATO** e gli export controllati devono bloccarsi finché non rifai Freeze/preflight/candidate.

---

## Export / Handoff

### DOCX editoriale
**Usalo per:** Word, Publisher, un impaginatore esterno e, quando utile, importazione in altri strumenti.

Contiene il manoscritto modificabile e, se configurate, le immagini incorporate nelle posizioni editoriali previste. Non è un layout finale.

### CSV Master
**Usalo per:** contenuti strutturati, scambio semplice, elaborazioni tabellari e interoperabilità.

### XLSX Master
**Usalo per:** Excel e flussi tabellari che richiedono un workbook modificabile reale.

### Piano illustrazioni
**Usalo per:** libri illustrati. Associa un'immagine a una sezione/capitolo e definisce indicativamente posizione, larghezza e didascalia.

**Perché esiste:** chi impagina deve sapere *dove* l'illustrazione è prevista, pur restando libero di rifinire il layout finale.

### ZIP immagini originali
**Usalo per:** coloring e consegna degli asset originali.

Contiene **solo le immagini originali incorporate**, senza resize, ricompressione, modifica DPI, upscale o manifest aggiuntivi.

Per un coloring questo può essere l'output principale da portare in Canva o altro strumento visuale.

### Crea Production Package completo
**Usalo per:** consegnare un libro completo a un impaginatore.

Comprende DOCX, CSV/XLSX, immagini originali, metadati, piano illustrazioni, istruzioni e manifest con hash SHA-256.

**Quando usarlo:** al termine del lavoro, con Publication Candidate corrente.

**Perché esiste:** evita di consegnare manualmente una collezione disordinata di file e permette di verificare l'integrità di ciò che è stato inviato.

---

# Quattro prove consigliate

## A. Libro testuale semplice
1. Nuovo progetto.
2. Importa TXT/DOCX.
3. Modifica un capitolo nel Master.
4. Salva, chiudi e riapri.
5. Controlla eventuali entità/problemi.
6. Inserisci metadati.
7. Freeze → Preflight → Publication Candidate.
8. Esporta DOCX e aprilo in Word.
9. Prova a modificare nuovamente il Master: il candidate deve diventare superato e l'export controllato deve bloccarsi.

## B. Libro illustrato
1. Importa testo + PNG/JPEG.
2. Configura Piano illustrazioni.
3. Freeze → Preflight → Candidate.
4. Esporta DOCX.
5. Verifica che l'immagine sia nel documento nel punto atteso.
6. Esporta ZIP immagini e controlla che contenga gli originali separati.
7. Crea Production Package e controlla che entrambi siano presenti.

## C. Coloring book
1. Nuovo progetto.
2. Importa più immagini.
3. Esporta ZIP immagini originali.
4. Controlla che dentro ci siano **solo immagini**.
5. Prova il tuo normale flusso Canva con quello ZIP.

## D. CSV/XLSX
1. Importa o crea un progetto con contenuto strutturato.
2. Arriva a un Publication Candidate corrente.
3. Esporta CSV e XLSX.
4. Aprili rispettivamente in un editor/Excel.
5. Controlla ordine, testo, caratteri accentati e celle lunghe.

---

# Come segnalare un problema

Una segnalazione ideale può essere brevissima, ma includere:

1. **fase:** es. `Editable Master`, `Preflight`, `DOCX`, `ZIP immagini`;
2. **azione:** es. `ho cliccato Applica approvata`;
3. **atteso:** cosa pensavi dovesse succedere;
4. **ottenuto:** cosa è successo davvero;
5. se presente, **testo esatto dell'errore**.

Per problemi di avvio, dalla RC3 Diez registra anche un log diagnostico in:

`%LOCALAPPDATA%\Diez Publishing Studio\logs\startup-errors.log`

Non serve interpretarlo: basta allegarlo o incollarne il contenuto.
