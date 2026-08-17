# Word Search — Scene semantiche, tassonomia e varianti temporali

Status: **DIRETTIVA DI PRODOTTO / SPECIFICA DA PRESERVARE — NON IMPLEMENTARE FINCHÉ L'UTENTE NON AUTORIZZA ESPLICITAMENTE IL LAVORO WORD SEARCH**

Questa specifica integra, senza sostituirla, `docs/legacy/WORD_SEARCH_LIST_MANAGER_ANCESTOR_SPEC.md`.

## 1. Capacità da non perdere

Word Search non deve limitarsi a estrarre parole appartenenti a una categoria. La tassonomia associata alle parole deve poter descrivere **scene semantiche riconoscibili**, cioè insiemi di parole che, lette insieme, ricostruiscono una situazione, un momento, un ambiente o un'esperienza coerente.

Esempi nostalgici:

- pranzo di Natale;
- primo giorno di scuola;
- una giornata al mare;
- il sabato pomeriggio in centro;
- la cucina della nonna;
- il viaggio delle vacanze;
- una festa di compleanno;
- la domenica allo stadio;
- il ritorno a scuola dopo l'estate.

Una scena non è quindi una semplice `category`: è un **criterio editoriale di composizione** che può attraversare più categorie e sottocategorie del database.

## 2. Nostalgic Word Search: stessa scena, decadi diverse

Per un Nostalgic Word Search deve essere possibile definire una scena editoriale e riprodurla in più anni o decadi, usando le parole appropriate a ciascun periodo.

Esempio concettuale:

`Primo giorno di scuola` + `anni 1960`

non deve necessariamente produrre lo stesso lessico di:

`Primo giorno di scuola` + `anni 1980`

oppure:

`Primo giorno di scuola` + `anni 2000`.

La scena rimane semanticamente la stessa, ma oggetti, prodotti, abitudini, tecnologia, linguaggio, mezzi di trasporto, abbigliamento, media e altri dettagli possono cambiare in base al periodo.

Il database canonico deve quindi poter usare, quando disponibili, dimensioni come:

- scena / situazione / occasione;
- categoria;
- sottocategoria;
- anno o intervallo di anni;
- decade primaria o decadi compatibili;
- rilevanza per la scena;
- nostalgia / forza evocativa;
- eventuali altri assi tassonomici importati dal database.

I nomi concreti delle colonne restano mappabili e non devono essere hard-coded: conta il ruolo semantico, coerentemente con la filosofia di WordSearchListManager.

## 3. Scene come query/composizione, non come duplicazione del database

Una scena non deve richiedere di duplicare le stesse parole in tabelle o liste separate.

La scena è una **definizione di selezione/composizione** applicata al lessico canonico. Può indicare, ad esempio:

- quali tassonomie sono pertinenti;
- quali anni/decadi sono ammessi;
- quante parole attingere dai diversi gruppi;
- eventuali parole obbligatorie, preferite o escluse;
- livello minimo di rilevanza;
- KDPSAFE e altri filtri;
- regole di bilanciamento della lista.

In questo modo lo stesso database può sostenere molte scene senza perdere una sola fonte di verità.

## 4. Generazione per matrice `scena × periodo`

Il generatore deve poter lavorare su una matrice editoriale.

Esempio:

- scena: `Pranzo di Natale`;
- periodi: `1950s`, `1960s`, `1970s`, `1980s`;
- output: quattro puzzle distinti, ciascuno semanticamente riconoscibile come Pranzo di Natale ma storicamente coerente con la decade assegnata.

La stessa capacità deve supportare più scene e più periodi nello stesso libro.

L'utente deve poter scegliere se:

- produrre una sola combinazione;
- produrre tutte le decadi selezionate per una scena;
- produrre una griglia di più scene × più periodi;
- usare il periodo come vincolo stretto oppure come preferenza quando i dati disponibili sono insufficienti.

Le eventuali strategie di fallback devono essere visibili e controllabili, mai silenziose.

## 5. Unicità whole-book resta HARD

La generazione per scena e periodo **non indebolisce il vincolo di non duplicazione**.

Se il progetto vieta i riusi, una parola usata in:

`Pranzo di Natale — anni 1960`

non può ricomparire automaticamente in:

`Pranzo di Natale — anni 1970`,

né in una scena diversa dello stesso libro.

Il dominio di unicità rimane l'intero libro, come già stabilito nella specifica Word Search.

Quando una combinazione scena/periodo non dispone di abbastanza parole uniche:

1. Diez deve segnalarlo;
2. deve indicare quali vincoli stanno esaurendo il pool;
3. può proporre alternative coerenti;
4. il riuso può avvenire soltanto se la policy del progetto lo consente esplicitamente.

La sostituzione di una parola resta chirurgica sul singolo puzzle/posizione e viene rivalidata contro tutto il libro.

