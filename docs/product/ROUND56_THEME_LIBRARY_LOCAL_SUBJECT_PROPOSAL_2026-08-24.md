# Round 5.6 — Libreria temi e proposta locale dei soggetti

Status: **IMPLEMENTATO IN CANDIDATA / TECHNICALLY_VERIFIED / NON CONSOLIDATO**

Data: 2026-08-24

Branch: `spike/uno-platform-ui`

## Decisione di prodotto

Il planner soggetti manuale introdotto nelle Round 5.4/5.5 è troppo tecnico per il normale flusso utente.

Nel percorso visuale ordinario Diez non deve chiedere all'utente di:

- copiare un Prompt intermedio;
- incollarlo in un provider AI;
- copiare una risposta JSON;
- conoscere uno schema JSON;
- importare/approvare Candidate tecniche del planner.

Il flusso visibile diventa:

**Tema → Proponi soggetti → verifica/modifica → Accetta e congela → Prompt → Prompt Pack**.

JSON, ID, stato canonico e serializzazione sono responsabilità interne di Diez.

## Libreria temi integrata

La candidata Round 5.6 introduce una libreria locale con pool ampi di soggetti, non triple predefinite.

Temi iniziali:

- Animali della giungla;
- Halloween;
- Natale / Christmas;
- Dinosauri;
- Spazio;
- Fattoria;
- Oceano / mare;
- Animali domestici;
- Veicoli;
- Fiabe / fantasy;
- Fiori / botanica;
- Inverno;
- Primavera;
- Pasqua;
- San Valentino;
- Custom.

Un tema non equivale a una lista fissa di N output. Ogni tema contiene un pool più ampio. Diez seleziona il numero richiesto di soggetti distinti considerando tema, quantità, stile/pubblico disponibili, vincoli utente ed esplicita rigenerazione.

A stato semantico invariato la proposta è stabile. **Rigenera proposta** cambia esplicitamente la selezione dal pool.

## Tema Custom

Scegliendo **Custom** compaiono:

- campo `Nome tema Custom`;
- decisione `Usa solo in questo progetto`;
- decisione `Salva anche nella libreria temi`.

L'espansione AI del tema resta prevista ma non viene simulata. Finché non esiste una API testuale diretta realmente configurata, il comando resta non operativo e chiaramente etichettato.

Quando verrà collegata una API, il suo compito sarà espandere internamente un tema Custom con parole/soggetti correlati e restituire dati strutturati direttamente a Diez. Nessun copy/paste o JSON sarà esposto all'utente.

## Inserimento Custom dentro qualunque tema

Decisione utente del 2026-08-24: anche l'elenco di parole/soggetti di un tema deve accettare inserimenti Custom.

Per ogni tema, BUILTIN o Custom, la UI espone:

- `Soggetto / parola personalizzata`;
- descrizione opzionale;
- `Solo in questo progetto`;
- `Salva nella libreria di questo tema`;
- azione `Aggiungi al tema`.

L'inserimento project-only modifica soltanto il pool disponibile nel progetto corrente.

L'inserimento archiviato viene copiato nella libreria locale riutilizzabile del tema e diventa disponibile nei progetti successivi.

Il salvataggio in libreria non è mai implicito.

## Stato canonico e Prompt Compiler

La proposta del tema non è ancora contenuto congelato.

Solo l'azione esplicita **Accetta e congela i soggetti** crea il normale `MultiSubjectProfile`, assegna nuovi `SubjectId` e rende i soggetti autoritativi per il Prompt Compiler.

Finché non esiste una proposta completa e accettata, il gate semantico esistente continua a bloccare il Prompt Pack.

Il vecchio `DiezVisualSubjectPlannerFrontendBridge` resta nel Core soltanto come infrastruttura/regressione di compatibilità per progetti e test precedenti; non viene più esposto nel normale workspace visuale.

## Produzione con AI

Le vecchie attività `Diez · Piano soggetti visuali` vengono nascoste dalla lista ordinaria di Produzione con AI.

