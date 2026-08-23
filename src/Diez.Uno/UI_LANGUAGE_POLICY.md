# Diez Uno — linguaggio dell'interfaccia

## Regola HARD

**L'italiano è la lingua dell'interfaccia utente e deve essere visibile a video in ogni punto in cui Diez mostra testo all'utente.**

Questo vale per:

- etichette;
- spiegazioni;
- descrizioni dinamiche;
- pulsanti e azioni;
- messaggi di stato;
- errori;
- controlli Vision;
- descrizioni di opzioni e impostazioni;
- anteprime descrittive rivolte all'utente.

L'interfaccia parla in italiano semplice, ma **non traduce per forza i termini che gli utenti conoscono e cercano già con il loro nome comune**.

In pratica:

- le frasi, le spiegazioni, gli errori e le azioni devono essere chiare e naturali in italiano;
- i nomi degli stili restano nella forma con cui sono comunemente conosciuti: `Kawaii`, `Cartoon`, `Chibi`, `Cottagecore`, `Clean Line Art`, `Art Nouveau`, `Manga`, `Anime-inspired`, `Steampunk`, ecc.;
- termini AI/editoriali ormai d'uso comune possono restare tali quando sono più riconoscibili della traduzione: `Prompt`, `Prompt Pack`, `Provider AI`, `Vision`;
- i parametri consolidati del framework conservano il proprio nome: `Bold & Easy`, `Cozy`, `Consistent`;
- attorno a questi termini Diez deve sempre spiegare in italiano semplice cosa fanno;
- i valori interni persistiti non vengono cambiati solo per ragioni di presentazione.

## Confine UI → stato canonico → Prompt

Il testo italiano mostrato a video **non è sorgente del Prompt** e non deve essere copiato o tradotto letteralmente nel Prompt provider-facing.

La pipeline obbligatoria è:

`UI italiana → significato/intento canonico strutturato → Prompt Compiler → prompt engineering provider/model-specifico`

Quindi Diez deve comprendere semanticamente una scelta UI e compilarne il significato operativo. Una label, una descrizione o una frase mostrata all'utente può essere riscritta, sintetizzata, espansa, gerarchizzata o trasformata in vincoli positivi/negativi/HARD nel Prompt, purché il significato editoriale scelto dall'utente venga preservato.

**È vietato usare come strategia generale:**

`testo italiano UI → traduzione inglese quasi letterale → Prompt`.

Il prompt deve essere scritto come prompt engineering efficace per il tipo di contenuto, il provider, il modello, il formato di output e la Work Unit corrente.

Esempi corretti:

- `Cozy — regola obbligatoria indipendente`
- `Bold & Easy — obbligatorio quando attivo`
- `Prompt da inviare all'AI`
- `Immagini 3/4 · Prompt Pack`
- `Vision controlla stile, Bold & Easy, Cozy, spessore linee, composizione e soggetti presenti.`

La filosofia è: **italiano per l'interazione umana; semantica canonica per la verità del progetto; prompt engineering per parlare all'AI**.
