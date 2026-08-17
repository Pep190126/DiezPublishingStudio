# Diez Uno — componenti riusabili per percorsi guidati

Status: **SPECIFICA UX / ARCHITETTURA — WORKING, NON CONSOLIDATA**

Data: 2026-08-18

Documenti collegati:

- `PROMPT_SYSTEM_ARCHITECTURE_SPEC.md`
- `BOOK_FAMILY_CAPABILITY_REGISTRY_SPEC.md`
- `BOOK_FAMILY_GUIDED_FLOWS_SPEC.md`

## 1. Scopo

Questa specifica definisce **componenti Uno riusabili** che potranno essere combinati dai diversi Tipi libro.

Non descrive ancora il layout definitivo delle quattro pagine Coloring: quello resta aperto alle note della prova fisica dell'utente.

Obiettivo: evitare che ogni famiglia costruisca da zero una nuova pagina per:

- scegliere valori;
- definire una struttura;
- editare scene;
- leggere una Candidate;
- importare un Response;
- approvare/applicare;
- esportare.

Il componente deve conoscere il proprio compito UX, non il Prompt Engine specifico della famiglia.

---

## 2. Principi comuni

Ogni componente deve rispettare:

- linguaggio editoriale, non tecnico;
- autosave o stato non salvato chiaramente visibile;
- nessuna perdita di dati con Avanti/Indietro;
- ID canonici nascosti salvo diagnostica;
- tastiera e focus prevedibili;
- dimensionamento fluido;
- campi lunghi realmente espandibili;
- messaggi di errore vicini all'azione che li ha generati;
- nessun `PASS` implicito nei controlli required;
- nessuna applicazione automatica di Candidate al contenuto canonico;
- distinzione visiva fra originale, dato canonico, proposta AI e versione approvata.

---

## 3. `GuidedWorkspaceShell`

Contenitore di un percorso famiglia.

Deve mostrare almeno:

- Tipo libro attivo;
- nome del percorso;
- passo corrente;
- stepper/progresso;
- eventuali problemi che impediscono di avanzare;
- `Indietro` / `Continua`;
- accesso a riepilogo e salvataggio.

Non deve trasformare ogni passo in una voce della sidebar globale.

### Stato

Ogni passo può essere:

- non iniziato;
- in corso;
- completo;
- completo con avvisi;
- bloccato;
- stale dopo modifica a monte.

Lo stato deve derivare dai dati canonici/validatori, non essere un semplice flag UI manuale.

---

## 4. `DecisionField`

Componente base per una decisione editoriale.

Supporta:

- valore;
- descrizione parlante;
- help contestuale;
- obbligatorietà;
- dipendenze;
- validator;
- stato della decisione.

### Modalità decisione

Dove applicabile:

- `Lo definisco io`;
- `Proponilo con AI`;
- `Derivalo dai materiali`;
- `Più avanti`;
- `Non applicabile`.

Il controllo del valore appare solo se la modalità lo richiede.

### Regola placeholder

Il placeholder non diventa mai valore canonico.

---

## 5. `DecisionGroupCard`

Raggruppa decisioni che rispondono alla stessa domanda editoriale.

Esempi:

- `Per chi stiamo creando questo libro?`
- `Come deve apparire la tavola?`
- `Quanto deve essere strutturato il testo?`

La card può avere riepilogo compatto quando completata e riapertura per modifica.

Non usare decine di card minuscole: il raggruppamento deve seguire il ragionamento dell'utente.

---

## 6. `LiveEditorialSummary`

Pannello di riepilogo vivo del progetto/passaggio.

Mostra in linguaggio umano:

- decisioni già fissate;
- decisioni delegate all'AI;
- decisioni da derivare;
- elementi mancanti;
- conflitti;
- quantità previste;
- stato del Prompt quando esiste.

Non mostra:

- hash;
- WorkUnitId;
- routing;
- JSON interno.

Su schermi larghi può essere una colonna laterale; su schermi stretti può diventare pannello espandibile.

---

## 7. `OrderedCollectionEditor`

Editor riusabile per liste ordinate:

- immagini/slot;
- capitoli;
- sezioni;
- domande;
- puzzle;
- record selezionati;
- scene.

Azioni comuni:

