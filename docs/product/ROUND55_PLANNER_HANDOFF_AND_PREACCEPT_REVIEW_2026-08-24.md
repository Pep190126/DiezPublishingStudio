# Round 5.5 — Planner handoff, review/edit prima dell'accettazione e Prompt Pack gate

Status: **IMPLEMENTATO IN CANDIDATA / TECHNICALLY_VERIFIED / NON CONSOLIDATO**

Data: 2026-08-24

Branch: `spike/uno-platform-ui`

## Evidenza fisica di partenza

In Round 5.4, con un progetto Coloring che richiede una serie aggregata di tre soggetti Halloween, Diez ha prodotto il prompt intermedio `# DIEZ SEMANTIC SUBJECT PLANNER`, ma l'utente ha osservato che la creazione del Prompt Pack immagini non parte.

Il prompt planner osservato contiene correttamente:

- `Book family: coloring book`;
- `Series theme: Halloween`;
- `Required subject count: EXACTLY 3`;
- contesto stile `Cute & Playful`;
- pubblico `Children ages 6–9`;
- contratto JSON con `display_name_it`, `canonical_concept`, `description_it`, `canonical_description`.

Sono però emersi difetti di prodotto nel passaggio planner → revisione utente → Prompt Pack.

## 1. Handoff planner / Prompt Pack

Il prompt planner non è il Prompt Pack immagini. È una Work Unit TEXT intermedia che deve prima ricevere una risposta JSON, essere importata come Candidate, validata e accettata dall'utente.

Round 5.5 rende questo stato esplicito:

- finché il piano soggetti è irrisolto, il Prompt Pack immagini è disabilitato;
- la UI indica la prossima azione esatta;
- senza risposta valida: `copia Prompt planner → esegui con AI → incolla JSON → Importa come candidato`;
- con proposta valida non ancora accettata: `Definizione → verifica/modifica proposta → Accetta proposta e congela i soggetti`;
- solo dopo l'accettazione il Prompt Pack immagini diventa operativo.

## 2. La proposta AI è visibile prima dell'accettazione

Decisione utente esplicita del 2026-08-24:

> Nel piano soggetti, quando li lascio preparare all'AI, devo vedere cosa ha trovato prima di accettare la proposta.

Regola HARD di UX/editorial control.

Diez mostra in **Definizione** una sezione **Proposta AI da verificare e modificare prima di accettare**, con per ogni soggetto:

1. nome editoriale visibile in italiano (`display_name_it`);
2. descrizione editoriale visibile in italiano (`description_it`).

`canonical_concept` e `canonical_description` restano dati semantici del compilatore e non sostituiscono la presentazione italiana.

L'import della risposta AI non equivale ad accettazione e non congela automaticamente alcun soggetto.

## 3. La proposta è modificabile prima dell'accettazione

Decisione utente esplicita del 2026-08-24:

> Devo poter eventualmente modificare la proposta.

La proposta AI è una **bozza editoriale**, non un risultato take-it-or-leave-it.

Per ogni soggetto sono modificabili:

- nome editoriale visibile in italiano;
- descrizione editoriale visibile in italiano.

Se un campo viene modificato, **Accetta proposta e congela i soggetti** viene disabilitato fino al salvataggio della revisione: Diez non può ignorare silenziosamente una modifica visibile dell'utente.

### 3.1 Modifica di sola formulazione editoriale

Scelta UI:

`Ho cambiato solo la formulazione · il soggetto/significato è lo stesso`

In questo caso:

- i nuovi testi italiani diventano autorevoli per la UI;
- `canonical_concept` e `canonical_description` già validati vengono conservati;
- Diez crea una nuova Candidate testuale;
- nessuna approvazione è automatica;
- la proposta modificata viene mostrata nuovamente prima dell'accettazione.

### 3.2 Modifica che cambia soggetto/significato

Scelta UI:

`Ho cambiato il soggetto o il significato · Diez deve ricompilare la semantica`

In questo caso:

- la vecchia semantica tecnica AI viene marcata stale e non può essere riutilizzata;
- Diez salva le modifiche utente come draft non semanticamente risolto;
- crea una nuova Work Unit TEXT `# DIEZ SEMANTIC SUBJECT RECONCILIATION`;
- `display_name_it` e `description_it` modificati dall'utente diventano HARD LOCK;
- il planner non può sostituire, fondere o reinterpretare i soggetti;
- l'AI deve rigenerare solamente `canonical_concept` e `canonical_description` coerenti;
- la nuova risposta torna Candidate e viene mostrata di nuovo prima dell'accettazione;
- il Prompt Pack immagini resta bloccato fino alla riconciliazione e successiva accettazione.

Questa separazione evita sia traduzione meccanica sia riuso di semantica obsoleta.

## 4. Duplicazione del planner

Per lo stesso stato semantico del progetto, stesso tema, cardinalità e prompt planner:

- una seconda pressione di `Prepara proposta soggetti con AI` non crea un duplicato;
- Diez riusa l'attività planner già preparata;
- una proposta già valida rimanda a verifica/modifica/accettazione;
- una riconciliazione semantica già pendente non genera un altro planner.

## 5. Contaminazione semantica nel planner

Nel prompt fisicamente osservato in Round 5.4 compariva:

`Publisher HARD exclusions relevant to subject selection: un'unica image con 3 illustrazioni`

È un vincolo di layout/atomicità, non di selezione dei soggetti.

