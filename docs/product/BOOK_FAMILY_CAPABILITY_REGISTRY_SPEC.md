# Diez — registro delle capability riusabili per famiglia libro

Status: **SPECIFICA ARCHITETTURA / PRODOTTO — WORKING, NON CONSOLIDATA**

Data: 2026-08-18

Documenti collegati:

- `PROMPT_SYSTEM_ARCHITECTURE_SPEC.md`
- `BOOK_FAMILY_GUIDED_FLOWS_SPEC.md`
- `BOOK_FAMILY_DECISION_MATRIX_SPEC.md`
- `WORD_SEARCH_SEMANTIC_SCENES_SPEC.md`

## 1. Scopo

Questa specifica rende esplicita la parte riusabile dell'architettura multi-libro.

Un Tipo libro non deve essere implementato come una schermata speciale più un Prompt Engine speciale. Deve essere descritto come una combinazione di **capability editoriali** condivise, più un piccolo insieme di regole realmente specifiche.

La capability è una capacità del prodotto, non un controllo UI e non una funzione del provider AI.

Esempio:

- `StructuredOutline` significa che il libro possiede una struttura ordinata modificabile;
- Uno può renderla con un tree editor;
- il Prompt Compiler può usarla come contesto;
- il Response importer può associare Candidate ai nodi;
- la review può controllare copertura e coerenza.

Una sola capability, più consumatori.

---

## 2. Regole del registro

Ogni capability futura deve dichiarare almeno:

- **Key stabile** — non dipendente dalla label UI;
- **Scopo editoriale**;
- **Dati canonici minimi**;
- **Dipendenze** da altre capability;
- **Componenti UI** che la possono rendere;
- **sezioni Prompt** che può alimentare;
- **tipo di Response** che può produrre/ricevere;
- **validator/reviewer** applicabili;
- **scope**: progetto, struttura, unità, whole-book;
- **presenza obbligatoria/opzionale** per famiglia.

La UI può cambiare copy, ordine o layout senza cambiare le chiavi canoniche.

---

## 3. Capability di progetto

### `ProjectIdentity`

Titolo, Tipo libro, lingua, edizione e identità editoriale di base.

Usata da tutte le famiglie.

### `AudienceAndPurpose`

Pubblico, livello, obiettivo, promessa editoriale, uso previsto.

Non deve obbligare tutte le famiglie ad avere gli stessi campi: definisce il concetto comune.

### `DecisionState`

Supporta decisioni non necessariamente note all'inizio:

- `Defined`
- `Propose`
- `Derive`
- `Later`
- `NotApplicable`

È trasversale soprattutto a long-form e libri illustrati.

### `SourceMaterials`

Materiali importati, reference, fonti e allegati usati come contesto o grounding.

La provenienza deve restare distinguibile dall'output AI.

---

## 4. Capability strutturali e long-form

### `StructuredOutline`

Struttura ordinata gerarchica con ID stabili.

Azioni comuni:

- aggiungi;
- elimina;
- rinomina;
- riordina;
- sposta;
- duplica;
- importa;
- confronta proposta AI.

### `Parts`

Livello opzionale sopra capitoli/sezioni.

### `ChaptersSections`

Nodi principali del testo long-form.

### `NarrativeScenes`

Scene narrative interne a capitoli, con POV, luogo, tempo, partecipanti, obiettivo e beat.

Da non confondere con le Scene visuali: possono condividere identità/partecipanti, ma hanno scopi editoriali diversi.

### `EditableMasterText`

Testo canonico modificabile del libro.

Candidate AI e originali importati non devono sovrascriverlo implicitamente.

### `CandidateTextVersions`

Più versioni di testo per nodo/unità, con stato e confronto.

### `BibleContinuity`

Entità, fatti canonici, relazioni, timeline, terminologia e regole da preservare.

### `FactAndSourceGrounding`

Fonti, citazioni, terminologia e controllo fattuale per saggio/manuale e altri contenuti informativi.

---

## 5. Capability visuali

### `VisualSlots`

