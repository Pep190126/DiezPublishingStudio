# Diez — Importa impostazioni da altro progetto

Status: **SPECIFICA DI PRODOTTO / ARCHITETTURA — HARD, NON CONSOLIDATA**

Data: 2026-08-24

Branch: `spike/uno-platform-ui`

## 1. Decisione

Nella schermata **Progetto** deve essere disponibile un'azione:

**Importa impostazioni da altro progetto**

Questa funzione **non clona un progetto esistente**.

Crea invece un progetto nuovo, con identità e stato interno completamente nuovi, precompilando soltanto ciò che l'utente aveva già inserito o scelto esplicitamente a video nel progetto sorgente.

Formula sintetica:

**nuovo progetto ex novo + replay delle sole decisioni visibili dell'utente**.

Tutto ciò che è "sottocoperta" non viene importato.

## 2. Cosa si importa

La whitelist funzionale comprende soltanto valori che rappresentano azioni esplicite dell'utente nell'interfaccia:

- titolo del progetto/libro;
- Tipo libro;
- scelte effettuate tramite controlli UI: toggle, selettori, radio, combo, quantità, opzioni, profili e modalità;
- testo libero scritto dall'utente;
- `DEVE FARE` e `NON DEVE FARE`;
- descrizioni di soggetti, scene, ambiente, obiettivi e note quando sono state scritte direttamente dall'utente;
- scelte relative a stile, complessità, line weight, Bold & Easy, Cozy, Consistent e analoghe decisioni visibili;
- eventuali stati decisionali scelti dall'utente come `Definito`, `Proponilo con AI`, `Derivalo`, `Più avanti`, `Non applicabile`.

Il principio non è copiare campi tecnici dal JSON. Il principio è ricostruire nel nuovo progetto **la superficie decisionale che l'utente aveva già compilato a video**.

## 3. Cosa NON si importa

Non devono essere importati:

- `ProjectId` del progetto sorgente;
- Job ID, Work Unit ID, Prompt Pack ID, Request Snapshot ID o altri identificatori interni;
- materiali e file allegati/incorporati;
- ruoli o riferimenti legati a materiali che non esistono nel nuovo progetto;
- cronologia progetto, checkpoint, undo/redo history;
- Prompt compilati, Prompt Pack, Response, manifest o snapshot;
- job AI;
- Candidate, versioni AI, risultati importati o asset generati;
- stato Vision, check Vision, approvazioni o rifiuti;
- contenuti applicati al Master provenienti dall'AI;
- stato di pubblicazione, freeze o publication candidate;
- cache, hash, provenance tecnica, audit trail e metadata di trasporto;
- valori derivati automaticamente dal vecchio progetto;
- valori inferiti dall'AI o dai materiali;
- qualsiasi altro dato che non corrisponda a una scelta o a testo inserito direttamente dall'utente nell'interfaccia.

## 4. AI propose / derive / later

Quando l'utente aveva scelto uno stato come:

- `Proponilo con AI`;
- `Derivalo dal progetto/materiali`;
- `Decidi più avanti`;

si importa **la decisione dell'utente di usare quello stato**, non il risultato che il vecchio progetto aveva eventualmente prodotto dopo.

Esempio:

- nel progetto A l'utente sceglie `Proponilo con AI` per la struttura;
- l'AI produce 12 capitoli;
- creando il progetto B con `Importa impostazioni da altro progetto`, si importa `Proponilo con AI`;
- **non** si importano i 12 capitoli prodotti nel progetto A.

Il progetto B dovrà produrre o derivare nuovamente il risultato nel proprio contesto.

## 5. Nuova identità obbligatoria

Il progetto destinatario deve essere realmente nuovo.

Diez deve quindi generare nuovi:

- ProjectId;
- ID di soggetti e scene quando tali oggetti devono essere ricreati;
- ID di entità canoniche;
- ID di job e Work Unit quando verranno successivamente creati;
- snapshot e versioni.

