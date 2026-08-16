# Contratto UX — libri con immagini

Status: **DIRETTIVA DI PRODOTTO PER UNO**

Famiglie coperte:

- Coloring Book
- Raccolta immagini
- Libro illustrato

Questo documento separa la **filosofia comune** dalle regole specifiche della famiglia. Il riferimento funzionale dettagliato del Coloring è in `docs/legacy/DIEZ_COLORING_LEGACY_REFERENCE_SPEC.md`.

## 1. Principio: percorso guidato, non dashboard piatta

I libri con immagini devono essere costruiti attraverso un percorso editoriale guidato. Per Coloring il riferimento è una sequenza obbligatoria in quattro fasi:

`1/4 definizione → 2/4 Prompt → 3/4 produzione/Prompt Pack → 4/4 revisione/Vision`

Le fasi appartengono al **contenuto principale** e non alla navigazione globale.

La sidebar può restare, ma deve contenere **macrovoci**, non una lista piatta di ogni tipo di libro né le sottofasi della famiglia corrente.

### Struttura laterale

Regola già decisa:

- una macrovoce come **Tipo libro** apre/seleziona la famiglia;
- Coloring/Raccolta immagini/Libro illustrato ecc. sono opzioni dentro Tipo libro, non dieci voci laterali permanenti;
- `1/4`, `2/4`, `3/4`, `4/4` non sono quattro voci laterali;
- le fasi sono rese nel workspace con stepper/progresso e azioni Avanti/Indietro.

Le etichette definitive delle altre macrovoci verranno affinate durante la prova reale dell'installer. Possibili domini, non ancora prescrizioni di copy: Progetto/Home, Tipo libro, Materiali, Produzione con AI, Esporta/Finalizza.

## 2. Uso dello spazio

**DIRETTIVA-PRODOTTO.** Il layout della demo Uno va distribuito meglio e deve utilizzare la schermata disponibile.

Vincoli:

- evitare una colonna centrale stretta con grandi aree vuote;
- il workspace principale si espande con la finestra;
- campi lunghi (soggetto, ambiente, Prompt, descrizione, note) devono poter usare larghezza utile reale;
- controlli correlati possono essere organizzati in due o più colonne quando c'è spazio, tornando fluidamente a una colonna su finestre più strette;
- l'anteprima deve avere spazio sufficiente per valutare realmente l'immagine;
- scroll solo dove serve, senza trasformare ogni pannello in un piccolo riquadro scrollabile;
- niente nuove finestre per svolgere le quattro fasi: dialoghi di sistema sono ammessi per file picker/save.

## 3. Shell stabile e gerarchia

La gerarchia concettuale è:

1. **Progetto**
2. **Tipo libro**
3. **Percorso della famiglia**
4. **Elemento corrente** (immagine/scena/placement/versione)

La shell deve far capire sempre:

- quale progetto è aperto;
- quale Tipo libro è attivo;
- in quale fase del percorso ci si trova;
- quale immagine/scena/versione si sta guardando;
- se ci sono prerequisiti da completare prima di avanzare.

Non mostrare all'utente ID tecnici, WorkUnitId, hash, routing o session ID come navigazione primaria.

## 4. Area Anteprima come componente di prima classe

L'anteprima immagine non è un dettaglio cosmetico. È una superficie operativa comune ai libri visuali.

Deve poter mostrare la stessa immagine indipendentemente dalla provenienza:

- materiale importato dal computer;
- materiale già embedded nel `.diez`;
- paradigma/reference;
- Candidate AI;
- versione approvata;
- immagine già collocata nel libro.

### Comportamento minimo

- selezione asset → preview immediata;
- proporzioni preservate;
- fit `Uniform`, mai deformazione;
- caption utile (nome/posizione/stato), non metadata interni;
- placeholder chiaro se manca l'asset;
- errore decodifica non deve far perdere il record/materiale;
- preview rimane nella stessa finestra;
- un cambio selezione non modifica implicitamente approvazione/placement.

### Provenienza visibile

È utile distinguere in linguaggio umano:

- Materiale aggiunto
- Reference/Paradigma
- Proposta AI
- Approvata
- Nel libro

La provenienza non deve creare pipeline separate: l'asset canonico è sempre un materiale/risultato del progetto.

## 5. Scene

Scene è una capacità trasversale della produzione visuale quando necessaria, non una quinta fase e non una macrovoce globale obbligatoria.

Invarianti:

- `SceneId` stabile, non riciclabile;
- nome/numero/descrizione modificabili;
- scene attive/inattive;
- partecipazione `SubjectId + SceneId`;
- ambiente locale della scena prevale sul generico;
- Vision verifica i partecipanti quando la scena li definisce.

UX: la zona Scene deve apparire nel punto del percorso in cui definisce il contenuto prima della generazione del Prompt. La collocazione grafica precisa sarà validata con l'utente sull'installer; non rompere la sequenza principale per inserirla.

## 6. Filosofia comune alle tre famiglie

Tutte e tre condividono:

