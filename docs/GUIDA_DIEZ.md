# Diez Publishing Studio — Guida operativa

**Build di riferimento:** Uno Platform — candidata correttiva successiva alla prova fisica del 19 agosto 2026  
**Stato:** guida di lavoro. Una funzione nuova diventa **CONSOLIDATA** solo dopo prova fisica dell'app installata e conferma esplicita dell'utente.

---

## 1. Come leggere Diez

Diez è organizzato attorno al ciclo editoriale di un libro, non attorno ai singoli strumenti tecnici.

Percorso principale:

**Progetto → Tipo libro → Produzione → Controlli e revisione → Esportazione → Libri finalizzati**

Le funzioni cambiano in base al tipo di libro, ma il significato delle sei aree resta stabile.

### Progetto
Crea o apre il `.diez`, importa materiali, verifica l'anteprima, assegna a ogni materiale un ruolo editoriale e consulta la cronologia del progetto.

### Tipo libro
Stabilisce la famiglia editoriale e quindi strumenti guidati, controlli e formati di uscita pertinenti.

### Produzione
Costruisce il contenuto. Per i libri visuali usa quattro fasi:

1. **Definizione**
2. **Prompt**
3. **Produzione**
4. **Revisione**

`Scene e soggetti` appartiene logicamente a Produzione.

### Controlli e revisione
Raggruppa:

- **Testo principale** — il Master realmente destinato al libro;
- **Mappa contenuti + guida progetto** — struttura, entità, relazioni, regole e memoria del progetto;
- **Controllo coerenza** — trova problemi senza correggere il libro di nascosto.

### Esportazione
Decide cosa portare fuori da Diez: libro, tabelle, database, immagini, materiali e pacchetti di consegna.

### Libri finalizzati
Conserva le edizioni considerate pronte e gli output già prodotti.

---

# 2. Barra laterale e spazio di lavoro

La barra laterale contiene le sei aree principali. La candidata correttiva aggiunge un piccolo pulsante **« / »** per contrarla ed espanderla.

La contrazione della sidebar è una preferenza di interfaccia, non un dato editoriale del libro.

Quando una superficie contiene un editor/elenco e una vera anteprima, Diez usa un divisore trascinabile. Nella candidata correttiva questo vale almeno per:

- elenco materiali ↔ anteprima/ruolo materiale;
- controlli Candidate ↔ anteprima immagine in Vision.

Il resize non deve cambiare selezione, contenuto o stato del progetto.

---

# 3. Progetto e materiali

## Creare o aprire un progetto

1. Apri **Progetto**.
2. Usa **Nuovo progetto** oppure **Apri .diez**.
3. Scegli nome/percorso.
4. Controlla titolo, tipo libro e materiali.

## Aggiungere materiali

Sono equivalenti due percorsi:

- **Aggiungi materiali…** con selezione file;
- **drag & drop** di uno o più file nell'area materiali.

Entrambi devono usare la stessa importazione canonica e la stessa rilevazione duplicati.

Dopo un'importazione riuscita Diez deve:

1. selezionare il nuovo materiale;
2. mostrarne l'anteprima;
3. chiedere **Come vuoi usare questo materiale?**;
4. permettere un'istruzione specifica;
5. salvare il ruolo come dato strutturato nel progetto.

## Materiale del progetto vs database operativo

Diez distingue:

- **materiale del progetto**: documento, immagine, tabella, PDF, reference o altra fonte incorporata nel `.diez`;
- **database operativo**: dati strutturati interpretati come record da una funzione specifica, ad esempio il lessico Word Search.

Un XLSX può quindi essere materiale generale oppure database operativo: dipende dall'azione scelta, non dall'estensione.

## Anteprima

La superficie materiali usa l'engine di preview universale:

- immagini → anteprima grafica;
- TXT / Markdown / CSV / TSV → estratto leggibile;
- XLSX → primo foglio e righe campione;
- DOCX / ODT → primi paragrafi estratti;
- RTF → estratto testuale;
- PDF → verifica strutturale, dimensione e pagine/titolo quando rilevabili;
- ZIP / `.diez` → elenco dei file interni;
- altri binari → metadata, dimensione e firma iniziale.

**Principio:** un file importato non deve essere una scatola nera.

---

# 4. Come usare un materiale: decisione editoriale

Il file non determina automaticamente il suo uso. Un'immagine PNG può essere un asset finale, una reference d'identità oppure semplice ispirazione; un XLSX può essere un database canonico oppure solo uno schema.

## Immagini

Scelte previste nella candidata:

