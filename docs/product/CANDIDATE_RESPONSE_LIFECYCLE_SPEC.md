# Diez — lifecycle comune Candidate / Response / applicazione al libro

Status: **SPECIFICA ARCHITETTURA / UX — WORKING, NON CONSOLIDATA**

Data: 2026-08-18

Scopo: definire un ciclo comune per risultati AI e importati senza appiattire le differenze fra immagini, testo, puzzle, quiz e dati.

---

## 1. Principio

Un Response importato non è automaticamente contenuto finale.

La pipeline comune è:

`Prompt snapshot → Work Unit → Response → Candidate → Review → Approval → Apply to book`

Le famiglie possono saltare alcuni passaggi solo quando il profilo lo dichiara esplicitamente e il rischio editoriale è basso.

Default di Diez: **importare non significa applicare**.

---

## 2. Identità minima

Ogni Candidate deve poter essere ricondotta a:

- Project;
- Job / Prompt snapshot;
- Work Unit o unità editoriale equivalente;
- Candidate version;
- tipo output;
- provenance/origine;
- eventuale target editoriale;
- asset/file associati.

La UI non deve mostrare questi ID come navigazione principale, ma l'identità deve rimanere verificabile e diagnostica.

---

## 3. Stati concettuali

Vocabolario target, adattabile nel copy UI:

- `Imported` — ricevuto ma non ancora revisionato;
- `NeedsReview` — richiesti controlli/editorial review;
- `Rejected` — non idoneo;
- `Approved` — idoneo come Candidate;
- `Applied` — promosso nel contenuto canonico/placement;
- `Superseded` — esiste una versione successiva applicata o preferita;
- `Stale` — non più coerente con decisioni/contesto corrente;
- `Error` — ingest o validazione incompleta.

Non è necessario persistire esattamente questi nomi se i modelli Core correnti possiedono stati compatibili; conta la semantica.

---

## 4. Versioni

Una Work Unit può avere più Candidate.

Regole:

- non sovrascrivere una Candidate precedente;
- versione stabile e ordinabile;
- approvare v2 non cancella v1;
- applicare una versione registra quale versione è diventata sorgente del contenuto canonico;
- una nuova Response può aggiungere Candidate senza rompere le associazioni esistenti.

---

## 5. Provenance

Origini possibili:

- AI provider / Prompt Pack;
- API integrata;
- import manuale;
- materiale dell'utente trasformato in Candidate;
- modifica manuale derivata da Candidate;
- combinazione/editorial merge.

La provenance deve sopravvivere all'applicazione al libro.

---

## 6. Review comune

Ogni famiglia può montare validator/reviewer diversi, ma il risultato deve distinguere:

- required checks;
- warning;
- informative checks;
- non eseguito.

Un required check non eseguito non è PASS.

### Regola recovery

Una Candidate fallita può essere rivalutata dopo correzioni/modifiche. Un nuovo check completo può portarla a stato idoneo; non deve restare fallita per sempre solo per storico.

Lo storico delle verifiche può essere conservato separatamente.

---

## 7. Visual Candidate

Contenuto:

- asset immagine;
- descrizione;
- unità/slot;
- eventuale scene/subjects context;
- reference provenance.

Review:

- decodifica/formato;
- Vision required;
- family HARD rules;
- consistenza;
- qualità;
- quantità/placement.

Azioni:

- preview;
- approva/scarta;
- rigenera;
- confronta versioni;
- `Porta nel libro`.

L'import Response visuale fisicamente validato su Windows costituisce prova della pipeline di ingest/preview del caso testato, non della qualità semantica o di tutti i gate Vision.

---

## 8. Text Candidate

Contenuto:

- testo completo;
- target ContentNode/capitolo/scena/sezione;
- word count;
- note/provenance.

Review:

- editor manuale;
- confronto con versione/Master;
- continuità;
- copertura brief;
- stile/tono;
- fact/source review quando applicabile.

Azioni:

- salva modifica come nuova versione o derivata;
- accetta/scarta;
- combina manualmente;
- applica al Master.

Non scrivere direttamente nel Master all'import.

---

## 9. Word Search Candidate

Quando il lavoro Word Search sarà autorizzato:

Candidate possibile:

- puzzle completo;
- lista parole;
- titolo/tema;
- scena semantica;
- variante/periodo;
- provenance parole.

Review required:

- parole nel pool consentito quando la policy lo richiede;
- max length;
- tassonomie/scenario;
- variante temporale;
- unicità whole-book;
- conteggio parole;
- esclusioni/KDPSAFE.

Applicazione:

