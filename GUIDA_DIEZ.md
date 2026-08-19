# Diez Publishing Studio — Guida operativa

**Build di riferimento:** Uno Platform — candidata alla prova fisica del 19 agosto 2026  
**Stato:** guida di lavoro. Una funzione nuova diventa **CONSOLIDATA** solo dopo prova fisica dell'app installata e conferma esplicita dell'utente.

---

## 1. Come leggere Diez

Diez è organizzato attorno al ciclo editoriale di un libro, non attorno ai singoli strumenti tecnici.

Il percorso principale è:

**Progetto → Tipo libro → Produzione → Controlli e revisione → Esportazione → Libri finalizzati**

Le funzioni cambiano in base al tipo di libro, ma il significato delle sei aree resta stabile.

### Progetto

Qui si crea o apre il file `.diez`, si aggiungono i materiali sorgente e si controlla che l'import sia corretto.

Il `.diez` è il contenitore di lavoro del progetto: conserva dati editoriali, materiali incorporati, stato di produzione, Candidate AI, controlli e informazioni necessarie a riprendere il lavoro.

### Tipo libro

Qui si sceglie la famiglia editoriale. La scelta non è solo un'etichetta: stabilisce quali strumenti guidati, controlli e formati di uscita hanno senso.

### Produzione

È il luogo in cui si costruisce il contenuto del libro.

Per i libri visuali la produzione usa quattro fasi:

1. **Definizione**
2. **Prompt**
3. **Produzione**
4. **Revisione**

`Scene e soggetti` appartiene logicamente a questa area e serve a dare identità persistente a luoghi, personaggi e partecipanti.

Per Word Search, Cruciverba, Romanzo, Saggio, Quiz e Raccolta dati la produzione deve invece seguire il metodo specifico della famiglia.

### Controlli e revisione

Raggruppa tre superfici diverse:

- **Testo principale** — il contenuto editoriale effettivamente destinato al libro;
- **Mappa contenuti + guida progetto** — struttura, entità, relazioni, personaggi, fatti, regole e memoria del progetto;
- **Controllo coerenza** — individua problemi senza modificare automaticamente il libro.

### Esportazione

Qui si decide cosa portare fuori da Diez: libro, tabelle, database, immagini, materiali e pacchetti di consegna.

### Libri finalizzati

È la libreria delle edizioni considerate pronte e degli output già prodotti. Serve a distinguere una lavorazione in corso da una specifica edizione consegnata.

---

# 2. Progetto e materiali

## Creare un progetto

1. Apri **Progetto**.
2. Seleziona **Nuovo progetto**.
3. Scegli nome e posizione del `.diez`.
4. Aggiungi i materiali necessari.

## Aprire un progetto

1. Apri **Progetto**.
2. Seleziona **Apri .diez**.
3. Scegli il progetto.
4. Controlla materiali, titolo e tipo libro prima di continuare.

## Aggiungere materiali

Diez distingue due concetti:

- **materiale del progetto**: documento, immagine, tabella, PDF, reference o altra fonte incorporata nel `.diez`;
- **database operativo**: dati strutturati che una funzione specifica interpreta come record, per esempio il lessico di Word Search.

Un XLSX può quindi essere un semplice materiale di riferimento oppure il database di una funzione: dipende dall'azione scelta, non dall'estensione.

### Anteprima e verifica

Nella build candidata, in **Progetto** viene aggiunta una superficie di verifica dei materiali.

Comportamento previsto:

- immagini → anteprima grafica;
- TXT / Markdown / CSV / TSV → estratto leggibile;
- XLSX → primo foglio e righe campione;
- DOCX / ODT → testo estratto dai primi paragrafi;
- RTF → estratto testuale;
- PDF → verifica strutturale, dimensione, pagine/titolo quando rilevabili;
- ZIP / `.diez` → elenco dei file interni;
- altri binari → metadata, dimensione e firma iniziale.

Lo scopo è semplice: **un file importato non deve essere una scatola nera**.

---

# 3. Candidate, approvazione e applicazione

