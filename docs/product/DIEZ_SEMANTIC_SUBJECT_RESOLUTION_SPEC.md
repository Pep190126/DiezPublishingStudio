# Diez — risoluzione semantica dei soggetti prima del Prompt Pack

Status: **SPECIFICA DI PRODOTTO / ARCHITETTURA — HARD, NON CONSOLIDATA**

Data: 2026-08-24

Branch: `spike/uno-platform-ui`

## 1. Decisione

Quando l'utente inserisce un tema aggregato, per esempio `3 soggetti di Halloween`, Diez non deve:

- usare la frase aggregata come soggetto di ogni Work Unit;
- chiedere al generatore immagini di inventare i soggetti durante il rendering;
- affidare al Prompt Pack esterno la pianificazione della serie;
- usare una traduzione letterale del testo UI.

Diez deve risolvere il tema in un **piano semantico concreto prima del Prompt Pack**.

Esempio:

`3 soggetti di Halloween`

può diventare, dopo risoluzione:

1. `Zucca jack-o'-lantern simpatica`
2. `Fantasma amichevole e sorridente`
3. `Gatto con cappello da strega`

I tre soggetti diventano stato canonico del progetto e ogni Work Unit riceve un solo soggetto concreto congelato.

## 2. Non hardcodare temi nel compiler

Non deve esistere una regola del tipo:

`Halloween => zucca, fantasma, gatto`

nel Prompt Compiler.

Il compiler deve consumare un piano già risolto.

La scelta dei soggetti appartiene a un componente precedente, concettualmente `EditorialSemanticPlanner` / `SubjectAssignmentResolver`.

## 3. Ordine di risoluzione

Diez deve risolvere i soggetti secondo questa precedenza:

### A. Soggetti esplicitamente definiti dall'utente

Se l'utente ha scritto o selezionato soggetti concreti, quelli sono autorevoli.

Nessuna AI può sostituirli silenziosamente.

### B. Soggetti strutturati già presenti nel progetto

Se la schermata `Scene e soggetti` contiene soggetti concreti attivi, Diez usa quelli secondo le regole di partecipazione/assegnazione.

### C. Decisione `Deriva dai materiali`

Se l'utente ha scelto di derivare i soggetti dai materiali, Diez estrae/proietta candidati dai materiali consentiti e li sottopone ai validatori semantici prima di congelarli.

### D. Decisione `Diez propone / decide`

Se l'utente ha fornito soltanto un tema aggregato o ha scelto esplicitamente che Diez proponga, Diez esegue una fase di pianificazione semantica **separata dal rendering immagini**.

Il planner può usare un modello testuale/provider disponibile come strumento interno di Diez, ma il suo output non viene mai inviato direttamente al renderer: deve tornare in formato strutturato, essere validato, diventare stato canonico e solo dopo alimentare il Prompt Compiler.

Quindi anche quando un modello AI aiuta a proporre i soggetti, **la decisione appartiene a Diez**, perché Diez controlla il contratto, valida il risultato, lo persiste e lo congela prima della produzione immagini.

### E. Nessuna capacità di pianificazione disponibile

Se Diez non può risolvere un tema aggregato in soggetti concreti e non esiste una lista utente/materiale sufficiente, il Prompt Pack deve restare bloccato con un messaggio italiano chiaro.

Diez non deve delegare implicitamente la scelta al renderer.

## 4. Contratto del planner

Per una richiesta di N immagini il planner deve produrre esattamente N assegnazioni strutturate, per esempio:

```text
SubjectAssignmentPlan
- Theme: Halloween
- Count: 3
- Items:
  - position: 1
    canonical_subject: pumpkin_jack_o_lantern
    display_label_it: Zucca jack-o'-lantern simpatica
    semantic_description: friendly carved Halloween pumpkin, single focal subject
  - position: 2
    canonical_subject: friendly_ghost
    display_label_it: Fantasma amichevole e sorridente
    semantic_description: friendly smiling ghost, single focal subject
  - position: 3
    canonical_subject: witch_cat
    display_label_it: Gatto con cappello da strega
    semantic_description: cute cat wearing a witch hat, single focal subject
```

Le label italiane sono UI. Il Prompt Compiler usa il significato canonico/semantico e lo trasforma in prompt engineering provider-facing.

## 5. Validatori obbligatori