## 6. Coerenza semantica della scena

Una lista non è valida soltanto perché ogni parola soddisfa individualmente i filtri.

Il controllo deve poter verificare anche la **coerenza d'insieme** della scena, per esempio:

- copertura sufficiente dei diversi aspetti della situazione;
- assenza di parole formalmente compatibili ma fuori contesto;
- eccessiva concentrazione su un'unica sottocategoria;
- adeguatezza storica rispetto ad anno/decade;
- forza evocativa/nostalgica quando il progetto la richiede.

L'AI può aiutare a proporre o valutare la composizione, ma non sostituisce i vincoli deterministici del database, della tassonomia e dell'unicità whole-book.

## 7. Generalizzazione oltre il Nostalgic Word Search

Il concetto di scena/contesto deve essere riutilizzabile anche per Word Search non nostalgici.

La dimensione temporale è una specializzazione, non il fondamento del modello.

Esempi:

- `Giornata in campeggio` × fascia d'età;
- `Vita in fattoria` × stagione;
- `Cucina italiana` × regione;
- `Animali della barriera corallina` × habitat/profondità;
- `Una giornata in ospedale` × ruolo professionale;
- `Viaggio in Giappone` × città/contesto;
- `Calcio` × ruolo/fase della partita;
- `Spazio` × missione/pianeta/argomento;
- `Halloween` × livello di difficoltà;
- `Primo giorno di scuola` × paese/cultura.

Il modello generale è quindi:

**contesto editoriale + assi tassonomici + eventuale periodo + regole di composizione + unicità whole-book**.

## 8. UX nel percorso Word Search

Questa capacità deve inserirsi nel percorso storico:

`DATABASE → FILTRI → GENERA → CONTROLLO → ESPORTA`.

Collocazione funzionale proposta:

### DATABASE

- mappatura dei ruoli tassonomici, incluso anno/decade quando presenti;
- visualizzazione/editing dei metadata;
- nessun obbligo di nomi colonna prestabiliti.

### FILTRI

- scelta degli assi tassonomici rilevanti;
- anteprima del pool per scena/periodo;
- possibilità di salvare un filtro/composizione come **Scenario** o equivalente editoriale.

### GENERA

- selezione scena o insieme di scene;
- selezione anno/decade o altro asse variabile;
- generazione matrice scena × variante;
- distribuzione bilanciata delle parole;
- rispetto dell'unicità whole-book.

### CONTROLLO

- duplicati globali;
- parole troppo lunghe;
- mismatch temporali/tassonomici;
- coerenza semantica della scena;
- sostituzioni contestuali che mantengano sia la scena sia la variante assegnata.

### ESPORTA

- il tema/titolo del puzzle deve poter conservare la scena e, quando applicabile, la variante (`Pranzo di Natale — anni 1970`);
- database Diez completo e reimportabile conserva i metadata necessari a rigenerare/controllare le liste.

## 9. Rapporto con Prompt/AI

Quando Word Search userà il sistema Prompt Diez, il Prompt non deve ricevere una lista piatta di parole senza contesto.

Il profilo strutturato deve poter comunicare semanticamente almeno:

- scena/contesto richiesto;
- variante temporale o tassonomica;
- pubblico e difficoltà quando applicabili;
- numero di puzzle e parole per puzzle;
- criteri di selezione;
- pool/metadata disponibili o istruzioni per rispettarli;
- divieto whole-book di duplicazione;
- richiesta di coerenza semantica e, per Nostalgic, storica.

L'AI può proporre titoli, composizioni, descrizioni e sostituzioni, ma i risultati rientrano nel normale ciclo Candidate → controllo → applicazione.

## 10. Acceptance contract futuro

Quando l'implementazione Word Search verrà autorizzata, tra i test obbligatori dovrà esserci almeno:

1. database con parole tassonomizzate e metadata temporali;
2. definizione della scena `Primo giorno di scuola`;
3. generazione della stessa scena per almeno tre decadi;
4. differenze lessicali coerenti tra le decadi;
5. zero duplicati tra tutti i puzzle quando `NoDuplicates = ON`;
6. sostituzione di una parola con alternativa valida per la stessa scena/decade;
7. revalidazione whole-book;
8. salvataggio/riapertura `.diez` senza perdita di scenario, tassonomie e associazioni;
9. export e reimport del database ricco senza perdita dei metadata temporali;
10. secondo test non nostalgico che dimostri che il meccanismo non è hard-coded sulle decadi.

## 11. Principio da preservare

Il valore editoriale del Word Search Diez non è soltanto evitare duplicati o riempire puzzle: è poter trasformare un database ricco in **liste che raccontano implicitamente una scena, un'epoca o un contesto**, mantenendo controllo, tracciabilità e varietà sull'intero libro.
