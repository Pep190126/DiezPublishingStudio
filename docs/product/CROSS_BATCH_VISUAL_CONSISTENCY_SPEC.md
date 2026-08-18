# Visual consistency tra lotti diversi

Status: **PROPOSTA ARCHITETTURALE / WORKING — DA VALIDARE CON L'UTENTE E POI FISICAMENTE**

Data: 2026-08-19

## 1. Problema

La consistency dentro un singolo lotto è relativamente semplice: più Work Unit possono condividere lo stesso profilo, le stesse regole e le stesse reference nel medesimo Prompt Pack.

La difficoltà reale è la **consistency nel tempo**:

- un personaggio viene generato oggi in un lotto;
- alcune immagini vengono approvate;
- settimane dopo si crea un altro lotto;
- il personaggio deve rimanere riconoscibilmente lo stesso anche se cambiano scena, posa, ambiente o provider.

Un semplice testo `mantieni lo stesso personaggio` non è un contratto abbastanza forte.

## 2. Cosa aveva già Avalonia

La linea Avalonia possedeva un buon meccanismo di rettifica:

- risultato associato a un job stabile;
- `Approva` / `Da rifare` / `Scarta`;
- `Da rifare` mantiene il job e consente di modificare la richiesta;
- `Ricrea prompt` ricompila il lavoro;
- per le serie immagini il pacchetto successivo poteva contenere soltanto immagini mancanti o `NeedsRevision`;
- gli elementi mantenevano lo stesso codice `IMG-###` anche fra ZIP parziali/correzioni.

Questa è una base valida per il **versioning e la rettifica**, ma non è da sola una garanzia sufficiente di identità visiva fra lotti separati.

## 3. Soluzione proposta: Identità Consistent canonica

Ogni soggetto/personaggio marcato **Consistent** deve possedere un'identità canonica persistente nel `.diez`.

Concettualmente:

- `SubjectId` stabile;
- nome/label editoriale modificabile;
- descrizione semantica dell'identità;
- caratteristiche che non devono cambiare;
- caratteristiche che possono variare;
- reference visuali canoniche;
- eventuali negative constraints;
- stato della reference (`Candidate`, `Approved identity reference`, `Archived`).

Il nome visibile non è la chiave: rinominare un personaggio non rompe la sua identità.

## 4. Reference Master del soggetto

Quando l'utente approva una rappresentazione particolarmente corretta di un personaggio, deve poterla promuovere a:

**Reference Master / Identità visiva approvata**.

Può essere una o più immagini, ad esempio:

- vista principale;
- volto/espressione neutra;
- corpo/proporzioni;
- abbigliamento/accessori canonici;
- eventuale vista laterale o posa utile.

Non è obbligatorio avere più immagini: una sola reference approvata può essere sufficiente per iniziare.

La Reference Master è distinta dalla semplice immagine `approvata per il libro`: può essere usata come sorgente identitaria in lotti futuri.

## 5. Regole di identità: invarianti e variabili

Il profilo Consistent distingue esplicitamente:

### Invarianti

Esempi:

- specie / tipo di personaggio;
- forma del volto;
- proporzioni fondamentali;
- capelli/pelliccia;
- segni distintivi;
- palette o trattamento B/N quando rilevante;
- accessori canonici;
- età apparente;
- stile grafico fondamentale.

### Variabili consentite

Esempi:

- posa;
- espressione;
- azione;
- inquadratura;
- ambiente;
- abbigliamento quando la scena lo richiede e non è canonico;
- illuminazione/colore nelle famiglie che lo consentono.

Il Prompt Compiler deve trasformare questa distinzione in istruzioni semantiche, non riversare ID tecnici nel prompt provider-facing.

## 6. Ogni nuovo lotto riusa la stessa radice identitaria

Quando una Work Unit usa un `SubjectId` Consistent, il compilatore deve includere nello snapshot del lavoro:

- profilo identitario corrente;
- reference master approvate applicabili;
- eventuale ultima versione canonica;
- regole specifiche della scena;
- relazione fra personaggi presenti.

Quindi un nuovo Prompt Pack creato mesi dopo non riparte da zero.

La consistency non appartiene al `BatchId`: appartiene al **SubjectId canonico**.

## 7. Rettifica di un'immagine senza cambiare soggetto

Il workflow Avalonia `Da rifare → modifica richiesta → Ricrea prompt` viene preservato e rafforzato.

Per una Candidate fallita:

1. l'utente seleziona `Da rifare`;
2. indica il problema (`mano errata`, `personaggio troppo diverso`, `manca X`, ecc.);
3. Diez conserva Work Unit, SubjectId e storia delle versioni;
4. il nuovo prompt contiene la **delta correction**;
5. riusa la Reference Master e gli invarianti Consistent;
6. chiede esplicitamente di correggere il difetto senza ridisegnare arbitrariamente l'identità;
7. il nuovo risultato entra come nuova Candidate/versione;
8. il precedente non viene cancellato dalla cronologia.