- **Inserisci nel libro così com'è** — asset editoriale diretto; non rigenerarlo automaticamente;
- **Modello / identità di un soggetto** — reference autorevole per un soggetto ricorrente;
- **Reference di stile** — tratto, resa, atmosfera, palette quando pertinente;
- **Reference di composizione** — inquadratura, disposizione, punto di vista;
- **Reference ambiente / sfondo**;
- **Replica molto fedelmente**;
- **Trasforma / reinterpreta**;
- **Modifica solo particolari specifici** — richiede un campo testo con ciò che può/deve cambiare e ciò che deve rimanere invariato;
- **Solo ispirazione** — riferimento libero, non imitazione stretta;
- **Solo archivio / non inviare all'AI**.

Per ogni materiale Diez conserva inoltre:

- policy d'uso AI;
- livello di fedeltà;
- istruzione specifica;
- eventuale scope/target in evoluzione.

## Testi e documenti

Ruoli editoriali utili:

- fonte autorevole;
- fonte da trasformare/riassumere;
- reference di stile/tono;
- reference di struttura/indice;
- autorità terminologica/glossario;
- testo originale da portare nel Master;
- archivio, mai inviare all'AI.

## Tabelle e dati

Ruoli utili:

- dataset canonico;
- database di una funzione libro;
- modello/schema;
- tabella di lookup;
- fonte da normalizzare/deduplicare;
- archivio, mai inviare all'AI.

## Materiali nei Prompt Pack

Solo i materiali il cui ruolo consente realmente l'uso AI vengono inseriti in `inputs/publisher/`.

Il manifest conserva per ciascun file:

- ID materiale;
- nome file;
- ruolo;
- istruzione;
- policy AI;
- fedeltà;
- scope.

`UNASSIGNED`, `NEVER_SEND` e gli asset diretti destinati al libro devono restare fuori dai Prompt Pack di generazione.

`PROMPT.md` riepiloga i materiali inclusi e ricorda al sistema AI che **il ruolo dichiarato è vincolante**: per esempio una reference di stile non autorizza automaticamente a copiare la composizione.

---

# 5. Undo / Redo e Cronologia progetto

Sono due concetti diversi.

## Undo / Redo locale

Serve durante la modifica di un controllo:

- **Ctrl+Z** = annulla modifica locale;
- **Ctrl+Y** = ripristina modifica locale;
- i pulsanti ↶ / ↷ della sidebar agiscono sul campo testuale attivo.

Questo livello è adatto a TextBox e, quando le grid editabili saranno completate, alle singole celle.

Un singolo carattere digitato **non deve creare un checkpoint dell'intero progetto**.

## Cronologia progetto

La cronologia registra stati significativi del lavoro editoriale.

Nella candidata include checkpoint manuali e checkpoint automatici per varie azioni importanti, fra cui:

- import/rimozione materiali;
- modifica ruolo di un materiale;
- emissione Prompt Pack;
- import Response;
- import Candidate immagine;
- review Vision;
- applicazione di una Candidate al libro.

In **Progetto → Cronologia progetto** puoi:

- creare un checkpoint;
- andare allo stato precedente;
- tornare allo stato successivo;
- scegliere un checkpoint e ripristinarlo.

Il ripristino non deve distruggere la cronologia. Se torni indietro e poi crei nuovo lavoro, il vecchio percorso resta un ramo storico consultabile invece di sparire silenziosamente.

---

# 6. Prompt Pack e Response: nomi canonici

Il nome di consegna deve rendere immediatamente riconoscibile progetto, data e rigenerazione.

Base:

`NomeProgetto_YYYYMMDD_vNNN`

Esempi:

- `MioLibro_20260819_v001_prompt-pack.zip`
- `MioLibro_20260819_v001_response.zip`
- seconda emissione nello stesso giorno: `MioLibro_20260819_v002_prompt-pack.zip`
- Response corrispondente: `MioLibro_20260819_v002_response.zip`

La versione aumenta quando viene emesso un nuovo Prompt Pack dello stesso progetto/data.

Il Prompt Pack contiene in `PROMPT.md` il **nome canonico atteso del Response**.

Se un provider rinomina il Response, Diez:

1. mostra un avviso sul nome diverso;
2. continua a verificare `project_id`, `prompt_pack_id`, Work Unit/versioni e manifest;
3. non rigetta un pacchetto tecnicamente valido solo per il filename;
4. conserva sia il nome provider sia quello canonico previsto.

**Il nome serve all'operatore; il manifest resta l'identità tecnica.**

---

# 7. Response, Candidate, approvazione e applicazione

Questi concetti non sono sinonimi.

- **Response**: ciò che torna dall'AI o da un processo esterno.
- **Candidate**: una versione proposta che Diez conserva e mostra per il controllo.
- **Approva**: la Candidate ha superato il controllo ed è accettabile.
- **Porta/Applica nel libro**: quella versione entra realmente nel contenuto editoriale del libro.

Flusso:

**Response → Candidate → Controllo → Approva → Porta nel libro**

Importare un Response non significa approvarlo e approvare una Candidate non deve sovrascrivere automaticamente il Master.