Questi tre concetti non sono sinonimi.

## Response

È ciò che torna da un'AI o da un processo esterno.

## Candidate

È una versione proposta che Diez conserva e mostra per il controllo.

Importare un Response **non significa** approvare il risultato.

## Approva

Significa: "questa Candidate ha superato il mio controllo ed è accettabile".

## Porta/Applica nel libro

Significa: "questa è la versione che deve entrare nel contenuto editoriale del libro".

Quindi:

**Response → Candidate → Controllo → Approva → Porta nel libro**

Questa separazione permette di conservare alternative e cronologia senza sovrascrivere il Master automaticamente.

---

# 4. Testo principale, Mappa e Coerenza

## Testo principale

È il Master modificabile. Per Romanzi, Saggi e libri con testo è la superficie che deve contenere la versione realmente destinata alla pubblicazione.

Gli originali importati non vanno confusi con il Master: restano materiali sorgente.

## Mappa contenuti + guida progetto

Serve a ricordare ciò che il libro "sa":

- struttura;
- capitoli e scene;
- personaggi;
- luoghi;
- relazioni;
- fatti;
- terminologia;
- regole di continuità;
- eventuali fonti e provenance.

Per un romanzo è simile a una Bible narrativa. Per un manuale è più vicina a una mappa di fonti, concetti, sezioni e terminologia.

## Controllo coerenza

Confronta ciò che è nel libro con le regole e la memoria del progetto.

Un problema rilevato non deve essere corretto di nascosto. Diez deve mostrarlo e lasciare la decisione editoriale all'utente.

---

# 5. Coloring Book

## Percorso consigliato

### 1. Definizione

Definisci almeno:

- numero immagini;
- soggetti;
- ambientazione;
- stile;
- pubblico e difficoltà;
- spessore linee;
- complessità/densità;
- sfondo e spazio bianco;
- eventuale **Bold & Easy**;
- eventuale **Cozy**;
- Consistent;
- Scene/Soggetti se il progetto ne ha bisogno.

Bold & Easy e Cozy sono parametri indipendenti, non semplici nomi di stile.

### 2. Prompt

Diez compone il Prompt a partire dalle decisioni strutturate.

Il Prompt resta visibile e modificabile, ma le decisioni guidate devono restare la fonte canonica del progetto.

### 3. Produzione

Puoi usare Prompt Pack oppure copia/incolla verso l'AI.

Il Prompt Pack e il Prompt mostrato a video devono derivare dallo stesso snapshot compilato.

### 4. Revisione

Importa il Response, controlla le Candidate e usa Vision/controlli obbligatori.

Per Coloring sono particolarmente importanti:

- bianco e nero corretto;
- contorni puliti;
- aree colorabili chiuse;
- niente micro-dettagli inutilizzabili;
- nessun testo o watermark;
- soggetti leggibili;
- anatomia/disegno plausibili;
- rispetto di Bold & Easy/Cozy quando richiesti;
- coerenza del soggetto quando Consistent è attivo.

Solo dopo l'approvazione una Candidate deve essere portata nel libro.

---

# 6. Consistency tra lotti diversi

La consistency non deve appartenere al singolo lotto.

Il modello previsto è:

**Soggetto persistente → profilo identità → Identity Anchor approvato → lotti successivi**

Esempio:

1. definisci il personaggio `Mia`;
2. generi il primo lotto;
3. approvi un'immagine che rappresenta correttamente Mia;
4. quella rappresentazione diventa un riferimento persistente del progetto;
5. un secondo lotto usa ancora `Mia`, lo stesso SubjectId e lo stesso Identity Anchor;
6. se una Candidate è sbagliata, scegli una rigenerazione correttiva mantenendo identità, scena e vincoli che non devono cambiare.

La vecchia linea Avalonia aveva già il concetto di **Da rifare** e di pacchetto contenente solo immagini mancanti/da rifare, con ID stabili. La nuova architettura deve usare quella buona idea aggiungendo un'identità del soggetto persistente tra batch diversi.

