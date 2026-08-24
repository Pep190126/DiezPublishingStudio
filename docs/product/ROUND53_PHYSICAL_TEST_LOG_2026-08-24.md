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

## Progetto nuovo — stesso tema aggregato

Nuova osservazione fisica riportata dall'utente:

- creando un progetto nuovo con lo stesso tipo di richiesta aggregata sui soggetti, Diez restituisce lo stesso gate di `Piano soggetti non risolto` prima di generare il Prompt Pack.

### Interpretazione tecnica

Questa osservazione conferma la parità semantica che Round 5.3 doveva ottenere tra progetto legacy riaperto e progetto nuovo:

- il comportamento non dipende più dalla provenienza del progetto;
- sia lo stato legacy sia lo stato fresh convergono sullo stesso preflight quando il soggetto resta aggregato e non atomico;
- il renderer immagini non riceve in nessuno dei due casi la responsabilità di inventare i soggetti concreti.

Esito classificato come **PHYSICAL OBSERVATION — LEGACY/FRESH PARITY CONFIRMED FOR UNRESOLVED AGGREGATE SUBJECT**.

### Limite di prodotto ora evidenziato

La parità è corretta, ma non equivale a completamento del flusso Coloring/Immagini. Il test dimostra anche che Diez oggi sa soltanto riconoscere e bloccare il piano aggregato; non dispone ancora del planner semantico interno capace di trasformare, per esempio, `3 soggetti di Halloween` in tre soggetti concreti, validati e congelati prima del Prompt Compiler.

Il prossimo miglioramento funzionale non deve indebolire questo gate e non deve ripristinare la delega al renderer. Deve invece completare il percorso:

`decisione utente aggregata -> planner semantico Diez -> proposta soggetti concreti -> eventuale revisione utente -> stato canonico congelato -> Prompt Compiler -> renderer immagini`

Il planner non può essere simulato con mapping tematici hardcoded.

### Stato della verifica dopo i due test

Confermato fisicamente:

- progetto legacy riaperto: aggregate subject bloccato;
- progetto nuovo: aggregate subject bloccato allo stesso modo;
- parità legacy/fresh sul preflight: confermata.

Restano da verificare almeno:

- prosecuzione corretta quando soggetti/scene concreti sono già presenti nello stato strutturato;
- successiva implementazione del planner semantico interno per evitare che il normale caso d'uso resti bloccato;
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
