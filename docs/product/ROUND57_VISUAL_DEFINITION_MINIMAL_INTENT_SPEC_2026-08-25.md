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

## Principio generale — ogni scelta esplicita dell'utente è HARD

Decisione utente del 2026-08-25: Diez non deve trattare i controlli della Definizione come semplici preferenze creative.

**Qualunque criterio esplicitamente scelto, scritto, attivato, disattivato, quantificato, modificato o accettato dall'utente diventa parte del contratto canonico HARD della generazione.**

La generazione deve quindi essere eseguita rispettando esattamente l'insieme dei criteri scelti dall'utente. L'AI può esercitare libertà creativa soltanto sugli aspetti che l'utente non ha deciso.

Esempi di decisioni HARD quando scelte dall'utente:

- numero immagini;
- Tema;
- Stile;
- audience;
- difficoltà;
- line weight;
- complessità;
- densità elementi;
- trattamento dello sfondo;
- white space;
- Bold & Easy;
- Cozy;
- Consistent e relativi lock;
- soggetti accettati dalla proposta Tema;
- soggetti Custom inseriti dall'utente;
- Scene strutturate;
- Soggetti strutturati;
- partecipazioni e assegnazioni soggetto/scena;
- `DEVE FARE`;
- `NON DEVE FARE`;
- qualunque altro parametro editoriale o tecnico che l'utente scelga esplicitamente nella UI.

La parola `opzione` può descrivere soltanto il fatto che l'utente può scegliere o meno un controllo. **Dopo la scelta, quel valore non è opzionale per il generatore.**

Formula canonica:

**scelta esplicita utente = HARD LOCK**

**non deciso dall'utente = spazio di decisione Diez/AI secondo profilo, tema e capability**

Un valore precompilato dalla UI o derivato automaticamente non deve essere falsamente attribuito all'utente. Lo stato canonico deve distinguere almeno la provenienza della decisione, per esempio:

- `USER_SELECTED` / `USER_AUTHORED` → HARD;
- `USER_ACCEPTED` → HARD;
- `PROFILE_DEFAULT` → default di sistema, non decisione utente;
- `DIEZ_DERIVED` → derivazione interna;
- `AI_PROPOSED` → proposta non ancora autoritativa;
- `NOT_DECIDED` → libertà residua.

Quando l'utente accetta esplicitamente un valore proposto da Diez/AI, la provenienza semantica diventa `USER_ACCEPTED` e il valore assume autorità HARD.

Il Prompt Compiler non deve tradurre `HARD` come una semplice enfasi testuale. Deve compilare le decisioni in istruzioni provider-facing non ambigue, validabili e coerenti con la capability/modello usato.

Se il provider non può rispettare un HARD scelto dall'utente, Diez deve segnalarlo o bloccare l'esecuzione; non deve degradare silenziosamente il criterio a preferenza.

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

Entrambi i campi sono opzionali come scelta di compilazione.

- campo vuoto = **nessun vincolo aggiuntivo**;
- campo valorizzato = requisito HARD canonico;
- Diez non deve inventare un requisito quando il campo è vuoto;
- Diez non deve trasformare un default di UI in requisito HARD utente;
- il testo utente non viene copiato letteralmente nel prompt provider-facing: segue sempre `UI italiana → significato canonico → Prompt Compiler → rendering provider/model-specifico`.

Esempi:

- `DEVE FARE: ogni soggetto deve essere sorridente` → HARD requirement;
- `NON DEVE FARE: niente elementi spaventosi o sangue` → HARD exclusion;
- entrambi vuoti → nessun ulteriore requisito testuale dell'utente; restano comunque HARD tutte le altre scelte esplicite effettuate nella Definizione.

## Minimal viable visual intent

Per un nuovo progetto visuale il set minimo di decisioni utente può essere:

- numero immagini;
- tema;
- stile.

Se questi tre valori sono esplicitamente scelti/accettati dall'utente, sono tutti HARD.

Audience, difficoltà, line weight, sfondo, densità e altri parametri specifici del tipo libro possono continuare ad avere default di profilo e restare modificabili. Un default non toccato resta un default di sistema; se l'utente lo modifica o lo conferma come propria decisione, diventa HARD.

Esempio:

`3 immagini + Halloween + Cute & Playful`

significa:

- esattamente 3 immagini → HARD;
- appartenenza semantica al tema Halloween → HARD;
- resa Cute & Playful → HARD.

È sufficiente perché Diez:

1. scelga il pool Halloween;
2. proponga 3 soggetti distinti coerenti con Halloween;
3. mostri la proposta all'utente;
4. permetta modifica/integrazione Custom;
5. congeli i soggetti solo dopo accettazione;
6. compili Work Unit immagini atomiche che rispettino tutti i criteri HARD scelti.

Diez può decidere autonomamente dettagli non definiti, ma non può generare qualcosa solo "vicino" ai criteri scelti: deve rispettarli.

## Scene e Soggetti strutturati

Scene/Soggetti strutturati non vengono rimossi.

Diventano un livello opzionale di controllo avanzato, utile quando l'utente vuole:

- scegliere esattamente uno o più soggetti;
- mantenere identità consistenti;
- definire scene precise;
- assegnare soggetti a scene;
- imporre una composizione editoriale più controllata.

Il percorso base non deve richiederli.

### HARD LOCK quando definiti dall'utente

