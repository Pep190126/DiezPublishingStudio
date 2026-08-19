# Diez Publishing Studio — Etichette publisher per Uso dell'AI e Fedeltà

Status: **DIRETTIVA UI UNO — NON CONSOLIDATA FINO A TEST FISICO**

## Principio

I codici tecnici salvati nel `.diez` e usati dai manifest (`ALLOW`, `REFERENCE_ONLY`, `DIRECT_ASSET`, `NEVER_SEND`, `EXACT`, `CLOSE`, `GUIDED`, `LOOSE`, `NOT_APPLICABLE`) non devono essere mostrati all'utente come etichette primarie.

La UI mostra italiano comprensibile e una descrizione breve del comportamento selezionato. Il codice interno resta invariato per compatibilità e audit.

## Uso dell'AI

| Codice interno | Etichetta UI | Descrizione breve |
|---|---|---|
| `ALLOW` | **Può usarlo e modificarlo** | L'AI può usare questo materiale come input operativo e trasformarlo secondo il ruolo e le istruzioni definite. |
| `REFERENCE_ONLY` | **Usalo solo come riferimento** | Il materiale guida identità, stile, composizione, ambiente o contenuto, ma non va trattato automaticamente come asset da copiare o inserire nel libro. |
| `DIRECT_ASSET` | **Usa il file direttamente** | Il file è già un asset editoriale: resta nel progetto/libro e non deve essere rigenerato automaticamente dall'AI. |
| `NEVER_SEND` | **Non inviare all'AI** | Il materiale resta disponibile nel progetto, ma viene escluso dai Prompt Pack e dagli input inviati all'AI. |

## Fedeltà

| Codice interno | Etichetta UI | Descrizione breve |
|---|---|---|
| `EXACT` | **Da rispettare esattamente** | Dati, termini, struttura o contenuto indicati sono vincolanti: non vanno reinterpretati liberamente. |
| `CLOSE` | **Molto fedele** | Mantieni identità e caratteristiche molto vicine al materiale originale, salvo le modifiche esplicitamente richieste. |
| `GUIDED` | **Fedele ma guidata** | Usa il materiale come base riconoscibile, con libertà controllata dalle istruzioni editoriali dell'utente. |
| `LOOSE` | **Solo ispirazione** | Conta l'idea, lo stile o l'atmosfera generale; non è richiesta una replica fedele del contenuto. |
| `NOT_APPLICABLE` | **Non applicabile** | Per questo ruolo non ha senso definire un livello di fedeltà. |

## Comportamento UI

- sotto il selettore **Uso dell'AI** compare sempre la descrizione della voce selezionata;
- sotto il selettore **Fedeltà** compare sempre la descrizione della voce selezionata;
- cambiando il `Ruolo editoriale`, Diez può proporre Uso dell'AI e Fedeltà coerenti con quel ruolo;
- l'utente può modificarli quando il ruolo lo consente;
- il salvataggio conserva i codici interni, non le etichette localizzate;
- `Non inviare all'AI` deve restare inequivocabile e impedire l'inclusione del materiale nei Prompt Pack;
- `Usa il file direttamente` non equivale a inviarlo all'AI: significa trattarlo come asset editoriale già disponibile.