**Stato nella build candidata:** il percorso correttivo/identity-lock cross-batch è specificato, ma non va considerato consolidato finché non è implementato e provato fisicamente.

---

# 7. Raccolta immagini

Percorso consigliato:

1. definisci scopo editoriale della raccolta;
2. definisci linguaggio visuale e regole di serie;
3. prepara soggetti, eventuali scene e reference;
4. compila Prompt e Prompt Pack;
5. genera/importa Candidate;
6. controlla qualità e coerenza di serie;
7. approva le immagini corrette;
8. definisci layout/descrizioni;
9. esporta libro e materiali a corredo.

Una Raccolta immagini riusa molte capacità del Coloring, ma **non eredita automaticamente** vincoli come puro bianco/nero, aree colorabili o Bold & Easy.

---

# 8. Libro illustrato

Il Libro illustrato lega testo e immagini a posizioni editoriali precise.

Percorso:

1. crea struttura del libro;
2. definisci testo/scene;
3. collega ogni posizione illustrata a scena e partecipanti;
4. definisci eventuali Subject Profile e reference;
5. genera/importa Candidate;
6. controlla coerenza narrativa e visuale;
7. approva;
8. porta l'asset nella posizione del libro;
9. controlla layout e sequenza;
10. finalizza ed esporta libro + materiali.

Una buona immagine non è automaticamente l'immagine giusta: deve anche essere collegata alla scena e alla posizione editoriale corretta.

---

# 9. Romanzo / racconto

Percorso progettato:

1. **Bussola del romanzo** — genere, pubblico, promessa narrativa, tono;
2. **Fondamenta narrative** — punto di vista, tempo, struttura generale;
3. **Personaggi e mondo** — identità, relazioni, luoghi e regole;
4. **Struttura** — Parti → Capitoli → Scene;
5. **Produzione testo** — Prompt per scena/capitolo e Candidate testuali;
6. **Revisione** — confronta, modifica, approva;
7. **Porta nel Master**;
8. **Coerenza** — personaggi, cronologia, luoghi, fatti, voce;
9. **Preflight/finalizzazione**;
10. **Export**.

L'indice deve diventare una struttura realmente modificabile: aggiungi, elimina, sposta, dividi, unisci e rinomina.

**Stato:** il metodo è specificato; il workspace Uno long-form attuale è ancora più semplice del metodo finale.

---

# 10. Saggio / manuale

Percorso progettato:

1. obiettivo e lettore;
2. fonti e vincoli;
3. indice/sezioni;
4. piano contenuti per sezione;
5. produzione AI o manuale;
6. Candidate testuali;
7. revisione fattuale/editoriale;
8. terminologia, fonti, esempi, figure;
9. Master;
10. finalizzazione ed export.

Per questo tipo la coerenza non riguarda solo la prosa: deve riguardare anche fatti, terminologia, fonti, numeri, figure e riferimenti incrociati.

---

# 11. Word Search

Il riferimento funzionale resta il metodo storico:

**DATABASE → FILTRI → GENERA → CONTROLLO → ESPORTA**

## DATABASE

Importa il lessico/dataset.

Diez deve adattarsi allo schema realmente importato.

Esempio Nostalgic:

- parola;
- categoria;
- sottocategoria;
- anno;
- decade;
- nostalgia;
- KDPSAFE.

Esempio Animals:

- parola;
- classe;
- habitat;
- continente;
- dieta.

Se un progetto non usa anno o decade, quelle colonne e quei filtri **non devono apparire inutilmente**.

Le colonne extra devono restare preservate, modificabili ed esportabili.

## FILTRI

I filtri devono nascere dalle colonne/ruoli disponibili.

Le tassonomie possono essere dipendenti, per esempio:

- Categoria → Sottocategoria;
- Regione → Provincia;
- Habitat → Zona;
- Evento → Momento.

## Scene semantiche

Il Word Search può costruire puzzle attorno a scene riconoscibili.

Esempio Nostalgic:

`Pranzo di Natale × anni '60`