- chirurgica sul puzzle/unità;
- rivalidazione whole-book immediatamente prima del commit.

---

## 10. Cruciverba Candidate

Candidate possibili:

- parola;
- una o più definizioni;
- ruolo tematico;
- difficoltà.

Review:

- risposta non rivelata;
- ambiguità;
- lingua;
- duplicati/somiglianza;
- caratteri/lunghezza;
- compatibilità con handoff/griglia quando nota.

L'approvazione di una definizione non deve eliminare le alternative storiche se servono per revisione.

---

## 11. Quiz Candidate

Unità strutturata:

- domanda;
- opzioni;
- risposta corretta;
- spiegazione;
- categoria;
- difficoltà;
- fonte/provenienza.

Review:

- una sola risposta corretta quando previsto;
- supporto della risposta;
- ambiguità;
- distrattori plausibili;
- duplicati semantici;
- distribuzione difficoltà whole-book/blocco.

Applicazione al QuestionBank solo dopo review richiesta.

---

## 12. Data Candidate

Unità:

- record;
- campi;
- provenance;
- eventuale confidence/notes.

Review:

- schema;
- tipi;
- required fields;
- normalizzazione;
- duplicati;
- conflitti;
- fonte.

Applicazione può essere singola o batch, ma il batch deve produrre un report chiaro di righe accettate/rifiutate.

---

## 13. Stale e dipendenze

Una Candidate può diventare stale quando cambia un input rilevante:

- Prompt/decisioni HARD;
- target unit;
- scene participants;
- Bible;
- schema;
- filtri/whole-book set;
- fonti required.

Non tutto rende stale tutto.

Il profilo/capability deve dichiarare le dipendenze per evitare invalidazioni eccessive.

Esempio:

- rinominare una label UI non rende stale nulla;
- cambiare il POV canonico di una scena può rendere stale una Text Candidate di quella scena;
- cambiare una decade Word Search può rendere stale la lista di quel puzzle;
- cambiare line weight Coloring può rendere stale le Candidate visuali interessate.

---

## 14. Apply to book

L'applicazione è un atto editoriale esplicito.

Prima dell'applicazione:

1. Candidate esiste e identità valida;
2. target esiste;
3. Candidate non è stale oppure l'utente ha risolto il conflitto;
4. required checks sono PASS;
5. whole-book validators richiesti sono eseguiti;
6. viene registrata la provenance.

Dopo:

- il contenuto canonico punta/riferisce la Candidate/versione sorgente quando appropriato;
- la Candidate non scompare;
- una successiva versione può supersedere senza distruggere lo storico.

---

## 15. Response import comune

Il trasporto comune deve occuparsi di:

- leggere package/manifest compatibile;
- validare identità;
- importare asset/testo/dati;
- creare Candidate;
- produrre diagnostica;
- non applicare automaticamente il contenuto finale.

La specializzazione di famiglia inizia **dopo** che la Candidate è stata materializzata correttamente.

---

## 16. UI comune

Dopo import riuscito la UI dovrebbe portare l'utente nel reviewer pertinente:

- visuale → preview/Vision;
- testo → Candidate editor;
- quiz/dati/puzzle → tabella/review;
- più tipi nello stesso Response → elenco unità con filtri.

Mostrare sempre:

- cosa è arrivato;
- quante Candidate;
- quante hanno problemi;
- quali sono pronte;
- quali sono state applicate.

---

## 17. Undo e sicurezza

Applicare una Candidate deve creare un cambiamento reversibile/versionato, non una perdita distruttiva del contenuto precedente.

Per contenuti complessi preferire:

- nuova revisione;
- snapshot precedente;
- provenance;
- undo editoriale.

---

## 18. Acceptance test comuni futuri

Per ogni famiglia:

1. crea Prompt snapshot;
2. crea/importa almeno due Candidate per la stessa unità;
3. verifica versioning;
4. fallisce un required check;
5. corregge/rivaluta;
6. approva una Candidate;
7. verifica che non sia ancora applicata;
8. applica esplicitamente;
9. salva/riapre `.diez`;
10. verifica provenance/versione;
11. importa una Candidate successiva;
12. verifica che lo storico resti intatto;
13. modifica un input che la rende stale;
14. verifica blocco/avviso corretto;
15. test fisico installer prima del consolidamento.

---

## 19. Gate corrente

Questa specifica non modifica il comportamento dell'import visuale appena validato fisicamente e non autorizza refactor distruttivi dei modelli AI correnti.

Serve come contratto di convergenza per le famiglie future e per evitare importer/reviewer paralleli incompatibili.