- aggiungi;
- elimina;
- duplica;
- sposta su/giù;
- drag & drop quando affidabile;
- rinomina;
- stato;
- ricerca/filtro;
- selezione multipla solo quando semanticamente sicura.

L'ordine deve essere canonico e persistito.

---

## 8. `OutlineTreeEditor`

Specializzazione per strutture gerarchiche.

Usi:

- Romanzo: Parti → Capitoli → Scene;
- Saggio/manuale: Parti → Capitoli → Sezioni;
- Libro illustrato: Capitoli/Sezioni → Nodi/Pagine;
- altri cataloghi strutturati.

Azioni:

- aggiungi sibling/child;
- elimina con conferma se contiene dati;
- rinomina inline;
- sposta;
- riordina;
- dividi/unisci quando supportato dal profilo;
- importa outline;
- confronta proposta AI;
- rinumerazione automatica separata dall'identità stabile.

Ogni nodo deve avere ID stabile anche se titolo/numero cambia.

---

## 9. `UnitInspector`

Pannello per l'elemento selezionato.

Il profilo della famiglia decide quali sezioni mostrare.

Esempi:

### Scena narrativa

- obiettivo;
- POV;
- luogo;
- tempo;
- partecipanti;
- beat;
- note;
- lunghezza indicativa;
- stato.

### Immagine/slot visuale

- scena;
- soggetti;
- inquadratura;
- reference;
- Prompt unità;
- Candidate corrente;
- placement.

### Quiz

- domanda;
- opzioni;
- risposta corretta;
- spiegazione;
- difficoltà;
- fonte.

Il componente non deve conoscere le regole di una famiglia: riceve una definizione di sezioni/campi dal profilo.

---

## 10. `EntityAndConsistencyEditor`

Editor comune per entità che richiedono identità stabile:

- personaggi;
- soggetti visuali;
- luoghi;
- concetti/terminologia quando applicabile.

Funzioni:

- elenco entità;
- scheda dettaglio;
- relazioni;
- note canoniche;
- proprietà Consistent;
- reference associate;
- ricerca.

Rinominare un'entità non deve rompere le associazioni.

---

## 11. `SceneEditor`

Componente condivisibile, con profilo adattabile.

Campi comuni:

- nome/numero visibile;
- descrizione;
- attiva/inattiva;
- ambiente/luogo;
- partecipanti;
- note.

Estensioni narrative:

- POV;
- tempo;
- beat;
- obiettivo.

Estensioni visuali:

- composizione;
- azione;
- relazione soggetti;
- ambiente visuale locale.

La partecipazione usa ID stabili, non nomi.

---

## 12. `PromptWorkbench`

Unica superficie Prompt per tutte le famiglie.

Mostra:

- riepilogo delle scelte usate;
- Prompt compilato;
- eventuale contesto unità;
- override manuale;
- stato `aggiornato/stale`;
- versione/snapshot.

Azioni:

- `Rigenera dalle scelte`;
- `Confronta modifiche`;
- `Ripristina compilato`;
- `Copia Prompt`;
- `Crea Prompt Pack`;
- in futuro `Invia via API`.

Il testo mostrato deve essere lo stesso snapshot usato dal trasporto.

---

## 13. `ResponseIngressPanel`

Componente comune di import Response.

Mostra:

- tipo Response atteso;
- ultimo Prompt Pack/snapshot collegato;
- azione importa;
- avanzamento/stato;
- esito comprensibile;
- diagnostica copiabile quando fallisce.

Non mostra per default ID tecnici, ma deve conservarli nel dettaglio diagnostico.

Dopo import riuscito apre o rende disponibile il reviewer appropriato alla famiglia.

---

## 14. `CandidateListAndPreview`

Superficie comune per Candidate visuali, testuali o strutturate.

Deve distinguere:

- Candidate;
- versione;
- origine;
- stato review;
- applicata/non applicata.

Azioni possibili secondo famiglia:

- apri;
- confronta;
- modifica;
- approva;
- scarta;
- rigenera;
- applica/porta nel libro.

L'approvazione non implica automaticamente applicazione.

---

## 15. `TextCandidateEditor`

Per Romanzo, Saggio/manuale, Libro illustrato e altri output testuali.

