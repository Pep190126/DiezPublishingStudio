# Round 5.6 — Libreria temi e proposta locale dei soggetti

Status: **IMPLEMENTATO IN CANDIDATA / IN VERIFICA TECNICA / NON CONSOLIDATO**

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

Un tema non equivale a una lista fissa di N output. Ogni tema contiene un pool più ampio. Diez seleziona il numero richiesto di soggetti distinti tenendo conto almeno di tema, quantità, stile/pubblico disponibili, vincoli utente ed esplicita rigenerazione.

A stato semantico invariato la proposta deve essere stabile. **Rigenera proposta** cambia esplicitamente la selezione dal pool.

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

Non deve più suggerire di copiare Prompt planner o incollare JSON.

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

## Regression criteria

Round 5.6 deve fallire se:

1. una proposta BUILTIN crea un job AI/planner;
2. l'utente deve vedere o incollare JSON nel percorso ordinario;
3. Halloween/Christmas/Jungla sono triple fisse anziché pool;
4. la proposta non ha la cardinalità richiesta;
5. i soggetti proposti non sono distinti;
6. lo stesso stato senza `Rigenera` cambia casualmente proposta;
7. `Rigenera` non può cambiare combinazione;
8. un inserimento Custom non può essere aggiunto a un tema BUILTIN;
9. un inserimento Custom project-only viene salvato globalmente senza consenso;
10. un tema Custom senza pool inventa contenuti o crea un planner copy/paste;
11. un tema Custom con pool manuale sufficiente non può funzionare senza API;
12. l'accettazione non crea normali `SubjectId` congelati;
13. il Prompt Pack si sblocca prima dell'accettazione;
14. la UI Produzione AI continua a mostrare il planner manuale come percorso normale.

## Verifica tecnica

Il primo source SHA applicato per Round 5.6 è `2658bd11c707b088b6b89ede47752bb7a97f9cf9`.

Il primo run tecnico è `#45`, Run ID `32723087016`. Questo run non è la candidata fisica finale perché il branch contiene ancora la workflow/marker/script one-shot usati per applicare la patch.

La candidata finale deve essere ricostruita da sorgente pulito dopo la rimozione dell'infrastruttura one-shot.

## Consolidamento

Round 5.6 resta **NON CONSOLIDATO** fino alla verifica fisica nell'app installata del percorso:

`seleziona tema → aggiungi eventualmente soggetti Custom → proponi → modifica eventualmente → accetta → Prompt → Prompt Pack`.