Numero/posizioni che richiedono immagini.

`ImageCount` è una decisione naturale soltanto quando questa capability è attiva.

### `VisualSubjects`

Soggetti/personaggi/oggetti che devono apparire.

### `VisualScenes`

Scene visuali con ambiente locale, partecipanti e descrizione. La scena è contenuto editoriale, non metadata del provider.

### `VisualConsistency`

Identità, aspetto, scala o altre proprietà da mantenere coerenti lungo una serie.

### `VisualReferences`

Paradigmi/reference associabili a progetto, soggetto, scena o unità.

### `VisualPromptProfile`

Scelte di resa visuale: stile, viewpoint, dettaglio, colore, sfondo e parametri specifici di famiglia.

### `ColoringConstraints`

Specializzazione Coloring:

- B/N puro;
- colorabilità;
- aree chiuse;
- clean contours;
- no micro-aree inappropriate;
- no testo;
- line weight;
- Bold & Easy;
- Cozy;
- HARD visuali.

Non deve essere attivata automaticamente per Raccolta immagini o Libro illustrato.

### `ImageCandidateReview`

Preview, Vision, Candidate versions, approvazione e applicazione.

---

## 6. Capability puzzle / lessico

### `SourceLexicon`

Database lessicale canonico importabile/editabile con preservazione dei metadata.

### `FlexibleColumnMapping`

Mappa ruoli semantici senza imporre nomi di colonna rigidi.

### `TaxonomyFilters`

Filtri tassonomici dinamici basati sul dataset.

### `SemanticScenarioComposition`

Definisce scene/contesti editoriali come query/composizioni sopra tassonomie e metadata.

Esempio Nostalgic Word Search:

`Pranzo di Natale × decade`.

La capability non è hard-coded sulla nostalgia: può usare regione, stagione, habitat, ruolo, difficoltà o altri assi.

### `VariantAxis`

Asse che produce varianti coerenti della stessa scena/contesto:

- anno;
- decade;
- regione;
- stagione;
- fascia d'età;
- difficoltà;
- altro ruolo tassonomico mappato.

### `PuzzleBatchGeneration`

Generazione di più puzzle con blocchi e quantità controllate.

### `WholeBookUniqueness`

Vincolo globale di unicità degli elementi usati nell'intero libro.

È HARD quando la policy `NoDuplicates` è attiva.

### `ContextualReplacement`

Sostituzione chirurgica che conserva i vincoli della posizione e rivalida il whole-book.

### `ExternalPuzzleHandoff`

Export verso strumenti esterni, per esempio Self Publishing Titans o Qxw, senza trasformarli nella fonte di verità Diez.

---

## 7. Capability quiz

### `QuestionBank`

Raccolta canonica di domande strutturate.

### `AnswerOptions`

Opzioni/distrattori con risposta corretta esplicita.

### `QuestionDifficultyDistribution`

Bilanciamento della difficoltà sul libro/blocco.

### `QuestionEvidence`

Fonte/provenienza e cutoff temporale quando richiesti.

### `QuestionQualityReview`

Controlla ambiguità, più risposte corrette, distrattori deboli, duplicati semantici e supporto della risposta.

---

## 8. Capability dati/cataloghi

### `DataSchema`

Definizione di campi, tipi, obbligatorietà, descrizioni ed esempi.

### `RecordCandidates`

Record importati o generati che devono essere validati prima dell'applicazione.

### `Deduplication`

Individuazione e gestione dei duplicati.

### `Normalization`

Uniformazione controllata di valori e formati.

### `Provenance`

Origine, data, fonte, affidabilità e note per record/campo quando applicabile.

### `SchemaValidation`

Controlla conformità, missing, tipi e vincoli.

---

## 9. Capability Prompt / AI comuni

### `PromptCompilation`

Compilazione dello stato canonico in testo provider-facing leggibile.

Un solo framework di compilazione, profili diversi.

### `PromptOverride`

Modifiche manuali separate dallo stato canonico con rilevazione `stale`.

### `UnitPrompt`