La loro opzionalità riguarda soltanto **se l'utente decide di usarli oppure no**.

Quando l'utente definisce esplicitamente un Soggetto strutturato o una Scena strutturata, quella decisione diventa HARD canonica.

Regole obbligatorie:

- un Soggetto definito dall'utente non può essere sostituito da una proposta del Tema;
- una Scena definita dall'utente non può essere reinterpretata come semplice preferenza;
- partecipanti, identità, relazioni e assegnazioni esplicitamente definite dall'utente sono HARD;
- il Tema scelto dall'utente resta anch'esso HARD e deve essere conciliato con la definizione strutturata;
- lo Stile scelto dall'utente è HARD e deve governare la resa senza cambiare identità, soggetto o fatti di scena;
- `DEVE FARE` e `NON DEVE FARE`, quando compilati, restano HARD e devono essere conciliati con tutte le altre scelte utente;
- se due HARD utente entrano in conflitto, Diez deve mostrare il conflitto e bloccare la compilazione invece di risolverlo silenziosamente;
- il Prompt Compiler deve congelare nelle Work Unit i concreti `SubjectId` / `SceneId` e i relativi fatti canonici, non una descrizione aggregata o una scelta delegata al renderer.

Non esiste una precedenza che autorizzi Diez a ignorare un HARD utente per rispettarne un altro. Le decisioni HARD devono essere compatibili fra loro. Se non lo sono, serve un conflitto visibile all'utente.

Ordine di autorità:

1. vincoli di sicurezza/piattaforma/capability non derogabili;
2. **insieme completo delle decisioni utente HARD**, senza degradazione: quantità, Tema, Stile, parametri tecnici/editoriali, DEVE/NON DEVE, Scene, Soggetti, Consistent, assegnazioni e valori accettati;
3. decisioni di tipo/profilo non esplicitamente cambiate dall'utente;
4. derivazioni Diez;
5. libertà AI residua.

Il Tema può completare ciò che non è definito nelle Scene/Soggetti, ma non può sovrascriverli; allo stesso modo Scene/Soggetti non possono violare il Tema scelto senza produrre un conflitto da risolvere.

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

Il Prompt Pack resta bloccato solo finché manca una decisione canonica realmente necessaria o esiste un conflitto tra HARD.

Con il nuovo flusso:

- count + theme + style → sufficienti per proporre soggetti;
- proposta non ancora accettata → Prompt Pack bloccato;
- proposta accettata → i soggetti diventano USER_ACCEPTED/HARD;
- `DEVE FARE` vuoto → non blocca;
- `NON DEVE FARE` vuoto → non blocca;
- nessun vecchio campo soggetti/ambientazione deve essere richiesto per sbloccare il flusso;
- se Scene/Soggetti strutturati sono attivi, devono essere completi e semanticamente coerenti prima del Prompt Pack;
- un conflitto tra HARD utente blocca il Prompt Pack con errore esplicito;
- nessun renderer/provider può ricevere una Work Unit che presenti come facoltativo un criterio USER_SELECTED / USER_AUTHORED / USER_ACCEPTED.

## Regression criteria

Round 5.7 deve fallire se:

1. `Soggetti` o `Ambientazione` generici restano obbligatori/visibili nel percorso base;
2. il Tema non è nella parte alta della Definizione;
3. `3 immagini + Halloween + Cute & Playful` non può produrre una proposta senza altro testo;
4. un `DEVE FARE` vuoto genera comunque un vincolo;
5. un `NON DEVE FARE` vuoto genera comunque un'esclusione;
6. una scelta esplicita dell'utente viene degradata da HARD a preferenza;
7. Tema o Stile USER_SELECTED vengono trattati come semplice contesto creativo;
8. quantità USER_SELECTED non viene rispettata esattamente;
9. un parametro tecnico/editoriale USER_SELECTED viene ignorato dal Prompt Compiler;
10. un valore PROFILE_DEFAULT viene falsamente marcato come decisione utente senza scelta/accettazione;
11. Scene/Soggetti strutturati vengono eliminati invece di restare opzionali;
12. un Soggetto strutturato definito dall'utente viene sostituito da un soggetto del Tema;
13. una Scena strutturata definita dall'utente viene trattata come preferenza o modificata silenziosamente;
14. un identity lock o una partecipazione esplicita viene ignorata dal compiler;
15. due HARD utente in conflitto vengono risolti silenziosamente;
16. una Work Unit delega al renderer la scelta di un criterio già deciso dall'utente;
17. un provider incapace di rispettare un HARD viene usato degradando silenziosamente il requisito;
18. progetti legacy perdono soggetto/ambientazione già decisi;
19. una stringa legacy/UI arriva letteralmente al renderer come fonte autoritativa;
20. il Prompt Pack resta bloccato dopo l'accettazione della proposta solo perché i vecchi campi sono vuoti.

## Stato

Questa specifica sostituisce, per il normale percorso visuale, l'idea che `Soggetto` e `Ambientazione` generici siano campi primari della Definizione.

La regola generale di Round 5.7 è ora:

**tutto ciò che l'utente decide esplicitamente è vincolo HARD di generazione; Diez/AI decidono soltanto ciò che l'utente lascia realmente aperto.**

Round 5.7 richiede implementazione Uno/Core, provenance delle decisioni, regression test e nuova verifica fisica prima di qualunque consolidamento.
