# Round 5.3 — Physical test log

Status: **IN RACCOLTA / NON CONSOLIDATO**

Data: 2026-08-24

Branch: `spike/uno-platform-ui`

## Progetto 10 — progetto riaperto

Scenario fisico riportato dall'utente:

1. apertura del progetto 10 esistente;
2. accesso alla fase Prompt;
3. richiesta di generazione del Prompt Pack;
4. Diez non genera il Prompt Pack e restituisce il seguente messaggio:

> Prompt non disponibile: Piano soggetti non risolto in Diez per la posizione 1. '3 immagini di soggetti per Halloween' descrive una serie, non un soggetto atomico. Prima del Prompt Pack Diez deve avere un soggetto concreto per ogni immagine. Usa Scene + soggetti strutturati oppure una futura proposta del planner AI interno; il renderer immagini non può scegliere i soggetti al posto di Diez.

### Interpretazione tecnica

Questo comportamento è coerente con il contratto Round 5.3 di semantic freeze:

- il residuo legacy `3 immagini di soggetti per Halloween` viene riconosciuto come descrizione aggregata di serie;
- il testo aggregato non viene promosso a `PRIMARY SUBJECT` di una singola Work Unit;
- il renderer immagini non riceve il compito di scegliere autonomamente i soggetti della serie;
- il Prompt Pack resta bloccato finché Diez non dispone di un soggetto concreto per la posizione richiesta.

### Stato della verifica

Esito classificato come **PHYSICAL OBSERVATION — EXPECTED ROUND53 GATE**.

Questo singolo esito non consolida Round 5.3. Restano da verificare almeno:

- comportamento equivalente su progetto nuovo con tema aggregato;
- prosecuzione corretta quando soggetti/scene concreti sono già presenti nello stato strutturato;
- generazione di un nuovo Prompt Pack reale con Work Unit atomiche;
- successivo Response reale e relativo import/review.

## Riferimenti candidata

Installer Round 5.3 tecnicamente verificato:

- Source SHA: `77cd6686034f91d84e8b3765df9f04382db975e4`
- Workflow run: `#32`
- Run ID: `32674072201`
- Artifact ID: `9502209022`
- Setup SHA-256: `1d52b19beaa36e698510bf04e2abecbd791226e4784bfd95bceb6d193463e1a2`

Round 5.3 resta **NON CONSOLIDATO** fino al completamento della validazione fisica prevista dalla specifica.
