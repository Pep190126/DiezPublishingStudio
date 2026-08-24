# Round 5.4 — Planner semantico soggetti + stile Custom

Status: **IMPLEMENTATO IN CANDIDATA / NON CONSOLIDATO**

Data: 2026-08-24

Branch: `spike/uno-platform-ui`

## Obiettivo

Superare il limite fisicamente verificato in Round 5.3: Diez riconosce correttamente che `3 soggetti di Halloween` e il residuo legacy `3 immagini di soggetti per Halloween` descrivono una serie, ma fino a Round 5.3 non dispone di soggetti atomici e quindi blocca il Prompt Pack.

Round 5.4 introduce il passaggio mancante:

**decisione utente aggregata → planner semantico Diez → proposta strutturata → validazione → accettazione utente → soggetti canonici → Prompt Compiler → renderer immagini**.

Il renderer immagini non pianifica mai la serie.

## 1. Nessun hardcode tematico

È vietato introdurre tabelle del tipo:

`Halloween => zucca, fantasma, gatto`.

Diez isola semanticamente il tema e il numero richiesto, poi prepara una vera attività AI di pianificazione.

## 2. Planner separato dal renderer

Il planner è una Work Unit **TEXT** nel normale AI Exchange Diez.

Il suo compito è solo proporre soggetti canonici. Non deve:

- generare immagini;
- scrivere prompt per il renderer immagini;
- decidere layout, collage, griglie o pannelli;
- inventare ID o metadati di trasporto;
- applicare direttamente il risultato al libro.

Il Prompt del planner richiede JSON strutturato con, per ogni soggetto:

- `display_name_it`: etichetta editoriale visibile in italiano;
- `canonical_concept`: concetto semantico tecnico usato dal compilatore provider-facing;
- `description_it`: descrizione editoriale visibile;
- `canonical_description`: descrizione semantica tecnica corrispondente.

Questa separazione impedisce che l'etichetta italiana visibile diventi automaticamente testo del renderer.

## 3. Trasporto attuale

Alla data di questa tranche, il catalogo Core dichiara `SupportsDirectApi = false` per OpenAI, Gemini e Altra/nuova AI.

Di conseguenza Diez **non simula una API interna inesistente**.

Il flusso operativo Round 5.4 usa il trasporto già reale:

1. in Definizione l'utente sceglie `Prepara proposta soggetti con AI`;
2. Diez crea una attività AI testuale con il Prompt planner;
3. si apre `Produzione con AI`;
4. l'utente usa `Copia Prompt attività selezionata`;
5. esegue il Prompt con il provider scelto;
6. incolla il JSON ricevuto e lo importa come Candidate testuale;
7. torna in Definizione;
8. Diez valida la proposta;
9. `Accetta proposta e congela i soggetti` è un atto utente esplicito;
10. solo dopo l'accettazione vengono create le identità soggetto canoniche e il Prompt immagini può essere compilato.

Quando un executor API diretto sarà realmente configurato, potrà usare lo stesso contratto senza cambiare lo stato canonico o la UX decisionale.

## 4. Validazione della proposta

Prima dell'applicazione Diez deve verificare almeno:

- numero di soggetti esattamente uguale al numero richiesto;
- soggetti concreti e atomici;
- nomi distinti;
- concetti canonici distinti;
- campi obbligatori presenti;
- JSON valido;
- nessuna promozione di categorie, conteggi o placeholder a soggetto.

Una proposta non valida non modifica lo stato canonico.

## 5. Accettazione e freeze canonico

L'accettazione della proposta:

- è esplicita;
- approva la Candidate testuale del planner;
- crea un nuovo piano `MultiSubjectProfile` attivo;
- crea un `SubjectId` distinto per ogni soggetto;
- conserva il tema aggregato come contesto di gruppo, non come `PRIMARY SUBJECT`;
- salva separatamente etichetta visibile e concetto canonico;
- sblocca la compilazione delle Work Unit immagine atomiche.

Se l'utente modifica manualmente un soggetto dopo la proposta, il concetto semantico generato dal planner viene invalidato per quel soggetto: una vecchia semantica AI non può sopravvivere silenziosamente a una modifica dell'utente.

## 6. Prompt Compiler

Per un soggetto proposto dal planner:

- UI: può mostrare `Zucca jack-o'-lantern simpatica`;
- stato canonico: conserva anche `friendly jack-o'-lantern pumpkin` come concetto semantico;
- renderer: riceve il concetto canonico e non l'etichetta UI italiana come sorgente testuale.

Lo stesso principio vale per descrizioni e per le etichette Consistent.

## 7. Compatibilità progetto legacy / nuovo

Il planner si innesta dopo il gate Round 5.3.

Quindi sia:

- `3 soggetti di Halloween` in un progetto nuovo;
- `3 immagini di soggetti per Halloween` in un progetto legacy riaperto;

seguono lo stesso percorso semantico e possono essere risolti prima del Prompt Pack.

## 8. Stile Custom

Quando l'utente seleziona `Custom` nel profilo Coloring, la UI deve mostrare:

- campo `Definizione stile Custom`;
- decisione `Usa solo in questo progetto`;
- decisione alternativa `Salva anche nella libreria degli stili`.

Regole:

- nessun salvataggio in libreria implicito;
- la definizione è sempre salvata nello stato del progetto quando Custom è attivo;
- la scelta di archiviazione è una decisione utente persistita;
- se archiviato, lo stile compare tra le opzioni Custom riutilizzabili;
- la libreria è locale e cross-project;
- selezionare uno stile archiviato recupera la sua definizione;
- il Prompt Compiler usa la definizione canonica Custom, non la parola `Custom` come stile finale.

## 9. Regression gate Round 5.4

`tests/Diez.VisualSemanticRegression` viene esteso per verificare:

1. Round 5.3 continua a bloccare il tema aggregato non risolto;
2. il planner crea una Work Unit TEXT distinta dal renderer;
3. il Prompt planner richiede JSON-only strutturato;
4. una risposta valida crea una Candidate testuale;
5. Diez valida esattamente il numero richiesto di soggetti;
6. l'accettazione crea soggetti canonici distinti;
7. il Prompt Pack immagine viene successivamente sbloccato;
8. il renderer riceve `canonical_concept`, non l'etichetta italiana;
9. il tema aggregato non sopravvive come `PRIMARY SUBJECT`;
10. una proposta con cardinalità errata viene rifiutata.

## 10. Consolidamento

Round 5.4 resta **NON CONSOLIDATO** finché non sono completati:

1. build Windows tecnicamente verificata da sorgente già committato, senza patch in CI;
2. installazione fisica della candidata;
3. test fisico del planner su progetto nuovo;
4. test fisico del planner su progetto 10 legacy;
5. ispezione di un Prompt Pack reale dopo l'accettazione dei soggetti;
6. nuovo Response reale e verifica del percorso Coloring/immagini.