Round 5.5 filtra dal planner soggetti i vincoli relativi a:

- una sola immagine/canvas;
- più illustrazioni nello stesso canvas;
- collage;
- griglia;
- triptych;
- pannelli/layout.

Questi restano responsabilità del Prompt Compiler delle Work Unit immagine e dei relativi HARD gate.

Resta valido il confine:

**UI italiana / testo utente → significato canonico → Prompt Compiler → prompt engineering provider-facing.**

## 6. Ritorno automatico alla proposta

Quando l'utente importa con successo la risposta JSON della Work Unit planner o di riconciliazione:

- Diez salva la Candidate;
- valida il contratto;
- ritorna al workspace visuale/Definizione;
- mostra immediatamente la proposta da verificare/modificare;
- non approva e non applica automaticamente.

## 7. Regression gate

Round 5.5 verifica automaticamente che:

1. una seconda preparazione identica non crei un secondo planner;
2. il Prompt Pack resti bloccato mentre il piano è irrisolto;
3. la proposta AI sia disponibile come stato prima dell'accettazione;
4. l'import Candidate non equivalga ad accettazione;
5. i vincoli raw di layout non contaminino il planner;
6. dopo l'accettazione il piano non resti irrisolto;
7. una modifica solo editoriale conservi il `canonical_concept` esistente;
8. una modifica semantica prepari una riconciliazione distinta;
9. durante la riconciliazione le modifiche italiane dell'utente restino visibili e non risultino già accettabili;
10. una riconciliazione duplicata venga bloccata;
11. la risposta riconciliata debba preservare esattamente i soggetti user-locked;
12. il renderer finale riceva la nuova semantica canonica e non quella stale precedente.

## 8. Candidata Windows Round 5.5

Build finale pulita, senza workflow/marker one-shot e senza patch applicate durante la CI:

- Source SHA: `bc069089eaa05d3235d6e4ced623184682c1cd85`
- Workflow: `Uno Windows Consolidation Candidate`
- Run: `#44`
- Run ID: `32713476241`
- stato: `TECHNICALLY_VERIFIED`
- Visual book gate: `success`
- Visual semantic regression: `success`
- Restore Windows runtime: `success`
- Publish Uno Windows x64: `success`
- Verify executable: `success`
- Build Setup EXE: `success`
- Smoke install/launch/uninstall: `success`
- Artifact upload: `success`
- Artifact ID: `9515092034`
- Artifact ZIP bytes: `94446663`
- Artifact ZIP SHA-256: `f68f6d239f1aed5c7944f3572e74adcaae207209e1a68213d7f9bcb25e9f24d3`
- Setup bytes: `94975594`
- Setup SHA-256: `e3a7a5f03e3289e5dcab16dd56841e11960823c2523256500d2ad29c4d84fe64`

Gli hash del ZIP scaricato e del Setup estratto sono stati verificati localmente e coincidono con CI/artifact metadata.

## 9. Consolidamento

Round 5.5 resta **NON CONSOLIDATO** finché non è fisicamente verificato il percorso completo nell'app installata:

`tema aggregato → planner unico → risposta JSON → proposta visibile/modificabile → eventuale riconciliazione → accettazione utente → Prompt immagini → Prompt Pack ZIP`.

Test fisico prioritario:

1. progetto nuovo;
2. progetto 10 legacy riaperto;
3. prova accettazione senza modifiche;
4. prova modifica solo editoriale;
5. prova modifica che cambia davvero un soggetto;
6. ispezione del Prompt Pack reale dopo il freeze canonico.

## 10. Round 5.5.2 — il JSON del planner è interno a Diez

Decisione utente esplicita del 2026-08-24:

> Il flusso è troppo elaborato e complicato. Diez, in base al framework, deve generare il JSON internamente.

Regola di prodotto:

**il JSON è un contratto interno di Diez, non un formato che l'utente deve conoscere, copiare, modificare o validare manualmente.**

Il flusso UX deve diventare:

`Prepara proposta soggetti → esegui richiesta AI → incolla la risposta AI → Diez estrae/normalizza → mostra le card soggetto → utente modifica/accetta → Diez genera e persiste lo stato canonico interno → Prompt Compiler`.

Conseguenze:

- il box utente non deve essere presentato come “incolla JSON”; deve essere semplicemente “Risposta AI”;
- l'utente può incollare una risposta leggibile/strutturata dell'AI senza preoccuparsi dello schema tecnico;
- Diez possiede schema, cardinalità, ID, stato Candidate e serializzazione JSON;
- Diez genera internamente `display_name_it`, `canonical_concept`, `description_it`, `canonical_description` nel proprio modello canonico;
- se la risposta AI non è interpretabile in modo univoco, Diez blocca e spiega cosa manca; non chiede all'utente di correggere sintassi JSON;
- il framework/provider può continuare a usare structured output/schema quando disponibile, ma questa complessità resta sotto coperta;
- con trasporto manuale, l'unica azione tecnica dell'utente resta copia della richiesta verso l'AI e incolla della risposta ricevuta;
- con futura API diretta, anche questo passaggio manuale scompare senza cambiare il modello canonico.

Nuovo principio UX:

**L'utente decide i contenuti; Diez decide il formato tecnico.**

La candidata Round 5.5 esistente non soddisfa ancora pienamente questa semplificazione; serve una successiva correzione UI/Core prima del consolidamento.