Prima di accettare il piano, Diez deve verificare almeno:

1. **cardinalità** — esattamente N soggetti per N Work Unit quando il modello richiesto è 1 soggetto principale per immagine;
2. **atomicità** — ogni assegnazione identifica un soggetto concreto, non `3 soggetti`, `vari personaggi`, `animali diversi`;
3. **distinzione** — i soggetti sono semanticamente distinti salvo ripetizione esplicitamente richiesta;
4. **aderenza al tema** — ogni soggetto appartiene chiaramente al tema/obiettivo scelto;
5. **compatibilità con HARD** — nessun soggetto viola `DEVE FARE`, `NON DEVE FARE`, audience, tipo libro o altri lock;
6. **riconoscibilità** — il soggetto è abbastanza specifico da poter essere riconosciuto come focal subject;
7. **assenza di layout** — la scelta del soggetto non incorpora collage, griglie, più tavole su un canvas o istruzioni di batching;
8. **stabilità** — una volta congelato, il piano non cambia solo perché si rigenera il Prompt Pack.

## 6. UX in Definizione

Quando Diez risolve automaticamente un tema aggregato, la schermata `Definizione` deve mostrare una sezione italiana, per esempio:

`Soggetti risolti da Diez`

con la lista ordinata delle Work Unit:

- `1 · Zucca jack-o'-lantern simpatica`
- `2 · Fantasma amichevole e sorridente`
- `3 · Gatto con cappello da strega`

L'utente può modificarli o rigenerare la proposta prima del freeze.

Non è necessario costringerlo a riscrivere tutto: la funzione esiste proprio per evitare lavoro ripetitivo.

## 7. Freeze e invalidazione

Il piano viene congelato prima della creazione delle Work Unit definitive / Prompt Pack.

Se cambia una decisione che invalida il piano, per esempio:

- tema;
- numero immagini;
- elenco soggetti;
- `DEVE FARE` / `NON DEVE FARE` rilevante;
- scene/partecipazioni;

Diez marca il piano `STALE` e lo risolve nuovamente oppure richiede conferma se esistono modifiche manuali dell'utente.

Una semplice rigenerazione del Prompt Pack senza cambiamenti semantici non deve cambiare i soggetti.

## 8. Rapporto con il Prompt Compiler

Il Prompt Compiler non decide il contenuto creativo di serie ancora irrisolto.

Riceve, per ogni Work Unit, un contesto già concreto:

`Work Unit IMG-001 -> subject = Zucca jack-o'-lantern simpatica`

`Work Unit IMG-002 -> subject = Fantasma amichevole e sorridente`

`Work Unit IMG-003 -> subject = Gatto con cappello da strega`

Poi trasforma quei significati in prompt engineering efficace per provider/modello.

Il renderer immagini non deve vedere istruzioni come:

- `scegli un soggetto Halloween diverso`;
- `one distinct subject fitting the theme`;
- `select another subject for this item`.

Queste sono istruzioni di pianificazione e devono essere state risolte prima.

## 9. Provider AI del planner vs provider immagini

Il planner semantico e il renderer immagini sono ruoli diversi.

Un modello testuale può essere usato da Diez per proporre un piano semantico strutturato anche quando il renderer finale è un modello immagini diverso.

La pipeline resta:

`decisioni utente -> planner semantico Diez -> validazione -> stato canonico -> Prompt Compiler -> renderer immagini`

Mai:

`decisioni utente -> renderer immagini che decide anche il piano`.

## 10. Compatibilità con progetti esistenti

Quando un progetto legacy contiene un aggregate subject persistito nel vecchio prompt/state, Diez deve ricostruire il piano dai dati utente/canonici disponibili.

Non deve promuovere una vecchia frase provider-facing a decisione canonica.

A parità di scelte visibili, progetto nuovo e progetto riaperto devono produrre lo stesso `SubjectAssignmentPlan`, esclusi gli ID tecnici.

## 11. Criterio di successo

Per un progetto con `3 soggetti di Halloween`, prima di creare il Prompt Pack deve essere possibile vedere in Diez quali sono i tre soggetti concreti assegnati.

Se il Prompt Pack contiene ancora una richiesta al provider di **scegliere** il soggetto di una Work Unit, la pianificazione semantica non è completa e il pacchetto deve essere considerato non conforme.