`Pranzo di Natale × anni '70`

`Primo giorno di scuola × anni '80`

Lo stesso meccanismo può essere usato fuori dalla nostalgia:

`Vita in fattoria × stagione`

`Cucina italiana × regione`

`Animali marini × habitat`

## GENERA

Definisci:

- numero puzzle;
- parole per puzzle;
- eventuali blocchi;
- criteri tassonomici;
- scenario/variante;
- regole di composizione.

L'unicità delle parole deve essere controllata a livello **whole-book** quando i duplicati sono vietati.

Una parola già usata in Puzzle 4 non deve ricomparire silenziosamente nel Puzzle 73 solo perché appartiene a un'altra decade o scena.

## CONTROLLO

Controlla:

- duplicati/reuse whole-book;
- numero parole;
- lunghezza;
- parole invalide;
- pool insufficiente;
- coerenza tassonomica/scena quando applicabile.

Una sostituzione deve essere chirurgica e rivalidata contro l'intero libro.

## Griglie

Il contratto finale prevede:

- celle selezionabili;
- copia con `Ctrl+C`;
- copia di più celle come TSV per Excel/Sheets;
- editing inline dove consentito;
- navigazione tastiera;
- colonne ridimensionabili/riordinabili/nascondibili;
- puzzle generati espandibili per vedere le parole;
- click su un problema che porta alla voce interessata.

**Stato nella build candidata:** la parità completa delle grid dell'antesignano è specificata ma non è ancora da considerare implementata/consolidata.

## ESPORTA

Formati importanti:

- CSV/XLSX puzzle nei formati previsti;
- Self Publishing Titans quando applicabile;
- **Database completo XLSX**;
- **Database del libro XLSX**;
- materiali a corredo ZIP.

### Database completo XLSX

Contiene il lessico canonico disponibile e una fotografia dei puzzle correnti.

### Database del libro XLSX

Contiene soltanto le parole realmente usate nel libro, con i metadata attualmente disponibili e i puzzle in cui compaiono.

Nella build candidata queste due uscite XLSX sono disponibili nella nuova area **Esportazione** per un progetto Word Search.

---

# 12. Cruciverba

Percorso:

1. definisci tema e lingua;
2. costruisci/importa parole;
3. genera o raccogli definizioni Candidate;
4. scegli/approva definizioni;
5. controlla duplicati e qualità;
6. prepara handoff verso il motore/griglia cruciverba previsto;
7. controlla risultato;
8. finalizza ed esporta.

La definizione AI è una Candidate: deve poter essere confrontata e approvata prima dell'uso.

---

# 13. Quiz / trivia

Percorso progettato:

1. definisci progetto e pubblico;
2. stabilisci fonti e politica di verità;
3. crea categorie/difficoltà;
4. genera domande strutturate;
5. Response con campi: domanda, opzioni, risposta corretta, spiegazione, categoria, difficoltà, fonte;
6. controlla ambiguità, duplicati e correttezza;
7. approva;
8. esporta.

Per un Quiz il controllo fattuale è più importante della semplice qualità stilistica della frase.

---

# 14. Catalogo / raccolta dati

Percorso progettato:

1. definisci che cosa raccogliere;
2. crea schema/colonne;
3. definisci fonti e provenance;
4. raccogli/importa/genera dati;
5. normalizza;
6. deduplica;
7. controlla campi obbligatori e tipi;
8. approva dataset;
9. esporta.

Il dataset deve restare strutturato: non va trasformato in un lungo testo libero solo perché passa attraverso un'AI.

---

# 15. Esportazione e materiali a corredo

Per un libro che non sia solo testo, il file del libro non è necessariamente l'unico output utile.

Il modello di consegna previsto comprende, secondo il tipo:

- file del libro;
- materiali utente pertinenti;
- asset AI approvati;
- database/tabelle;
- eventuali reference utili alla produzione;
- manifest/provenienza;
- piano immagini/posizioni quando applicabile.

Le Candidate AI rifiutate non devono entrare automaticamente nel pacchetto finale.

