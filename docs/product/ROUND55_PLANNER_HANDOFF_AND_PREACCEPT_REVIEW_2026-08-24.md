# Round 5.5 — Planner handoff, review prima dell'accettazione e Prompt Pack gate

Status: **PHYSICAL FINDING + FIX REQUIRED / NON CONSOLIDATO**

Data: 2026-08-24

Branch: `spike/uno-platform-ui`

## Evidenza fisica

In Round 5.4, con un progetto Coloring che richiede una serie aggregata di tre soggetti Halloween, Diez ha prodotto il prompt intermedio `# DIEZ SEMANTIC SUBJECT PLANNER`, ma l'utente ha osservato che la creazione del Prompt Pack immagini non parte.

Il prompt planner osservato contiene correttamente:

- `Book family: coloring book`;
- `Series theme: Halloween`;
- `Required subject count: EXACTLY 3`;
- contesto stile `Cute & Playful`;
- pubblico `Children ages 6–9`;
- contratto JSON con `display_name_it`, `canonical_concept`, `description_it`, `canonical_description`.

Sono però emersi tre difetti di prodotto.

## 1. Handoff poco chiaro tra planner e Prompt Pack

Il prompt planner non è il Prompt Pack immagini. È una Work Unit TEXT intermedia che deve prima ricevere una risposta JSON, essere importata come Candidate, validata e accettata dall'utente.

Round 5.4 lascia però visibile la creazione del Prompt Pack anche mentre il planner è irrisolto; il Core la blocca successivamente, facendo apparire il sistema come se il Prompt Pack non partisse.

### Regola Round 5.5

Finché il piano soggetti è irrisolto:

- il Prompt Pack immagini è esplicitamente bloccato/disabilitato;
- la UI spiega la prossima azione esatta;
- se non esiste una risposta valida: `copia Prompt planner → esegui con AI → incolla JSON → Importa come candidato`;
- se esiste una proposta valida ma non accettata: `torna in Definizione → verifica proposta → Accetta proposta e congela i soggetti`;
- solo dopo l'accettazione il Prompt Pack immagini diventa disponibile.

## 2. La proposta AI deve essere visibile prima dell'accettazione

Decisione utente esplicita del 2026-08-24:

> Nel piano soggetti, quando li lascio preparare all'AI, devo vedere cosa ha trovato prima di accettare la proposta.

Questa è una regola HARD di UX/editorial control.

Prima di abilitare l'accettazione, Diez deve mostrare in **Definizione** una sezione chiaramente identificabile come **Proposta AI da verificare**, contenente per ogni soggetto almeno:

1. nome editoriale visibile in italiano (`display_name_it`);
2. descrizione editoriale visibile in italiano (`description_it`), quando presente.

I campi tecnici `canonical_concept` e `canonical_description` restano stato semantico/compilatore e non devono sostituire la presentazione italiana.

La UI deve dichiarare esplicitamente che nessun soggetto viene applicato o congelato finché l'utente non preme **Accetta proposta e congela i soggetti**.

L'accettazione non può essere implicita nell'import della risposta AI.

## 3. Duplicazione del planner

L'utente riferisce che Diez ha generato due prompt planner durante lo stesso tentativo. `Prepare` in Round 5.4 crea sempre una nuova attività.

### Regola Round 5.5

Per lo stesso stato semantico del progetto, stesso tema, stessa cardinalità e stesso prompt planner:

- una seconda pressione di `Prepara proposta soggetti con AI` non deve creare un duplicato;
- Diez deve riusare l'attività planner già preparata;
- se una proposta valida è già importata, deve portare l'utente alla verifica/accettazione anziché creare una nuova attività.

## 4. Contaminazione semantica nel prompt planner

Nel prompt fisicamente osservato compare:

`Publisher HARD exclusions relevant to subject selection: un'unica image con 3 illustrazioni`

Questo vincolo è di layout/atomicità della produzione immagini, non di selezione dei soggetti. Inoltre contiene testo utente quasi letterale e misto italiano/inglese.

Viola il confine già definito:

**UI italiana / testo utente → significato canonico → Prompt Compiler → prompt engineering provider-facing.**

### Regola Round 5.5

Il planner soggetti riceve solo vincoli semanticamente rilevanti per **quali soggetti scegliere**.

Vincoli su:

- una sola immagine/canvas;
- più illustrazioni nello stesso canvas;
- collage;
- griglia;
- triptych;
- pannelli/layout;

non devono essere copiati nel planner soggetti. Restano responsabilità del compilatore delle Work Unit immagine e dei relativi HARD gate.

## 5. Ritorno automatico alla proposta

Quando l'utente importa con successo la risposta JSON della Work Unit planner:

- Diez salva la Candidate;
- valida il contratto;
- ritorna al workspace visuale/Definizione;
- mostra immediatamente la proposta AI da verificare;
- non approva e non applica automaticamente.

## Regression criteria

Round 5.5 deve fallire se:

1. una seconda preparazione identica crea un secondo planner;
2. il Prompt Pack immagini appare operativo mentre `planner.Required == true`;
3. una proposta valida può essere accettata senza che i nomi/descrizioni siano prima mostrati in UI;
4. l'import della Candidate equivale automaticamente ad accettazione;
5. il planner contiene il vincolo raw `un'unica image con 3 illustrazioni` o equivalenti di layout;
6. dopo l'import planner l'utente non viene guidato alla verifica in Definizione;
7. dopo l'accettazione il piano resta segnato come irrisolto.

## Consolidamento

Round 5.5 resta **NON CONSOLIDATO** finché non è fisicamente verificato il percorso completo:

`tema aggregato → planner unico → risposta JSON → proposta visibile → accettazione utente → Prompt immagini → Prompt Pack ZIP`.
