# Diez — UI italiana e compilazione semantica del Prompt

Status: **SPECIFICA DI PRODOTTO / ARCHITETTURA — HARD, NON CONSOLIDATA**

Data: 2026-08-24

Branch: `spike/uno-platform-ui`

## 1. Decisione

In Diez l'italiano appartiene all'esperienza utente. Ogni testo rivolto all'utente deve essere mostrato in italiano, salvo nomi propri, nomi di stile, nomi di provider/modelli e termini di prodotto/AI che rimangono riconoscibili nella loro forma standard.

Questa decisione **non** significa che il Prompt debba essere una traduzione inglese della UI.

Il Prompt provider-facing deve essere una compilazione semantica e tecnica delle decisioni editoriali.

## 2. Pipeline obbligatoria

La pipeline è:

`Testo/controlli UI italiani`

`→ decisioni canoniche strutturate e language-neutral`

`→ interpretazione semantica per famiglia libro / capability / Work Unit`

`→ prompt engineering`

`→ adattamento provider/modello/formato output`

`→ snapshot Prompt / Prompt Pack / API`

La UI non è la fonte di verità testuale del Prompt.

## 3. Regola HARD: niente traduzione letterale

Diez **non deve trascrivere nel Prompt ciò che compare a video**, né in italiano né tramite semplice traduzione inglese.

Esempio concettuale sbagliato:

`UI: Contorni spessi`

`Prompt: Thick outlines.`

Questo può essere semanticamente insufficiente.

La compilazione corretta deve trasformare la decisione in una direttiva adatta al task, per esempio per un Coloring:

- contorni neri chiaramente leggibili;
- spessore coerente sull'intera pagina;
- separazione netta delle aree colorabili;
- nessuna perdita di leggibilità in stampa;
- niente masse nere crude o forme primitive solo per simulare uno spessore elevato.

Il wording esatto dipende dal provider/modello e dal contesto della Work Unit.

## 4. Lo stato canonico deve rappresentare il significato

Le decisioni persistite nel `.diez` devono rappresentare concetti, non frasi UI.

Esempi:

- `LineWeight = Thick`
- `BoldEasy = true`
- `Cozy = true`
- `NoTextInsideImage = true`
- `Consistency.SubjectIds = [...]`
- `SceneLock = SameScene`
- `MaterialAiUse = REFERENCE_ONLY`

La label italiana può cambiare senza cambiare il significato canonico.

Il Prompt Compiler consuma i valori canonici, non la stringa visibile nella ComboBox o nel TextBlock.

## 5. Testo libero dell'utente

Quando l'utente scrive testo libero (`DEVE FARE`, `NON DEVE FARE`, descrizione soggetto, scena, obiettivo, note editoriali), Diez deve:

1. preservare il significato e i fatti espliciti;
2. non inventare requisiti non espressi;
3. classificare semanticamente ciò che è requisito, esclusione, fatto canonico, preferenza o contesto;
4. integrare il contenuto nel punto corretto del Prompt;
5. riscriverlo, quando utile, in una forma di prompt engineering più precisa e operativa;
6. mantenere HARD ciò che l'utente ha definito HARD;
7. segnalare conflitti tra HARD invece di risolverli silenziosamente.

Quindi anche il testo libero **non va semplicemente tradotto**: va interpretato e compilato senza alterarne il significato.

## 6. Prompt engineering per provider e modello

Il renderer finale del Prompt può cambiare formulazione in funzione di:

- provider;
- modello;
- testo vs testo strutturato vs immagine;
- schema JSON richiesto;
- capacità native del modello;
- limiti/peculiarità del provider;
- Work Unit corrente;
- famiglia libro e capability attive.

Due provider possono ricevere prompt diversi pur derivando dallo stesso snapshot canonico.

La semantica deve restare equivalente; la forma deve essere ottimizzata.

## 7. Testo strutturato

Per output JSON/schema/tabellari Diez deve privilegiare contratti strutturati nativi quando disponibili, invece di descrivere lo schema soltanto in prosa.

Il Prompt Compiler deve separare:

- istruzioni semantiche;
- schema/output contract;
- vincoli di validazione;
- eventuali esempi;
- contesto materiale.

Anche qui le label UI italiane non devono diventare nomi di proprietà o istruzioni provider-facing salvo decisione esplicita dello schema editoriale.

## 8. Lingua del contenuto prodotto

La lingua dell'interfaccia e la lingua del contenuto sono due decisioni diverse.

Esempio:

- UI Diez: italiano;
- libro richiesto: inglese;
- Prompt provider-facing: inglese tecnico ottimizzato;
- output del libro: inglese.

Oppure:

- UI: italiano;
- Prompt provider-facing: inglese tecnico;
- output richiesto: italiano.

Il Prompt deve specificare chiaramente la lingua dell'output quando rilevante, senza confonderla con la lingua dell'interfaccia.

## 9. Prompt Preview

La pagina Prompt può mostrare all'utente il Prompt provider-facing reale, anche se è in inglese, perché quello è un artefatto tecnico destinato all'AI.

Tutte le spiegazioni attorno al Prompt, gli stati, i pulsanti, gli errori e le annotazioni restano in italiano.

Se in futuro viene offerta una vista “spiegami il Prompt”, quella spiegazione deve essere in italiano e derivata semanticamente dal Prompt/snapshot; non deve sostituire il Prompt reale.

## 10. Criteri di regressione

Una modifica futura è errata se:

1. cambia una label UI e cambia il comportamento del Prompt senza che sia cambiato lo stato canonico;
2. il Prompt contiene copie letterali di descrizioni UI che non sono state pensate come testo utente canonico;
3. il Prompt è una traduzione quasi parola-per-parola dell'interfaccia;
4. due provider ricevono lo stesso testo per inerzia quando le loro capacità richiedono contratti diversi;
5. il significato di una scelta viene perso durante la riscrittura;
6. un HARD dell'utente viene declassato a preferenza;
7. testo orchestrativo/UI finisce nel renderer-facing prompt;
8. la lingua dell'interfaccia viene confusa con la lingua dell'output editoriale.

## 11. Formula sintetica

**UI italiana ≠ Prompt italiano da tradurre.**

**UI italiana → significato canonico → prompt engineering ottimizzato.**