Se il piano soggetti è irrisolto, Produzione con AI rimanda semplicemente a:

`Definizione → Tema → Proponi soggetti → controlla/modifica → Accetta e congela`.

Non suggerisce più di copiare Prompt planner o incollare JSON.

## API Custom

Round 5.6 lascia aperta l'architettura API ma non aggiunge un executor fittizio.

Il catalogo Core attuale dichiara ancora `SupportsDirectApi=false` per i provider integrati. Di conseguenza:

- temi BUILTIN: completamente locali;
- tema Custom alimentato manualmente: completamente locale;
- espansione AI del tema Custom: visibile come possibilità futura ma disabilitata finché non viene collegata/configurata una API reale.

## Persistenza libreria locale

La libreria utente dei temi viene salvata in LocalAppData sotto la configurazione Diez, separata dai file `.diez`.

Il progetto conserva invece:

- tema selezionato;
- eventuale tema Custom project-only;
- aggiunte project-only;
- proposta corrente;
- indice di rigenerazione;
- stato di accettazione.

## Regression gate

La regression Round 5.6 verifica tra l'altro che:

1. una proposta BUILTIN non crei job AI/planner;
2. Halloween, Natale e Giungla siano presenti nella libreria;
3. la cardinalità della proposta sia esatta e i soggetti distinti;
4. la proposta sia stabile senza `Rigenera`;
5. `Rigenera` possa cambiare combinazione dal pool;
6. un soggetto/parola Custom possa essere aggiunto anche a un tema BUILTIN;
7. un tema Custom senza pool non inventi soggetti e non generi planner copy/paste;
8. un tema Custom alimentato manualmente funzioni senza API;
9. l'accettazione congeli normali `SubjectId` e sblocchi le Work Unit immagini;
10. l'espansione API resti non operativa finché il catalogo non dichiara una vera API text diretta.

## Candidata Windows finale Round 5.6

Build pulita: nessuna workflow one-shot, nessun marker one-shot e nessuno script di applicazione Round 5.6 nel sorgente della candidata.

- Source SHA: `d76b8acb9634168a50dc4a9b895ec49d8a776014`
- Workflow: `Uno Windows Consolidation Candidate`
- Run: `#47`
- Run ID: `32723591633`
- stato: `TECHNICALLY_VERIFIED`
- Visual book gate: `success`
- Visual semantic regression: `success`
- Restore Windows runtime: `success`
- Publish Uno Windows x64: `success`
- Verify executable: `success`
- Build Setup EXE: `success`
- Smoke install/launch/uninstall: `success`
- Artifact upload: `success`
- Artifact ID: `9518756228`
- Artifact ZIP bytes: `94468442`
- Artifact ZIP SHA-256: `e80e9bb0a86b891ca66eb784b8ddc7453745a044d87f4326737c43f706d88aad`
- Setup bytes: `94997881`
- Setup SHA-256: `408c591432abb69488bea17a69b66f26b73b9b177f8b038b6cbf1fda648a798d`

ZIP e Setup scaricati sono stati verificati localmente: dimensioni e SHA-256 coincidono con CI/artifact metadata.

## Consolidamento

Round 5.6 resta **NON CONSOLIDATO** fino alla verifica fisica nell'app installata del percorso:

`seleziona tema → aggiungi eventualmente soggetti Custom → proponi → modifica eventualmente → accetta → Prompt → Prompt Pack`.

Test fisico prioritario:

1. selezionare Halloween e chiedere 3 soggetti senza passare da Produzione AI;
2. controllare la proposta e provarne la rigenerazione;
3. aggiungere una parola/soggetto Custom solo-progetto a Halloween;
4. aggiungere una seconda voce scegliendo `Salva nella libreria di questo tema` e verificarne la riusabilità in un progetto nuovo;
5. creare un tema Custom project-only, alimentarlo manualmente e produrre una proposta;
6. verificare che `Espandi tema con AI` resti chiaramente disabilitato/non simulato;
7. accettare la proposta e verificare che Prompt e Prompt Pack si sblocchino senza JSON o planner manuale.
