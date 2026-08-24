# Custom Style — decisione utente e libreria stili

Status: **SPECIFICA / DA IMPLEMENTARE**

Data: 2026-08-24

Branch: `spike/uno-platform-ui`

## Regola UI

Quando l'utente seleziona **Custom** nel controllo dello stile, Diez deve mostrare immediatamente un campo testuale dedicato in cui l'utente può descrivere liberamente lo stile desiderato.

Il campo custom è una **decisione visibile dell'utente** e il suo contenuto deve essere persistito come significato canonico del progetto, non come semplice frammento di prompt.

## Decisione di archiviazione

Accanto o subito sotto il campo Custom deve comparire una decisione esplicita dell'utente:

- **Usa solo in questo progetto**
- **Salva anche nella libreria degli stili**

La scelta di archiviare lo stile è separata dalla scelta dello stile stesso e non deve essere implicita.

Se l'utente sceglie **Usa solo in questo progetto**:
- lo stile resta parte del progetto corrente;
- non viene aggiunto alla libreria globale degli stili;
- il suo testo resta comunque disponibile alla riapertura del progetto.

Se l'utente sceglie **Salva anche nella libreria degli stili**:
- Diez crea una nuova voce riutilizzabile nella libreria degli stili;
- la nuova voce deve avere una propria identità stabile;
- il progetto corrente continua a conservare la propria decisione semantica anche se in futuro la voce della libreria viene rinominata o modificata.

## Prompt Compiler

Il testo scritto nel campo Custom non deve essere incollato o semplicemente tradotto nel prompt provider-facing.

Pipeline obbligatoria:

`testo stile Custom visibile all'utente -> significato canonico dello stile -> Prompt Compiler -> prompt engineering provider/model-specifico`

Diez deve preservare il significato espresso dall'utente, trasformandolo in istruzioni di rendering efficaci per il modello/provider selezionato.

## Importa impostazioni da altro progetto

Essendo una scelta/testo visibile dell'utente, lo stile Custom e la decisione relativa al suo uso nel progetto devono essere importabili tramite **Importa impostazioni da altro progetto**.

Non deve invece essere duplicata automaticamente una voce di libreria globale già esistente: il nuovo progetto deve riferirsi semanticamente allo stile scelto senza creare duplicati involontari nella libreria.

## Stato

Da implementare e validare fisicamente nella UI Coloring/Immagini prima del consolidamento della relativa tranche.