---

# 8. Produzione visuale e sicurezza delle fasi

La prova fisica precedente ha rilevato un loop grave quando si selezionava una fase non immediatamente successiva.

La candidata correttiva applica queste regole:

- la selezione iniziale di un Tab non deve richiamare la navigazione come se fosse un click dell'utente;
- un click deve produrre al massimo una navigazione;
- il callback è protetto da reentrancy;
- la fase visuale corrente è stato di sessione del progetto, non contenuto editoriale canonico;
- il vecchio `Visual.ActivePhase` viene letto solo per recupero compatibilità e poi rimosso;
- aprire un altro progetto non deve ereditare la fase del precedente;
- un vecchio `.diez` con stato anomalo non deve poter intrappolare il focus;
- la stessa protezione è usata per i Tab di **Controlli e revisione**.

Per Coloring il percorso resta:

1. Definizione;
2. Prompt;
3. Produzione;
4. Revisione;
5. Scene e soggetti come area collegata.

Saltare avanti può mostrare eventuali prerequisiti mancanti, ma **non deve mai generare un loop di focus/navigazione**.

---

# 9. Coloring Book

## Definizione

Definisci almeno:

- numero immagini;
- soggetti e ambientazione;
- stile;
- pubblico e difficoltà;
- spessore linee;
- complessità/densità;
- sfondo e spazio bianco;
- eventuale **Bold & Easy**;
- eventuale **Cozy**;
- Consistent;
- Scene/Soggetti se necessari.

Bold & Easy e Cozy sono parametri indipendenti, non semplici nomi di stile.

## Prompt

Diez compone il Prompt dalle decisioni strutturate. Il Prompt resta visibile e modificabile, ma le decisioni guidate sono la fonte canonica.

## Produzione

Puoi usare Prompt Pack oppure copia/incolla verso l'AI. Prompt a video e Prompt Pack devono derivare dallo stesso snapshot compilato.

## Revisione / Vision

La candidata aggiunge un divisore trascinabile fra controlli della Candidate e anteprima immagine.

Per Coloring controllare almeno:

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

# 10. Altre famiglie libro

## Raccolta immagini

Scopo editoriale → linguaggio visuale → soggetti/reference → Prompt/Prompt Pack → Candidate → controllo qualità/coerenza → approvazione → layout/descrizioni → export.

Non eredita automaticamente i vincoli Coloring come bianco/nero o Bold & Easy.

## Libro illustrato

Struttura → testo/scene → posizione illustrata → partecipanti/reference → Candidate → controllo narrativo/visuale → approvazione → placement → sequenza/layout → export.

## Romanzo / racconto

Bussola → fondamenta narrative → personaggi/mondo → Parti/Capitoli/Scene → Candidate testuali → revisione → Master → coerenza → finalizzazione.

Il contratto futuro prevede struttura realmente modificabile con aggiungi/elimina/sposta/dividi/unisci/rinomina.

## Saggio / manuale

Obiettivo/lettore → fonti → indice → piano contenuti → produzione → Candidate → review fattuale/editoriale → Master → finalizzazione.

## Quiz / trivia

Progetto → fonti/politica di verità → categorie/difficoltà → generazione strutturata → controllo ambiguità/duplicati/fatti → approvazione → export.

## Catalogo / raccolta dati

Cosa raccogliere → schema → fonti/provenance → raccolta → normalizzazione/dedup → QA → export.

## Cruciverba

Tema/lingua → parole → definizioni Candidate → controllo/approvazione → handoff griglia/Qxw → verifica → export.

---

# 11. Word Search

Riferimento funzionale invariato:

**DATABASE → FILTRI → GENERA → CONTROLLO → ESPORTA**

Il database deve adattarsi allo schema reale. Esempi diversi possono avere tassonomie completamente diverse; colonne inutili non devono essere imposte.

I filtri devono nascere dai dati disponibili e possono essere dipendenti, per esempio Categoria → Sottocategoria o Habitat → Zona.

La generazione deve controllare l'unicità **whole-book** quando i duplicati sono vietati. Una sostituzione deve essere chirurgica e rivalidata contro tutto il libro.

Il contratto finale delle grid prevede selezione celle, `Ctrl+C`, TSV multi-cella, editing inline, navigazione tastiera, colonne ridimensionabili/riordinabili/nascondibili e navigazione diretta dagli errori ai record.

Export importanti:

- formati puzzle;
- Self Publishing Titans quando applicabile;
- **Database completo XLSX**;
- **Database del libro XLSX**;
- materiali a corredo ZIP.

La parità completa delle grid dello storico WordSearchListManager resta da implementare/provare prima di consolidarla.

---

# 12. Esportazione e materiali a corredo

Il file del libro non è sempre l'unico output utile.

Secondo il tipo, la consegna può comprendere:

