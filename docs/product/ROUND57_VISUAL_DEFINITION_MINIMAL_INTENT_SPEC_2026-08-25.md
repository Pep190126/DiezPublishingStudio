# Round 5.7 — Definizione visuale minima guidata da Tema

Status: **DECISIONE DI PRODOTTO / DA IMPLEMENTARE / NON CONSOLIDATO**

Data: 2026-08-25

Branch: `spike/uno-platform-ui`

## Decisione utente

La Definizione visuale per Coloring Book / Raccolta immagini non deve più aprirsi con i vecchi campi generici `Soggetti` e `Ambientazione`.

Il percorso principale deve rendere evidente subito la libreria temi introdotta in Round 5.6.

Ordine visibile consigliato:

1. `Numero immagini`;
2. `Tema`;
3. `Stile`;
4. `DEVE FARE · HARD`;
5. `NON DEVE FARE · HARD`;
6. eventuali controlli editoriali/tecnici specifici del tipo libro;
7. proposta soggetti derivata dal Tema e relativa revisione/accettazione.

Formula UX:

**Numero immagini + Tema + Stile devono già essere sufficienti per ottenere una proposta e poi un Prompt Pack sensati.**

`DEVE FARE` e `NON DEVE FARE` servono solo quando l'utente vuole restringere o precisare ulteriormente la volontà editoriale.

## Eliminazione dei vecchi campi generici

Nel percorso visuale ordinario vengono eliminati dalla UI:

- `Soggetto / soggetti` generico;
- `Ambientazione / scenario` generico.

Questi concetti non vengono eliminati dall'architettura semantica: quando servono, vengono espressi attraverso:

- il Tema e il relativo pool/proposta soggetti;
- `DEVE FARE` per requisiti espliciti;
- `NON DEVE FARE` per esclusioni esplicite;
- Scene/Soggetti strutturati opzionali quando l'utente desidera controllo avanzato e identità concrete.

Non devono esistere due fonti visibili concorrenti per la stessa decisione.

## Semantica di DEVE FARE / NON DEVE FARE

Entrambi i campi sono opzionali.

- campo vuoto = **nessun vincolo aggiuntivo**;
- campo valorizzato = requisito HARD canonico;
- Diez non deve inventare un requisito quando il campo è vuoto;
- Diez non deve trasformare un default di UI in requisito HARD;
- il testo utente non viene copiato letteralmente nel prompt provider-facing: segue sempre `UI italiana → significato canonico → Prompt Compiler → rendering provider/model-specifico`.

Esempi:

- `DEVE FARE: ogni soggetto deve essere sorridente` → HARD requirement;
- `NON DEVE FARE: niente elementi spaventosi o sangue` → HARD exclusion;
- entrambi vuoti → Tema/Stile e profilo del tipo libro hanno libertà editoriale entro i propri vincoli.

## Minimal viable visual intent

Per un nuovo progetto visuale il set minimo di decisioni utente deve essere:

- numero immagini;
- tema;
- stile.

Audience, difficoltà, line weight, sfondo, densità e altri parametri specifici del tipo libro possono continuare ad avere default di profilo e restare modificabili, ma non devono obbligare l'utente a definire soggetti o ambientazione prima di ottenere una proposta valida.

Esempio:

`3 immagini + Halloween + Cute & Playful`

è sufficiente perché Diez:

1. scelga il pool Halloween;
2. proponga 3 soggetti distinti;
3. mostri la proposta all'utente;
4. permetta modifica/integrazione Custom;
5. congeli i soggetti solo dopo accettazione;
6. compili Work Unit immagini atomiche coerenti con stile e profilo Coloring.

## Scene e Soggetti strutturati

Scene/Soggetti strutturati non vengono rimossi.

Diventano un livello opzionale di controllo avanzato, utile quando l'utente vuole:

- scegliere esattamente uno o più soggetti;
- mantenere identità consistenti;
- definire scene precise;
- assegnare soggetti a scene;
- imporre una composizione editoriale più controllata.

Il percorso base non deve richiederli.

## Compatibilità progetti esistenti

La rimozione dei campi visibili non deve perdere decisioni già salvate.

Per progetti legacy che contengono valori in `SubjectDescription` o `EnvironmentDescription`:

- Diez conserva i valori durante la migrazione;
- li converte semanticamente in requisiti canonici equivalenti prima della compilazione;
- non li mostra come vecchi campi duplicati;
- la migrazione non deve cambiare il significato né aggiungere dettagli non richiesti;
- dopo il salvataggio nel nuovo modello, il progetto deve dipendere dalle decisioni canoniche nuove e non da stringhe UI legacy.

Una eventuale esposizione all'utente della migrazione deve usare testo italiano chiaro e non introdurre un secondo editor concorrente.

## Tema in cima alla Definizione

Il selettore `Tema` deve essere visivamente prominente e collocato nella parte alta della Definizione, vicino al numero immagini.

Motivazione fisica Round 5.6: il selettore era funzionale ma poteva non essere notato immediatamente.

Questo è classificato come miglioramento UX, non come bug funzionale della libreria temi.

## Gate Prompt Pack

Il Prompt Pack resta bloccato solo finché manca una decisione canonica realmente necessaria.

Con il nuovo flusso:

- count + theme + style → sufficienti per proporre soggetti;
- proposta non ancora accettata → Prompt Pack bloccato;
- proposta accettata → Prompt Pack sbloccabile;
- `DEVE FARE` vuoto → non blocca;
- `NON DEVE FARE` vuoto → non blocca;
- nessun vecchio campo soggetti/ambientazione deve essere richiesto per sbloccare il flusso.

## Regression criteria

Round 5.7 deve fallire se:

1. `Soggetti` o `Ambientazione` generici restano obbligatori/visibili nel percorso base;
2. il Tema non è nella parte alta della Definizione;
3. `3 immagini + Halloween + Cute & Playful` non può produrre una proposta senza altro testo;
4. un `DEVE FARE` vuoto genera comunque un vincolo;
5. un `NON DEVE FARE` vuoto genera comunque un'esclusione;
6. un campo valorizzato viene degradato da HARD a preferenza;
7. Scene/Soggetti strutturati vengono eliminati invece di restare opzionali;
8. progetti legacy perdono soggetto/ambientazione già decisi;
9. una stringa legacy/UI arriva letteralmente al renderer come fonte autoritativa;
10. il Prompt Pack resta bloccato dopo l'accettazione della proposta solo perché i vecchi campi sono vuoti.

## Stato

Questa specifica sostituisce, per il normale percorso visuale, l'idea che `Soggetto` e `Ambientazione` generici siano campi primari della Definizione.

Round 5.7 richiede implementazione Uno/Core, regression test e nuova verifica fisica prima di qualunque consolidamento.
