# Round 5.6.2 — Abilitazione `Accetta e congela i soggetti`

Status: **RISCONTRO FISICO / FIX IN IMPLEMENTAZIONE / NON CONSOLIDATO**

Data: 2026-08-25

Branch: `spike/uno-platform-ui`

## Riscontro fisico utente

Nella candidata Round 5.6.1 il pulsante `Accetta e congela i soggetti` può restare disabilitato anche quando la proposta soggetti è visibile.

Questo impedisce di congelare il piano e, di conseguenza, impedisce al nuovo gate 5.6.1 di arrivare correttamente al Prompt.

## Diagnosi

La UI inizializza `IsEnabled` usando `state.ProposalReady`, poi disabilita in modo incondizionato il pulsante a ogni evento `TextChanged` dei `TextBox` della proposta.

Questo lega l'abilitazione a un evento UI anziché al significato dello stato corrente. Un aggiornamento/refresh del controllo può quindi produrre una falsa condizione di `modifiche non salvate` anche se i valori visibili coincidono con la proposta persistita.

## Decisione 5.6.2

L'abilitazione deve essere derivata semanticamente e in modo deterministico:

1. la proposta persistita contiene esattamente `RequestedCount` soggetti;
2. i nomi sono non vuoti e distinti;
3. ogni elemento è semanticamente concreto tramite nome visibile o concetto canonico;
4. i valori correnti degli editor coincidono con la proposta persistita.

Solo una **modifica reale** rispetto ai valori persistiti deve disabilitare temporaneamente `Accetta e congela i soggetti` e richiedere `Salva modifiche alla proposta`.

Un mero `TextChanged` senza differenza semantica non deve disabilitare il pulsante.

La stessa regola di accettabilità deve vivere nel Core come policy condivisa, essere usata da `BuildState` e dalla Uno UI e avere una regressione automatica.

## Regression obbligatoria

Per una proposta BUILTIN valida (es. Halloween, 3 soggetti):

- `ProposalReady == true`;
- la policy condivisa `IsAcceptableProposal(...) == true`;
- la proposta resta accettabile fino a una modifica reale non salvata;
- dopo `AcceptProposal`, il piano deve risultare `Resolved` e il Prompt Pack deve compilare.

## Scope

Questo è un fix mirato **Round 5.6.2**. Non introduce la futura riorganizzazione UI 5.7 e non modifica il modello di proposta locale della Libreria Temi.