- quantità/posizioni visuali;
- soggetti e ambienti;
- optional multi-soggetto;
- Consistent;
- Scene quando applicabile;
- specifiche trim/aspect ratio/risoluzione;
- Prompt generato da profilo canonico;
- Prompt sempre modificabile prima della produzione;
- Prompt Pack / AI Exchange;
- materiali/reference;
- preview reale;
- versioni candidate;
- descrizione/metadata editoriale;
- Vision;
- approvazione esplicita;
- `Porta nel libro` separato dall'approvazione;
- asset checks whole-book;
- freeze/preflight/candidate/finalizzazione.

La famiglia determina i vincoli specifici, non la struttura infrastrutturale.

## 7. Coloring Book — specializzazione

Coloring aggiunge/impone:

- output line art binario nero/bianco;
- stile Coloring;
- line weight;
- difficoltà, complessità, densità, sfondo, white space;
- aree chiuse/colorabili;
- no tiny details quando richiesto;
- clean contours;
- no text/numbers;
- subject separation;
- Bold & Easy HARD indipendente;
- Cozy HARD indipendente;
- thin line → Bold & Easy OFF;
- Vision HARD coerente con questi parametri.

La preview deve mostrare sia materiale importato sia tavole AI generate con il Prompt Diez.

## 8. Raccolta immagini — stessa filosofia, profilo diverso

Raccolta immagini usa il medesimo percorso visuale ma **non eredita automaticamente i vincoli Coloring**.

Profilo adattato:

- uso editoriale;
- resa cromatica: colore, scala di grigi, B/N puro, monocromatico/altre opzioni Core;
- dettaglio;
- trattamento linee/contorno;
- stile rendering;
- sfondo;
- viewpoint;
- leggibilità soggetto;
- evita testo salvo richiesta;
- chiarezza editoriale;
- scala/inquadratura comparabili nelle serie quando richiesto;
- soggetto/ambiente/Consistent/Scene.

Esempi d'uso supportati dalla filosofia: figure didattiche, sequenze di esercizi/movimenti, immagini tecniche/manualistiche, illustrazioni editoriali, collezioni autonome.

## 9. Libro illustrato — stessa pipeline visuale dentro un libro misto

Libro illustrato condivide il profilo avanzato della Raccolta immagini, ma le immagini non sono una collezione scollegata.

Devono essere legate a:

- contenuto narrativo/editoriale;
- nodo/posizione del libro;
- eventuale scena;
- soggetti partecipanti;
- placement/illustrazione canonico.

La pipeline visuale rimane la stessa; la differenza è che il risultato approvato viene portato in una posizione editoriale del libro misto testo+immagini.

## 10. Prompt: filosofia legacy, sicurezza corrente

Il Prompt deve essere:

- generato da dati editoriali reali;
- leggibile/modificabile dall'utente;
- copiabile;
- rigenerabile senza perdere le scelte canoniche;
- specializzato per posizione/scena quando serve.

Il provider-facing visual prompt **non deve** contenere:

- WorkUnitId;
- SceneId/SubjectId come metadata tecnici, salvo contenuto semantico trasformato in linguaggio visuale;
- retry/session/routing;
- hash;
- nomi file tecnici;
- marcatori interni `ELEMENTO DIEZ`.

Prompt Compiler 3.6 resta autoritativo: ART DIRECTION sintetizzata + HARD locks, con scene-local environment prioritario.

## 11. Vision e approvazione

La quarta fase non è una semplice gallery con un pulsante Approva.

Per ogni Candidate:

- mostra immagine reale;
- mostra descrizione e stato;
- esegue/recepisce i gate Vision richiesti;
- nessun required check parte implicitamente PASS;
- HARD fail blocca;
- una verifica completa successiva può recuperare una candidate precedentemente fallita quando il problema è stato corretto;
- approvazione e applicazione al libro restano due atti distinti.

## 12. Whole-book asset safety

Prima della finalizzazione verificare sull'intero libro:

- materiale mancante;
- asset duplicato quando vietato/indesiderato;
- placement senza asset;
- asset estranei/non referenziati secondo la policy di pubblicazione;
- versione non approvata usata come finale;
- mismatch di quantità/posizioni;
- freeze stale dopo modifiche.

## 13. Acceptance test dell'installer visuale

Quando viene consegnata la prossima build da provare, il percorso Coloring deve essere testato almeno così:

1. crea/apri progetto Coloring;
2. imposta Tipo libro senza vedere dieci tipi nella sidebar;
3. verifica layout a finestra grande e ridimensionata;
4. fase 1: quantità, soggetto, ambiente, stile, HARD, specifiche;
5. definisci/varia Scene e partecipanti;
6. aggiungi materiale immagine e verifica preview;
7. fase 2: prepara Prompt, modifica testo, undo, copia;
8. fase 3: aggiungi reference e verifica preview; crea Prompt Pack; importa risposta AI;
9. verifica preview della Candidate AI;
10. fase 4: Vision, FAIL HARD bloccante, successivo PASS, approvazione;
11. `Porta nel libro` separato;
12. salva, chiudi, riapri `.diez` e verifica asset/preview;
13. finalizza e verifica package/export;
14. ripeti con click/typing/navigation casuale da “test del pianista”.

Solo dopo questa prova reale si considera la nuova UX visuale pronta come base per le altre famiglie.