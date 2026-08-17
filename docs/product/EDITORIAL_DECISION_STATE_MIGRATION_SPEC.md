# Diez — stato canonico delle decisioni editoriali e migrazione UnoUiState

Status: **SPECIFICA DATI / MIGRAZIONE — WORKING, NON IMPLEMENTATA**

Data: 2026-08-18

Scopo: definire come trasformare le scelte utente dei futuri percorsi guidati in dati editoriali canonici senza perdere compatibilità con i `.diez` attuali e senza fare del Prompt la fonte di verità.

---

## 1. Stato attuale osservato

La Uno Preview conserva ancora molte impostazioni di superficie dentro `UnoUiState` tramite chiavi stringa e metodi `GetUiString` / `SetUiString`.

Questo è accettabile per lo spike e per preferenze puramente UI, ma non è il posto definitivo per decisioni editoriali che devono guidare:

- Prompt Compiler;
- Prompt Pack;
- Response identity;
- validatori;
- editor del libro;
- export;
- riapertura cross-frontend.

Alcune aree sono già più mature e passano da bridge Core/costrutti canonici, per esempio AI versions, Word Search e Cruciverba.

---

## 2. Regola: separare stato UI e stato editoriale

### Stato UI

Esempi appropriati per `UnoUiState` o equivalente:

- pannello aperto/chiuso;
- ultimo elemento selezionato;
- larghezza splitter;
- filtro locale della lista;
- tab visiva corrente;
- zoom preview.

### Stato editoriale canonico

Non deve vivere soltanto in `UnoUiState`:

- numero immagini realmente scelto;
- genere del romanzo;
- struttura del libro;
- decisione `Proponilo con AI`;
- soggetti Consistent;
- scene;
- fonti obbligatorie;
- quantità puzzle;
- `NoDuplicates`;
- schema dati;
- criteri di review;
- output contract.

Se una decisione cambia il Prompt o il libro, è editoriale.

---

## 3. Identità della decisione

Ogni decisione canonica deve avere almeno:

- `DecisionKey` stabile;
- `BookType` o capability owner;
- `Scope`;
- `Mode`;
- `Value` tipizzato/serializzabile;
- `UpdatedAt` opzionale;
- `Source` opzionale;
- `SchemaVersion`.

### `Scope`

Valori concettuali:

- Project;
- StructureNode;
- Unit;
- Entity;
- Scene;
- WholeBookPolicy.

La chiave visibile UI non deve essere usata come identità.

---

## 4. Modalità decisione

Enum semantico target:

- `Defined` — valore scelto consapevolmente;
- `Propose` — chiedere una proposta AI;
- `Derive` — derivare da materiali/stato progetto;
- `Later` — non deciso, non blocca ancora;
- `NotApplicable` — escluso esplicitamente.

### Regola Prompt

- `Defined` → il valore entra come istruzione/contesto;
- `Propose` → il Prompt chiede una proposta, non inventa che il valore sia già fissato;
- `Derive` → il Prompt/servizio usa i materiali disponibili e segnala insufficienza se necessario;
- `Later` → non imporre un valore;
- `NotApplicable` → non mostrare/compilare il concetto salvo diagnostica.

---

## 5. Valori tipizzati

Il sistema deve poter conservare almeno:

- string;
- integer/decimal;
- boolean;
- enum/choice;
- lista ordinata;
- riferimento a EntityId/SceneId/ContentId/MaterialId;
- oggetto strutturato;
- range;
- unità di misura.

Evitare di convertire tutto in stringhe perché il Prompt le usa come testo.

Il renderer Prompt trasforma i dati in testo; lo storage non deve perdere semantica.

---

## 6. Placeholder, suggerimento e default

Devono essere distinti.

### Placeholder

Solo UI. Non viene persistito come decisione.

### SuggestedDefault

Valore consigliato dalla famiglia. Diventa canonico soltanto dopo accettazione/azione dell'utente o policy esplicita.

### Defined value

Valore effettivamente scelto o confermato.

Questa distinzione elimina il rischio che `20 capitoli` o `50 immagini` entrino nel Prompt solo perché erano visualizzati all'apertura della pagina.

---

## 7. Dipendenze e visibilità

La definizione della decisione, non il dato persistito, dichiara condizioni come:

- mostra `Numero immagini` solo se `VisualSlots` attiva;
- mostra `Piano illustrazioni` nel Romanzo solo se abilitato;
- `DescriptionLength` ha senso solo se `CreateDescription = true`;
- `Bold & Easy` può essere forzato OFF da line weight incompatibile;
- `CitationStyle` appare solo se citazioni richieste;
- `VariantAxis` appare solo se il dataset espone un ruolo mappabile.

