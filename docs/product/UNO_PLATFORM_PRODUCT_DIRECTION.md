# Diez — direzione UI Uno Platform

Status: **DECISIONE DI PRODOTTO + VALIDAZIONE FISICA PARZIALE**

Data: 2026-08-18

## 1. Decisione di prodotto

Dopo prova reale dell'app installata, l'utente considera **Uno Platform nettamente migliore di Avalonia per la direzione UI di Diez**.

Conseguenza operativa:

- le nuove specifiche UX e i nuovi percorsi guidati devono essere progettati per Uno Platform;
- il Core rimane UI-neutral e non deve essere duplicato;
- Avalonia resta riferimento storico/di parità finché serve alla migrazione, ma non deve dettare la nuova UX;
- evitare nuovi investimenti strutturali nell'interfaccia Avalonia salvo necessità di migrazione, confronto o recupero funzionale;
- la priorità è rendere Uno il frontend editoriale principale attraverso prove fisiche successive.

Questa decisione riguarda la **direzione prodotto/UI**. Non implica che ogni funzione Uno sia già consolidata.

## 2. Validazione fisica Response — Windows

L'utente ha eseguito il retest fisico sul PC Windows con la build Uno Preview contenente il fix di import Response.

Esito comunicato:

- il Response precedentemente fallito ora viene importato;
- dopo l'import compare l'anteprima della Candidate.

Per questo caso reale è quindi validato il percorso:

`File picker StorageFile → lettura/copia byte-per-byte → riconoscimento Response → import Candidate → anteprima`

Il precedente errore `MANIFEST_MISSING` osservato sullo stesso Response è da considerare **risolto per il caso fisico testato**.

Questa validazione non consolida automaticamente:

- qualità semantica delle immagini;
- Prompt Compiler per nuove generazioni;
- organizzazione UX Coloring 1–4;
- Vision completa;
- altri formati/provider Response non provati fisicamente;
- comportamento macOS/Linux non provato dall'utente.

## 3. Prossima priorità

Prima di produrre nuovi Prompt Pack, la priorità è stabilizzare il **metodo di costruzione del Prompt tramite scelte utente** per ogni Tipo libro.

Ordine di lavoro:

1. rivedere Coloring 1–4 con le note raccolte dall'utente nell'app installata;
2. definire percorsi guidati per tutte le famiglie;
3. fissare la matrice delle decisioni canoniche;
4. costruire i componenti Uno riusabili;
5. collegare le decisioni a un Prompt System compositivo unico;
6. solo dopo stabilizzare Prompt Pack/Response/validatori per le altre famiglie.

Riferimenti:

- `docs/product/PROMPT_SYSTEM_ARCHITECTURE_SPEC.md`
- `docs/product/BOOK_FAMILY_GUIDED_FLOWS_SPEC.md`
- `docs/product/BOOK_FAMILY_DECISION_MATRIX_SPEC.md`
- `docs/product/SPEC_CONSOLIDATION_MEMO.md`