## Materiali del libro · ZIP

Nella build candidata la nuova area **Esportazione** offre un ZIP con:

- materiali utente;
- asset AI approvati che Diez riesce a ricondurre alle versioni AI;
- `MANIFEST-MATERIALI.tsv`.

Il `.diez` completo resta il progetto di lavoro; il ZIP materiali è un output di consegna, non un sostituto del progetto.

---

# 16. Freeze e libro finalizzato

## Freeze

Il Freeze è una fotografia intenzionale dell'edizione che si vuole consegnare.

Serve a evitare il problema:

> "Ho continuato a modificare il progetto: quale versione avevo realmente esportato?"

## Libri finalizzati

Dopo i controlli e il Freeze, gli output dell'edizione possono essere registrati nella libreria dei libri finalizzati.

Il progetto può poi continuare a evolvere per una nuova edizione senza confondere i vecchi output con quelli nuovi.

---

# 17. Uscita dall'app

Contratto della build candidata:

- se il progetto canonico risulta modificato/non ancora salvato → **Salva e chiudi / Esci senza salvare / Annulla**;
- se il progetto risulta salvato → **Sei sicuro di voler uscire?**;
- un progetto ancora senza percorso di salvataggio viene trattato come non salvato.

La prova fisica deve verificare in particolare chiusura con X della finestra dopo modifiche reali.

---

# 18. Cosa provare in questa build

Questa iterazione è una **candidata al consolidamento**, non è automaticamente consolidata perché il CI è verde.

Verificare fisicamente:

1. la sidebar contiene soltanto le sei sezioni principali;
2. il brand è centrato e leggibile;
3. la sidebar usa l'azzurro Diez;
4. massimizzando la finestra non rimane una fascia bianca inutilizzata a destra;
5. il focus di un TextBox è chiaramente visibile con bordo azzurro marcato;
6. in Progetto, selezionando un'immagine, compare l'anteprima reale;
7. selezionando TXT/CSV/XLSX/DOCX/PDF, compare una verifica leggibile/strutturale;
8. importando uno ZIP, si vede l'elenco dei file contenuti;
9. per un libro visuale, Produzione presenta tab per Definizione, Prompt, Produzione, Revisione e Scene/Soggetti;
10. Controlli e revisione presenta tab per Master, Mappa/Guida e Coerenza;
11. l'import Response che era già funzionante continua a funzionare senza regressioni;
12. in Esportazione si riesce a creare `Materiali del libro · ZIP`;
13. per Word Search compaiono `Database completo · XLSX` e `Database del libro · XLSX`;
14. i due XLSX si aprono correttamente e contengono dati coerenti;
15. chiudendo un progetto salvato compare la conferma uscita;
16. chiudendo un progetto non salvato compare la scelta di salvataggio.

Annotare qualsiasi comportamento diverso, anche se sembra solo estetico: la prova fisica è ciò che decide il consolidamento.

---

# 19. Cosa NON considerare ancora consolidato

Anche se descritto in questa guida o nelle specifiche, non dichiarare consolidato senza implementazione e prova fisica:

- redesign definitivo delle 4 pagine Coloring;
- nuovo schema definitivo delle decisioni del Prompt Compiler;
- Identity Anchor cross-batch completo;
- rigenerazione correttiva con identity lock completo;
- grid Word Search con parità totale dell'antesignano;
- import Word Search completamente adattivo allo schema con mapping visuale finale;
- generazione automatica `scene × varianti` finale;
- workflow guidati definitivi di Romanzo/Saggio/Quiz/Dati;
- pacchetto editoriale finale completo per ogni famiglia.

---

# 20. Regola di consolidamento Diez

Una funzione passa attraverso quattro stati:

1. **proposta / in lavorazione**;
2. **tecnicamente verificata**;
3. **provata fisicamente nell'app installata**;
4. **CONSOLIDATA** dopo conferma esplicita.

Il CI, i test automatici e il fatto che l'installer venga costruito sono necessari, ma non sostituiscono la prova fisica dell'applicazione installata.