Prompt specializzato per un'unità editoriale: scena, capitolo, immagine, puzzle, domanda, lotto dati.

### `PromptPackTransport`

Packaging di snapshot, Work Unit, asset e contract di Response.

### `ManualPromptTransport`

Copia/incolla dello stesso lavoro editoriale senza Prompt Pack.

### `ResponseIngress`

Import comune del Response con identità e provenienza preservate.

### `CandidateLifecycle`

Stati comuni:

- importato;
- da controllare;
- revisionato;
- approvato;
- scartato;
- applicato al libro.

L'esatto vocabolario UI può variare, ma approvazione e applicazione non devono essere confuse.

---

## 10. Matrice iniziale per famiglia

Legenda:

- `R` = richiesta dalla famiglia;
- `O` = opzionale/attivabile;
- `—` = non pertinente per default.

| Capability | Coloring | Raccolta immagini | Illustrato | Romanzo | Saggio/manuale | Word Search | Cruciverba | Quiz | Catalogo dati |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| ProjectIdentity | R | R | R | R | R | R | R | R | R |
| AudienceAndPurpose | R | R | R | R | R | O | R | R | R |
| StructuredOutline | — | O | R | R | R | — | — | O | O |
| EditableMasterText | — | O | R | R | R | — | — | — | O |
| BibleContinuity | O | O | O | R | O | — | — | — | — |
| VisualSlots | R | R | R | O | O | — | — | — | O |
| VisualScenes | O | O | O | O | O | — | — | — | — |
| VisualConsistency | O | O | O | O | O | — | — | — | — |
| ColoringConstraints | R | — | — | — | — | — | — | — | — |
| SourceLexicon | — | — | — | — | — | R | R | — | — |
| TaxonomyFilters | — | — | — | — | — | R | O | O | O |
| SemanticScenarioComposition | — | — | — | — | — | R | O | O | O |
| VariantAxis | — | — | — | — | — | R | O | O | O |
| WholeBookUniqueness | O | O | O | — | — | R | O | O | O |
| QuestionBank | — | — | — | — | — | — | — | R | — |
| DataSchema | — | O | O | — | O | O | O | O | R |
| Provenance | O | O | O | O | R | O | O | R | R |
| PromptCompilation | R | R | R | R | R | O | O | R | R |
| ResponseIngress | R | R | R | R | R | O | O | R | R |
| CandidateLifecycle | R | R | R | R | R | O | O | R | R |

Questa matrice non definisce la UI finale: serve a impedire duplicazioni architetturali.

---

## 11. Dipendenze importanti

- `CandidateTextVersions` richiede una unità/nodo stabile a cui agganciare la versione.
- `ImageCandidateReview` richiede asset identificabile e provenance.
- `SemanticScenarioComposition` richiede tassonomie/metadata disponibili, ma non una colonna chiamata `scene`.
- `VariantAxis` deve essere semanticamente mappabile dal dataset.
- `WholeBookUniqueness` opera sopra l'insieme canonico applicato, non soltanto sull'ultimo batch generato.
- `PromptPackTransport` dipende dallo snapshot del Prompt, non viceversa.
- `ResponseIngress` non deve modificare direttamente il Master senza passare dal lifecycle Candidate/review quando la famiglia lo richiede.

---

## 12. Regola per nuovi Tipi libro

Prima di aggiungere codice speciale per un nuovo Tipo libro, verificare:

1. quali capability già esistono;
2. quali possono essere composte;
3. quale nuova capability manca realmente;
4. se la nuova capability può essere utile anche ad altre famiglie.

Una nuova capability è preferibile a una nuova pipeline parallela quando il comportamento è semanticamente riusabile.

---

## 13. Gate di implementazione

Questa specifica autorizza progettazione e componentizzazione, **non** il redesign definitivo delle quattro pagine Coloring mentre l'utente sta completando la prova fisica e raccogliendo note.

Per Word Search resta valido il gate esplicito della specifica ancestor: nessuna nuova implementazione operativa finché l'utente non autorizza espressamente il lavoro Word Search.