Funzioni minime previste:

- editor completo;
- undo/redo;
- cerca/sostituisci;
- conteggio parole;
- confronto con Master/versione precedente;
- note review;
- stato;
- salva come bozza;
- applica al Master esplicitamente.

Il testo importato rimane Candidate finché l'utente non lo applica.

---

## 16. `VisualCandidateViewer`

Componente già in parte presente nella direzione Uno.

Funzioni:

- preview grande;
- fit uniforme;
- caption umana;
- provenienza;
- Candidate/versione;
- Vision/review;
- reference vicine quando utile;
- confronto fra versioni;
- approva;
- `Porta nel libro` separato.

---

## 17. `StructuredCandidateTable`

Per Quiz, Word Search, Cruciverba, Catalogo dati e output tabellari.

Caratteristiche:

- colonne definite dal profilo/schema;
- editing cella controllato;
- validazione riga;
- validazione whole-set;
- filtri problemi;
- selezione del problema;
- sostituzione/rigenerazione chirurgica;
- provenance;
- applicazione esplicita.

Non deve duplicare il database canonico: le Candidate restano separate fino all'applicazione.

---

## 18. `ReviewIssuesPanel`

Pannello unificato dei controlli.

Ogni issue espone:

- severità;
- descrizione umana;
- elemento interessato;
- regola violata;
- azione possibile;
- stato.

Categorie:

- HARD/blocking;
- warning;
- informativo.

Un required check mai eseguito non è `PASS`: è `NOT CHECKED`/equivalente.

---

## 19. `ApplyToBookAction`

Azione comune ma semanticamente specifica.

Esempi:

- immagine → placement/slot;
- testo → Master del nodo;
- quiz → QuestionBank canonico;
- record → dataset canonico;
- puzzle → raccolta puzzle canonica.

Prima di applicare deve:

1. verificare identità dell'unità;
2. rieseguire i validator required;
3. verificare stale/conflict;
4. creare la nuova versione/stato canonico;
5. preservare provenance e Candidate di origine.

---

## 20. `ExportHandoffPanel`

Componente comune per export e tool esterni.

Il profilo espone i formati pertinenti.

Esempi:

- PDF/DOCX per long-form;
- asset + manifest per visuale;
- Titans CSV/XLSX;
- Qxw handoff;
- CSV/XLSX dati.

L'handoff esterno non sostituisce il progetto `.diez` come fonte di verità.

---

## 21. Responsive layout

Regola Uno:

- desktop largo: contenuto principale + riepilogo/inspector quando utile;
- desktop medio: due colonne solo dove leggibili;
- finestra stretta: una colonna, pannelli collassabili;
- niente larghezze massime troppo strette che lascino metà finestra vuota;
- preview e editor testo hanno priorità di spazio;
- liste/inspector possono usare split pane ridimensionabile in futuro.

---

## 22. Componenti da NON costruire come primitive comuni

Non generalizzare prematuramente:

- editor specifico Bold & Easy;
- controllo purezza B/N Coloring;
- editor distrattori Quiz;
- mapping colonne Word Search;
- tool di citazioni Saggio;
- compatibilità griglia Cruciverba.

Questi possono vivere come moduli di famiglia montati dentro primitive comuni.

---

## 23. Ordine consigliato quando partirà l'implementazione

Componenti a basso rischio e alto riuso:

1. `GuidedWorkspaceShell`;
2. `DecisionField` + `DecisionGroupCard`;
3. `LiveEditorialSummary`;
4. `OrderedCollectionEditor`;
5. `OutlineTreeEditor`;
6. `UnitInspector`;
7. `PromptWorkbench`;
8. `CandidateListAndPreview`;
9. `ReviewIssuesPanel`;
10. `ApplyToBookAction`.

L'ordine reale deve comunque seguire i percorsi approvati dall'utente, non un refactor fine a se stesso.

---

## 24. Gate corrente

Questa specifica può guidare progettazione e refactor preparatori, ma **non fissa ancora la composizione definitiva delle quattro pagine Coloring**.

Finché l'utente sta raccogliendo osservazioni dalla build fisica Uno, evitare modifiche di layout Coloring che potrebbero anticipare decisioni non ancora prese.
