# Diez Publishing Studio 1.0 RC1 — guida di prova pre-finale

Questa build va provata come prodotto reale, non come demo tecnica. Non serve verificare tutto in una sola sessione.

## 1. Installazione e progetto

- Installa la RC1 sopra la versione precedente e verifica che i tuoi file `.diez` restino intatti.
- Crea un nuovo progetto, chiudilo e riaprilo.
- Apri almeno un vecchio progetto `.diez` se ne hai uno disponibile.

## 2. Libro testuale

- Importa un DOCX/TXT/altro manoscritto reale.
- Controlla struttura, capitoli/sezioni ed Editable Master.
- Modifica un contenuto e verifica che l'originale importato resti disponibile.
- Prova Content Graph/Bible e almeno una decisione nel Consistency Review.
- Compila i metadati, crea Edition Freeze, esegui Preflight e crea Publication Candidate.
- Esporta DOCX, CSV, XLSX e Production Package e aprili con i programmi che usi davvero.

## 3. Libro illustrato

- Importa testo e immagini.
- Apri Piano illustrazioni e colloca immagini in più posizioni/larghezze.
- Crea Freeze/Candidate dopo aver terminato il piano.
- Apri il DOCX esportato in Word e verifica che immagini e didascalie siano modificabili/spostabili.
- Nel Production Package controlla che gli originali siano presenti separatamente in `assets/images/`.

## 4. Coloring / image-only

- Crea o apri un progetto con sole immagini.
- Usa `ZIP immagini originali` senza creare Publication Candidate.
- Verifica che lo ZIP contenga esclusivamente immagini, nell'ordine atteso, senza file accessori.
- Prova il caricamento delle immagini nel tuo flusso Canva se lo usi per i coloring.

## 5. Dati strutturati

- Esporta CSV e XLSX da un progetto con contenuti reali.
- Apri il CSV nel programma abituale e l'XLSX in Excel/LibreOffice.
- Controlla titoli, ordine, contenuto e testi lunghi.

## 6. Sicurezza del lifecycle

- Dopo un Publication Candidate corrente, modifica il Master, un metadato oppure il Piano illustrazioni.
- Verifica che Freeze/Candidate risultino superati e che gli export candidate-gated vengano bloccati.
- Crea un nuovo Freeze/Candidate e verifica che l'handoff torni disponibile.

## 7. Cosa annotare

Per ogni problema è sufficiente segnare:

- cosa stavi facendo;
- cosa ti aspettavi;
- cosa è successo;
- eventuale messaggio mostrato da Diez;
- se il problema è ripetibile.

La RC1 non aggiunge nuove funzioni durante la fase di prova. Le modifiche successive saranno correzioni e rifiniture guidate dall'uso reale.