- file del libro;
- materiali utente pertinenti;
- asset AI approvati;
- database/tabelle;
- reference utili;
- manifest/provenienza;
- piano immagini/posizioni.

Le Candidate AI rifiutate non devono entrare automaticamente nel pacchetto finale.

`Materiali del libro · ZIP` è un output di consegna; il `.diez` resta il progetto di lavoro.

---

# 13. Freeze, finalizzazione e uscita

## Freeze

Fotografia intenzionale dell'edizione che si vuole consegnare.

## Libri finalizzati

Archivio delle edizioni/output considerati pronti, separati dal progetto che può continuare a evolvere.

## Uscita dall'app

- progetto modificato/non salvato → **Salva e chiudi / Esci senza salvare / Annulla**;
- progetto salvato → conferma uscita;
- progetto senza percorso → considerato non salvato.

---

# 14. Checklist prova fisica della candidata correttiva

Verificare in particolare:

1. la sidebar si contrae/espande con `« / »` e il main recupera spazio;
2. in **Progetto**, drag & drop di PNG/JPG/TXT/XLSX/ZIP importa i file;
3. lo stesso risultato si ottiene con **Aggiungi materiali…**;
4. il materiale appena importato viene selezionato automaticamente;
5. l'anteprima resta visibile dopo import/refresh;
6. il divisore materiali ↔ preview si trascina senza perdere selezione;
7. per un'immagine si può scegliere **Modifica solo particolari specifici** e il campo istruzione è obbligatorio;
8. ruolo e istruzione restano dopo salvataggio/chiusura/riapertura;
9. `Solo archivio / non inviare all'AI` non entra nel Prompt Pack;
10. un materiale `Reference` entra in `inputs/publisher/` e compare in `publisher_materials` del manifest;
11. `PROMPT.md` riepiloga il ruolo dei materiali inviati;
12. Ctrl+Z/Ctrl+Y funziona in un TextBox modificabile;
13. i pulsanti ↶/↷ agiscono sul campo attivo;
14. si crea un checkpoint, si modifica il progetto, si torna indietro e poi avanti;
15. un ripristino selezionato non cancella la cronologia;
16. Coloring: clic 1→3, 3→2, 2→4, 4→1 e Scene/Soggetti senza loop;
17. usare anche i pulsanti interni Avanti/Indietro delle fasi e verificare assenza di loop;
18. salvare/chiudere/riaprire un vecchio progetto che conteneva `Visual.ActivePhase`: nessun focus bloccato;
19. aprire subito un secondo progetto: non deve ereditare la fase del primo;
20. i Tab di **Controlli e revisione** cambiano liberamente senza loop;
21. in Vision il divisore controlli ↔ preview si può trascinare;
22. il Response reale già usato nelle prove precedenti continua a importarsi e mostrare anteprima;
23. nuovo Prompt Pack: nome `NomeProgetto_YYYYMMDD_v001_prompt-pack.zip`;
24. `PROMPT.md` richiede `NomeProgetto_YYYYMMDD_v001_response.zip`;
25. una rigenerazione/emissione successiva usa `v002`;
26. un Response correttamente rinominato importa senza avviso;
27. un Response valido ma rinominato dal provider mostra avviso filename ma continua la verifica del manifest;
28. Vision può approvare/rifiutare e la cronologia registra l'evento;
29. `Porta nel libro` resta separato da Approva e crea un checkpoint quando cambia il libro;
30. chiusura app con progetto salvato e non salvato continua a comportarsi correttamente.

Annotare qualsiasi comportamento diverso, anche se sembra soltanto estetico.

---

# 15. Cosa NON considerare ancora consolidato

Anche se descritto nelle specifiche, non dichiarare consolidato senza implementazione e prova fisica:

- redesign definitivo delle 4 pagine Coloring;
- nuovo schema definitivo delle decisioni del Prompt Compiler;
- Identity Anchor cross-batch completo;
- rigenerazione correttiva con identity lock completo;
- grid Word Search con parità totale dell'antesignano;
- import Word Search completamente adattivo allo schema con mapping visuale finale;
- generazione automatica `scene × varianti` finale;
- workflow guidati definitivi di Romanzo/Saggio/Quiz/Dati;
- grid editabili definitive con undo/redo cella-per-cella per ogni famiglia;
- cronologia automatica completa per ogni singola azione editoriale di tutte le famiglie;
- pacchetto editoriale finale completo per ogni famiglia.

---

# 16. Regola di consolidamento Diez

Una funzione passa attraverso quattro stati:

1. **proposta / in lavorazione**;
2. **tecnicamente verificata**;
3. **provata fisicamente nell'app installata**;
4. **CONSOLIDATA** dopo conferma esplicita.

CI, test automatici e costruzione dell'installer sono necessari, ma non sostituiscono la prova fisica dell'applicazione installata.
