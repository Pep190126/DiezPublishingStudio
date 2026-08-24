# Round 5.5.1 — Validazione risposta planner e UX di import

Status: **PHYSICAL FINDING / FIX REQUIRED / NON CONSOLIDATO**

Data: 2026-08-24

Branch: `spike/uno-platform-ui`

## Evidenza fisica

Durante la prova installata di Round 5.5, nel percorso Piano soggetti Diez, dopo `Prepara proposta soggetti con AI` l'utente ha copiato il prompt intermedio del planner e lo ha incollato direttamente nel box `Risposta ricevuta`. Diez ha consentito di importarlo come Candidate testuale e di usare anche `Approva versione`, ma il pulsante `Crea Prompt Pack ZIP · Manuale` è rimasto disabilitato.

## Diagnosi

Il prompt `# DIEZ SEMANTIC SUBJECT PLANNER` è una **richiesta** da inviare all'AI, non una risposta valida del planner.

Nel box `Risposta ricevuta` deve entrare solamente la risposta dell'AI conforme al contratto JSON, per esempio:

`{"subjects":[{"display_name_it":"...","canonical_concept":"...","description_it":"...","canonical_description":"..."}]}`

Solo una Candidate che supera `TryValidateProposal` rende disponibile in Definizione una proposta soggetti verificabile/accettabile.

Il pulsante Prompt Pack resta correttamente disabilitato finché `planner.Required == true`, cioè finché non esiste una proposta valida accettata e congelata.

## Difetto di prodotto rilevato

La UI Round 5.5 permette però un errore evitabile:

1. il planner può importare come Candidate testo arbitrario, incluso il prompt stesso;
2. `Approva versione` resta disponibile anche sull'attività planner;
3. l'import non valida il contratto planner **prima** di creare la Candidate;
4. la UI non distingue abbastanza nettamente `Prompt da inviare all'AI` da `Risposta JSON ricevuta dall'AI`;
5. una Candidate planner non valida può quindi essere approvata genericamente pur non potendo mai sbloccare il piano soggetti.

## Regola Round 5.5.1

Quando l'attività selezionata è `Diez · Piano soggetti visuali` o una riconciliazione semantica correlata:

- il box deve essere etichettato chiaramente `Risposta JSON del planner`;
- `Importa come candidato` deve prima eseguire la validazione specifica del planner;
- se il contenuto non è JSON valido o non rispetta lo schema/cardinalità, l'import deve essere rifiutato senza creare Candidate;
- se il contenuto coincide o assomiglia al prompt planner, deve essere rifiutato con messaggio italiano esplicito: `Hai incollato il Prompt del planner. Eseguilo con l'AI e incolla qui la risposta JSON ottenuta.`;
- `Approva versione` deve essere nascosto o disabilitato per le attività planner;
- l'unico atto di accettazione del planner deve restare `Accetta proposta e congela i soggetti` in Definizione;
- una Candidate planner valida deve riportare automaticamente alla proposta visibile/modificabile;
- una Candidate planner non valida non deve modificare lo stato editoriale e non deve poter sembrare approvata.

## Percorso corretto con Round 5.5 attuale

1. `Prepara proposta soggetti con AI`;
2. `Copia Prompt attività selezionata`;
3. incollare quel Prompt in ChatGPT/Gemini/altra AI;
4. copiare **la risposta JSON dell'AI**;
5. incollare il JSON in `Risposta ricevuta`;
6. `Importa come candidato`;
7. non usare `Approva versione` per il planner;
8. tornare in Definizione;
9. verificare/modificare la proposta;
10. `Accetta proposta e congela i soggetti`;
11. solo allora il Prompt Pack immagini diventa disponibile.

Una Candidate errata già importata non richiede un nuovo progetto: la stessa Work Unit planner può ricevere una nuova versione corretta con il JSON valido.

## Regression criteria

Fallisce se:

- il prompt planner può ancora essere importato come risposta valida;
- testo non-JSON crea una Candidate planner;
- una Candidate planner non valida può essere approvata genericamente;
- `Approva versione` resta operativo sul planner;
- una risposta JSON valida non riporta alla proposta in Definizione;
- dopo accettazione valida il Prompt Pack resta bloccato.
