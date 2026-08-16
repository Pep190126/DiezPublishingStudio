# Diez Publishing Studio — current handoff

Last updated: **2026-08-16**  
Working branch: `spike/uno-platform-ui`

Questo file è una memoria operativa breve per nuove chat/sessioni. Per gli invarianti storici Avalonia leggere anche `PROJECT_STATE.md`; per la migrazione Uno e il prodotto corrente usare prioritariamente questo handoff e le specifiche collegate sotto.

## 1. Priorità attuale

**PRIMA completare e verificare il percorso dei libri con immagini.**

Ordine attuale:

1. Coloring Book — l'utente sta testando la demo/installer Uno;
2. Raccolta immagini — stessa filosofia visuale adattata alla famiglia;
3. Libro illustrato — stessa pipeline immagini integrata al contenuto editoriale;
4. soltanto dopo, Word Search;
5. poi Cruciverba e le altre famiglie.

Non iniziare nuove implementazioni Word Search finché l'utente non dice esplicitamente qualcosa equivalente a **“procedi con Word Search”**.

## 2. Riferimenti persistenti appena ricostruiti

Leggere prima di modificare le aree relative:

- `docs/legacy/DIEZ_COLORING_LEGACY_REFERENCE_SPEC.md`
  - parità del vecchio Diez Coloring;
  - quattro fasi sequenziali;
  - materiali/reference;
  - anteprima reale materiale + risultato AI;
  - profilo Coloring, Bold & Easy, Cozy, specifiche immagine, Vision;
  - zona Scene da aggiungere rispetto al riferimento allegato.

- `docs/product/IMAGE_BOOK_UX_CONTRACT.md`
  - contratto Uno per Coloring/Raccolta immagini/Libro illustrato;
  - sidebar a macrovoci;
  - layout full-screen responsivo;
  - stepper sequenziale nel workspace;
  - preview come componente di prima classe;
  - differenze fra le tre famiglie.

- `docs/legacy/WORD_SEARCH_LIST_MANAGER_ANCESTOR_SPEC.md`
  - analisi statica dell'antenato Word Search;
  - DATABASE → FILTRI → GENERA → CONTROLLO → ESPORTA;
  - mapping flessibile, filtri, generazione a blocchi, sostituzioni, export Titans;
  - specifica congelata fino ad autorizzazione esplicita.

## 3. Direttive UX correnti per i libri con immagini

- Le **4 fasi Coloring devono procedere in sequenza** come nella linea Diez precedente.
- La sidebar è accettata, ma deve mostrare **macrovoci**, non tutti i tipi di libro e non le quattro fasi.
- Esempio strutturale: `Tipo libro` è una macrovoce; i tipi effettivi si scelgono al suo interno.
- Il layout va distribuito meglio e deve sfruttare l'intera schermata.
- L'anteprima immagine deve funzionare sia per **materiale aggiunto/importato** sia per **immagini create dall'AI con il Prompt generato da Diez**.
- Raccolta immagini e Libro illustrato devono adottare la stessa filosofia, con controlli e gate specifici per la famiglia.
- La zona Scene va integrata senza diventare una quinta fase né una voce globale separata.

## 4. Invarianti Core da non regredire

- SceneId stabile e non riciclabile.
- Partecipazione keyed `SubjectId + SceneId`.
- Scene-local environment prevale sull'ambiente generico.
- Prompt Compiler 3.6: provider-facing visual prompt = ART DIRECTION sintetizzata + HARD locks; niente routing/retry/session/internal metadata.
- Vision HARD: subject, single composition e gate profilo (stile, Bold & Easy, Cozy, line weight, scene participants quando applicabile).
- Ogni HARD fail blocca approvazione.
- `Approva` e `Porta nel libro` restano separati.
- Il package `.diez` deve preservare dati/entry sconosciuti durante migrazioni.

## 5. Cross-platform

Ogni claim desktop/cross-platform richiede almeno:

- Windows;
- macOS;
- Linux.

La CI Uno corrente costruisce/testa sui tre sistemi. Esistono già packaging reali sperimentali: Setup Windows, DMG macOS Intel/Apple Silicon e DEB Linux. Non dichiarare un nuovo head verde finché il relativo run non è terminato con successo.

## 6. Word Search — stato sospeso ma memoria conservata

Capacità già implementate nel Core non autorizzano a continuare ora il frontend Word Search. Restano disponibili, fra le altre:

- controllo duplicati whole-book;
- sostituzione locale con revalidazione globale;
- final gate quantità/word count/approvazione/KDPSAFE;
- database XLSX Diez reimportabile;
- handoff Self Publishing Titans XLSX + CSV in corso di consolidamento.

La priorità prodotto resta il percorso immagini finché l'utente non lo considera verificato.

## 7. Sicurezza PR

PR **#37** deve rimanere:

- open;
- draft;
- unmerged.

Non modificarla o mergiarla senza istruzione esplicita dell'utente.

## 8. Protocollo per una nuova chat

Prima di modificare codice:

1. leggere questo file;
2. leggere `docs/product/IMAGE_BOOK_UX_CONTRACT.md`;
3. se si lavora sul visuale, leggere `docs/legacy/DIEZ_COLORING_LEGACY_REFERENCE_SPEC.md`;
4. se e solo se l'utente ha autorizzato Word Search, leggere `docs/legacy/WORD_SEARCH_LIST_MANAGER_ANCESTOR_SPEC.md`;
5. fetchare l'head reale di `spike/uno-platform-ui` e lo stato CI corrente;
6. non assumere che un run precedente valga per l'head nuovo;
7. non toccare PR #37.