Questa è la naturale evoluzione del meccanismo Avalonia.

## 8. Correzione vs nuova scena

Due casi devono essere distinti:

### Correzione della stessa unità

- stesso scopo editoriale;
- stessa Work Unit;
- stesso SubjectId;
- nuova Candidate/versione;
- prompt = base + reference + delta correction.

### Nuova immagine in un lotto futuro

- nuova Work Unit;
- nuova scena/azione;
- stesso SubjectId;
- stessa radice Consistent/Reference Master.

In entrambi i casi l'identità resta comune.

## 9. Provider independence

Il contratto Diez non può dipendere dal fatto che un provider possieda una funzione proprietaria chiamata `character reference`, `seed`, `edit`, `image-to-image` o simile.

Il profilo canonico conserva:

- identità semantica;
- reference asset;
- relazioni;
- storico versioni.

L'adapter/provider, quando esisterà, sfrutta le capacità native disponibili:

- reference image;
- image edit;
- mask/inpainting;
- seed o character reference;
- multimodal prompt.

Se il trasporto è manuale, il Prompt Pack deve comunque includere reference e istruzioni sufficienti a consentire all'utente di allegarle alla AI scelta.

## 10. Reference scope

Una reference può avere scope:

- **Subject** — identità del personaggio/soggetto;
- **Style** — stile visuale;
- **Scene** — ambiente/composizione;
- **Object** — oggetto canonico;
- **Book/Series** — coerenza generale.

Questo evita di usare una sola immagine per scopi incompatibili.

## 11. Vision / controllo consistency cross-batch

Vision non deve confrontare una Candidate solo con le altre immagini del lotto corrente.

Quando è attiva la consistency:

- carica il profilo canonico del SubjectId;
- usa le Reference Master;
- verifica i tratti invarianti;
- distingue cambi consentiti da identity drift;
- produce un controllo `subject_identity_match` HARD quando richiesto.

Una Candidate può essere coerente con il lotto corrente ma incoerente con il personaggio canonico: in quel caso deve fallire.

## 12. Aggiornamento della Reference Master

Non sostituire automaticamente la reference perché una nuova immagine è stata approvata.

Azioni esplicite:

- `Usa come nuova Reference Master`;
- `Aggiungi alle reference del soggetto`;
- `Mantieni solo come immagine del libro`;
- `Archivia reference precedente`.

Cambiare Reference Master è una modifica canonica e rende potenzialmente stale i Prompt non ancora prodotti.

## 13. Conflitti di identità

Se più reference approvate divergono troppo:

- non scegliere silenziosamente;
- segnalare conflitto;
- mostrare le reference;
- chiedere quale rappresentazione è autoritativa oppure consentire di definire quali caratteristiche restano invarianti.

## 14. UI proposta dentro Produzione

Scene/Soggetti viene riprogettato come superficie editoriale con almeno due tab/sezioni correlate:

### Soggetti / Personaggi

- elenco con ID nascosto tecnicamente ma stabile;
- descrizione;
- `Consistent ON/OFF`;
- invarianti;
- variabili consentite;
- gallery Reference Master;
- aggiungi reference da materiali / Candidate approvata;
- preview grande.

### Scene

- scena;
- descrizione;
- ambiente;
- partecipanti scelti dall'elenco soggetti;
- azione/relazione;
- override locali;
- preview/reference di scena.

Queste sezioni devono comparire prima della compilazione del Prompt quando la famiglia le usa.

## 15. Dati da conservare nel Prompt Pack

Il manifest tecnico può contenere gli ID canonici necessari alla tracciabilità.

Il prompt provider-facing contiene invece linguaggio semantico:

- chi è il soggetto;
- quali tratti non cambiano;
- cosa cambia in questa scena;
- quali reference usare.

Gli asset reference necessari vengono inclusi nel pack con mapping esplicito per Work Unit/Subject.

## 16. Acceptance test futuro

Caso minimo obbligatorio:

1. crea personaggio `A` Consistent;
2. genera lotto 1 con almeno 3 immagini;
3. approva una Candidate e promuovila a Reference Master;
4. chiudi e riapri il `.diez`;
5. crea lotto 2 con scene differenti;
6. verifica che il pack del lotto 2 includa la stessa identità/reference;
7. importa Response lotto 2;
8. Vision identifica una Candidate con identity drift;
9. segna `Da rifare` con nota di correzione;
10. genera solo quell'unità mantenendo SubjectId/reference;
11. importa nuova Candidate;
12. approva senza perdere versioni precedenti;
13. crea un terzo lotto in una sessione successiva e ripeti la consistency.

Il test deve dimostrare consistency **fra lotti e fra sessioni**, non solo dentro un singolo ZIP.

## 17. Decisione proposta

La strada raccomandata è quindi:

**recuperare il workflow Avalonia di rettifica/versioning + introdurre un'identità Consistent persistente con Reference Master canonica.**

È più robusto di tentare di ottenere consistency affidandosi soltanto al testo del prompt o al fatto che le immagini appartengano allo stesso batch.