La visibilità non deve cancellare automaticamente il valore precedente: se una capacità viene temporaneamente disattivata, la migrazione deve definire se conservare il dato dormant o richiedere conferma.

---

## 8. Provenienza della decisione

Quando utile, una decisione può indicare origine:

- User;
- ImportedProject;
- DerivedFromMaterials;
- AiProposalAccepted;
- MigratedLegacy;
- SystemPolicy.

La provenance serve a capire perché un valore esiste e a gestire stale/recompute.

Non deve diventare rumore UI quotidiano.

---

## 9. Prompt snapshot e stale detection

Il Prompt snapshot deve registrare quali decisioni/versioni ha consumato.

Se una decisione a monte cambia dopo un override manuale del Prompt:

- il Prompt diventa `stale`;
- l'utente può confrontare e ricomporre;
- non sovrascrivere silenziosamente l'override.

Il Prompt Pack deve usare uno snapshot immutabile delle decisioni compilate per quel Job.

---

## 10. Strategia di migrazione da `UnoUiState`

La migrazione deve essere **additiva e reversibile** finché non è validata fisicamente.

### Fase 1 — inventario

Classificare ogni chiave attuale:

- UI-only;
- editoriale canonica;
- legacy/duplicata;
- diagnostica.

### Fase 2 — mapping

Per ogni chiave editoriale definire:

- DecisionKey target;
- tipo;
- Mode iniziale;
- conversione;
- fallback;
- conflitti con dati Core esistenti.

### Fase 3 — dual read

Il nuovo codice legge prima lo stato canonico e, se assente, può leggere la chiave legacy.

### Fase 4 — canonical write

Le nuove modifiche vengono scritte nel modello canonico. La chiave legacy non viene più considerata autoritativa.

### Fase 5 — cleanup differito

Rimuovere/ignorare definitivamente le chiavi legacy soltanto dopo:

- test round-trip;
- test su `.diez` precedenti;
- build installata;
- conferma fisica dell'utente.

---

## 11. Non duplicare dati Core già canonici

Prima di creare una nuova `EditorialDecision` verificare se il dato ha già un modello canonico migliore.

Esempi:

- BookType → entità canonica esistente;
- Scene → modello Scene/relazioni stabile;
- ContentNodes → struttura/contenuto;
- AI Job/Work Unit/Version → modelli AI Exchange;
- Materials → materiali canonici;
- Word Search puzzle/lessico → bridge Core dedicato.

Le decisioni devono **referenziare o configurare** questi modelli, non duplicarli in una seconda fonte di verità.

---

## 12. Schema concettuale minimo

Esempio puramente architetturale:

```text
EditorialDecision
  DecisionKey: "Novel.TargetWords"
  Scope: Project
  Mode: Defined
  ValueType: Integer
  Value: 70000
  Source: User
  SchemaVersion: 1
```

Esempio:

```text
EditorialDecision
  DecisionKey: "Novel.ChapterCount"
  Scope: Project
  Mode: Propose
  Value: null
```

Il Prompt quindi può dire "proponi una struttura adatta" invece di imporre un numero inventato.

---

## 13. Versionamento

Lo schema deve essere versionato indipendentemente dalle label UI.

Una modifica di copy non richiede migrazione.

Una modifica semantica sì, per esempio:

- valore combinato `Kawaii / Cartoon` separato in stile singolo;
- vecchio `Bold & Easy` trattato come stile trasformato in parametro HARD indipendente;
- campo struttura stringa trasformato in outline canonico.

Le migrazioni devono essere esplicite e testabili.

---

## 14. Acceptance test futuri

Prima di considerare la migrazione completa:

1. aprire un `.diez` precedente;
2. leggere correttamente le vecchie impostazioni;
3. salvare senza perdere sezioni sconosciute;
4. modificare una decisione nel nuovo percorso;
5. riaprire e ottenere lo stesso valore/mode;
6. generare Prompt coerente;
7. verificare che placeholder non scelti non entrino nel Prompt;
8. verificare `Propose`/`Derive`/`Later`;
9. verificare Prompt stale dopo modifica a monte;
10. verificare che Candidate/Response già presenti restino associati;
11. verificare che i dati Core esistenti non vengano duplicati;
12. prova fisica installer prima del consolidamento.

---

## 15. Gate corrente

Nessuna migrazione distruttiva viene autorizzata da questa specifica.

Finché i flow Coloring sono in revisione con l'utente:

- progettare il modello;
- inventariare le chiavi;
- preparare test;
- non spostare ancora in massa le impostazioni persistite;
- non cambiare il Prompt Compiler per consumare il nuovo schema.