Nessuna identità interna del progetto sorgente deve sopravvivere per semplice copia.

## 6. Soggetti e scene

Se soggetti o scene sono stati **definiti direttamente dall'utente a video**, possono essere ricreati nel nuovo progetto con lo stesso testo/semantica ma con nuovi ID.

Se invece sono stati:

- proposti dall'AI;
- derivati da materiali;
- creati come risultato di una precedente produzione;

non vengono copiati come contenuto già risolto, salvo che esista in futuro un'esplicita funzione distinta di modello/template approvato dall'utente.

Questa regola è coerente con il principio già adottato per i template scena: riuso esplicito, copia semantica, **nuovi SubjectId/SceneId**.

## 7. Materiali

I materiali non vengono mai importati da questa funzione.

Questo include:

- file;
- embedded asset;
- preview;
- extracted text;
- hash;
- policy AI collegate a uno specifico materiale;
- ruoli editoriali specifici del materiale;
- riferimenti `REFERENCE_ONLY`, `DIRECT_ASSET`, `NEVER_SEND`, ecc. quando dipendono da un file concreto.

Il nuovo progetto parte senza materiali.

## 8. Cronologia e produzione AI

Il nuovo progetto deve apparire come appena creato.

Pertanto deve avere:

- cronologia vuota;
- nessun checkpoint ereditato;
- nessun Prompt Pack precedente;
- nessun Response precedente;
- nessuna Candidate;
- nessuna approvazione;
- nessun asset applicato;
- nessuna pubblicazione precedente.

La sola differenza rispetto a un progetto completamente vuoto è che le decisioni che l'utente avrebbe dovuto reinserire a video risultano già valorizzate.

## 9. Regola di implementazione: whitelist semantica

L'importazione deve essere implementata come **whitelist semantica**, non come clone JSON seguito da una lista di cancellazioni.

Procedura concettuale:

1. crea un progetto nuovo tramite il normale percorso di creazione;
2. legge il progetto sorgente;
3. estrae esclusivamente le decisioni/user-authored fields ammesse;
4. applica tali decisioni attraverso i normali servizi canonici del progetto nuovo;
5. rigenera eventuali oggetti canonici con nuovi ID;
6. non copia nessun altro nodo interno;
7. lascia a Prompt Compiler, AI, derivazioni e materiali il compito di ricostruire da zero ciò che serve.

Questo evita che nuovi campi interni aggiunti in futuro vengano accidentalmente importati.

## 10. UI prevista

Nella schermata **Progetto**, durante o subito dopo la creazione di un nuovo progetto, prevedere l'azione:

**Importa impostazioni da altro progetto**

Il testo esplicativo in UI deve chiarire in italiano:

> Copia titolo, Tipo libro, scelte e testi inseriti da te. Materiali, cronologia, Prompt, risultati AI e dati interni non vengono importati.

Dopo la selezione del `.diez` sorgente Diez deve mostrare un riepilogo semplice di ciò che verrà importato prima di applicarlo, senza esporre ID o dettagli interni.

## 11. Criteri di regressione

La funzione è errata se:

1. il nuovo progetto conserva il `ProjectId` del vecchio;
2. compare un materiale del progetto sorgente;
3. compare un job, Candidate, Response, Prompt Pack o snapshot precedente;
4. la cronologia non è vuota;
5. vengono copiate identità interne di soggetti/scene invece di rigenerarle;
6. viene importato un risultato proposto/derivato dall'AI quando l'utente aveva scelto solo `Proponilo` o `Derivalo`;
7. una scelta o un testo realmente inserito dall'utente viene perso;
8. l'importazione viene implementata copiando l'intero JSON e tentando poi di ripulirlo;
9. un campo interno futuro viene importato automaticamente senza essere stato aggiunto esplicitamente alla whitelist semantica.

## 12. Formula finale

**Importa ciò che l'utente ha deciso o scritto.**

**Non importare ciò che Diez, l'AI, i materiali, la cronologia o il protocollo hanno costruito dietro le quinte.**
