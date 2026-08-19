# Diez — Consistency visuale tra lotti diversi

Status: **DIRETTIVA ARCHITETTURALE / WORKING SPEC — DA VALIDARE CON TEST REALI**

## 1. Problema

La consistency non può valere soltanto dentro un singolo lotto di immagini.

Un personaggio, soggetto o elemento visuale canonico può comparire:

- in un primo Prompt Pack oggi;
- in un secondo lotto giorni dopo;
- in una rigenerazione correttiva;
- in un altro capitolo/scena;
- in un'altra fase della produzione dello stesso libro.

La sua identità deve rimanere coerente anche quando il provider non vede il lotto precedente.

## 2. Evidenza dalla linea Avalonia

La linea Avalonia aveva già due mattoni utili:

- un risultato poteva essere marcato **Da rifare**, mantenendo la storia del job;
- il batch immagini poteva esportare un nuovo XLSX con **solo immagini mancanti o da rifare**, preservando l'ID `IMG-###`.

Questa è una buona base di workflow, ma non basta per una consistency cross-lotto forte: ID e prompt del job preservano la posizione logica, non necessariamente l'identità visuale del personaggio.

## 3. Soluzione proposta: Character/Subject Identity Profile canonico

Ogni soggetto marcato `Consistent` deve avere un profilo persistente nel `.diez`, con ID stabile e indipendente dal lotto.

Il profilo può contenere:

- `SubjectId` stabile;
- nome editoriale;
- descrizione semantica canonica;
- caratteristiche HARD da non cambiare;
- caratteristiche flessibili;
- reference/paradigmi approvati;
- eventuali crop/viste utili;
- Candidate visuale scelta come riferimento principale;
- note di continuità;
- versioni del profilo.

Il prompt provider-facing traduce questo profilo in linguaggio naturale e allega/reference gli asset quando il trasporto/provider lo consente.

## 4. Identity Anchor

Quando una Candidate viene approvata come buona rappresentazione del soggetto, l'utente deve poterla promuovere a **Identity Anchor** (nome UI da affinare).

L'anchor non significa che tutte le immagini debbano essere copie. Significa che i tratti identitari devono essere mantenuti mentre possono cambiare:

- posa;
- espressione;
- abbigliamento se consentito;
- camera/viewpoint;
- ambiente;
- azione;
- composizione.

Per personaggi con abbigliamento canonico anche l'abbigliamento può diventare HARD.

## 5. Prompt Pack cross-lotto

Ogni Work Unit che usa un soggetto Consistent deve ricevere:

- la descrizione canonica corrente;
- gli HARD identity locks;
- i partecipanti della scena;
- l'eventuale Identity Anchor/reference;
- le sole variazioni ammesse per quella scena.

Il `PromptPackId` o lotto non deve essere la sorgente della consistency. La sorgente è il profilo persistente nel progetto.

## 6. Rigenerazione correttiva

La strada Avalonia `Da rifare → nuovo pacchetto solo mancanti/da rifare` viene mantenuta e potenziata.

Per una Candidate fallita l'utente deve poter scegliere:

- **Rigenera mantenendo identità** — stessa Work Unit, stesso SubjectId/SceneId e stesso Identity Anchor, con correzioni aggiuntive;
- **Rigenera da prompt aggiornato** — ricompila dalle scelte canoniche attuali;
- **Crea variante** — nuova Candidate della stessa Work Unit, non sostituzione silenziosa;
- **Cambia reference/anchor** — operazione esplicita che può rendere stale altre Candidate da controllare.

La storia delle Candidate deve restare consultabile.

## 7. Correzione guidata dal controllo/Vision

Quando Vision o l'utente rilevano un problema, Diez può costruire un **Correction Brief** separato dal prompt base.

Esempi:

- volto non coerente con l'anchor;
- manca un partecipante;
- colore/segno/stile errato;
- posa impossibile;
- elemento da rimuovere;
- linee troppo sottili;
- scena semanticamente sbagliata.

Il Correction Brief deve dire cosa correggere e cosa **non cambiare**, soprattutto identità e composizione già approvate.

## 8. Provider con e senza image reference

### Provider con reference/variation/edit

Usare l'Identity Anchor come input visuale quando supportato, più prompt/Correction Brief.

### Provider solo testo

Compilare una descrizione identitaria più rigorosa, includendo tratti distintivi e HARD locks; la reliability sarà inferiore e deve essere indicata come tale.

Diez non deve promettere consistency perfetta se il provider non offre meccanismi adatti.

## 9. Multiple identity anchors

Per soggetti complessi può essere utile più di un anchor:

- frontale;
- tre quarti;
- figura intera;
- dettaglio volto;
- abbigliamento/oggetto distintivo.

Non obbligatorio per tutti i progetti: è una capability avanzata.

## 10. Scene e partecipanti

`Scene e soggetti` va quindi riprogettato in Produzione come editor di contenuto, non pannello tecnico.

Per ogni scena:

- quali soggetti partecipano;
- ruolo/azione;
- ambiente;
- eventuale relazione spaziale;
- quali Subject Profile devono essere Consistent.

Il Prompt Compiler consuma queste relazioni senza esporre ID tecnici all'utente.

## 11. Consistency tra libri/edizioni

Per ora il contratto HARD è **all'interno dello stesso progetto `.diez`**.

In futuro un Subject Profile potrebbe essere esportabile/importabile come asset/preset tra progetti, ma non va introdotto implicitamente senza una gestione esplicita di provenienza/versione.

## 12. Acceptance test obbligatorio

Prima di dichiarare pronta la consistency cross-lotto:

1. crea personaggio Consistent con anchor approvato;
2. genera lotto A con almeno 3 scene;
3. chiudi/salva/riapri progetto;
4. crea lotto B separato con nuove scene dello stesso personaggio;
5. verifica che tutte le Work Unit del lotto B portino lo stesso profilo/anchor;
6. importa Response B;
7. marca una Candidate `Da rifare` per identity mismatch;
8. crea correction pack solo per quella Work Unit;
9. rigenera mantenendo anchor e parti corrette;
10. importa nuova Candidate senza perdere la precedente;
11. Vision/review confronta identità con anchor;
12. modifica esplicitamente l'anchor e verifica segnalazione di elementi stale da ricontrollare.

## 13. Principio

**La consistency appartiene al soggetto canonico del progetto, non al batch. Il batch è soltanto un trasporto.**
